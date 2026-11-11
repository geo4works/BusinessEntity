using BusinessEntity.Infrastructure.Bus;
using Microsoft.AspNetCore.Mvc;

namespace BusinessEntity.Infrastructure.Api;

// HTTP статус не передаётся как право: код ожидаемого отказа сохраняется в предметном ответе.
public sealed class ApiFault(int status,string code,string message,Guid? operationId=null) : PublicOperationException(code,message)
{
    public int Status {get;}=status;
    public Guid? OperationId {get;}=operationId;
}

// Единственная внешняя проекция ошибок не раскрывает SQL, stack или входные credentials.
public static class ApiProblems
{
    // Отсутствующий токен, отказ предметного права и неизвестный ресурс имеют разные статусы.
    public static int Status(string code)=>code switch
    {
        "record.not_found" or "api.operation_not_found"=>404,
        "access.denied" or "api.grant_denied" or "api.capability_denied" or "api.space_denied"=>403,
        _ when code.StartsWith("api.session_")||code=="api.identity_invalid"=>401,
        _ when code.Contains("conflict")=>409,
        "payload.unsupported"=>415,
        "upload.too_large"=>413,
        "validation.failed" or "payload.invalid"=>400,
        _=>503
    };
    // ProblemDetails содержит только безопасную корреляцию и стабильную причину.
    public static Task WriteAsync(HttpContext http,int status,string code,string message,Guid? operationId=null)
    {
        http.Response.StatusCode=status;
        var problem=new ProblemDetails {Status=status,Title=message,Type="about:blank"};
        problem.Extensions["code"]=code;problem.Extensions["requestId"]=http.TraceIdentifier;
        if(operationId.HasValue){problem.Extensions["operationId"]=operationId;problem.Extensions["statusUrl"]="/api/v1/operations/"+operationId;}
        return http.Response.WriteAsJsonAsync(problem,options:(System.Text.Json.JsonSerializerOptions?)null,contentType:"application/problem+json",cancellationToken:http.RequestAborted);
    }
}

// V1 pipeline полностью отделён от HTML redirects, cookie space context и внутренних исключений.
public sealed class ApiErrorMiddleware(RequestDelegate next,ILogger<ApiErrorMiddleware> logger)
{
    // Ограничение тела применяется до десериализации; transport uploads объявят собственный предел.
    public async Task InvokeAsync(HttpContext http,BusRequestScope scope,ApiTransportLimits limits,ApiConcurrencyGate gate)
    {
        if(!http.Request.Path.StartsWithSegments("/api/v1")){await next(http);return;}
        scope.MarkApiRequest();
        try
        {
            using var accepted=gate.Enter();
            var maximum=http.GetEndpoint()?.Metadata.GetMetadata<ApiBinaryRequestLimit>()?.MaxBytes??limits.MaxJsonBytes;
            if(http.Request.ContentLength>maximum)throw new ApiFault(413,"api.body_too_large","Превышен размер запроса.");
            var bodyLimit=http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if(bodyLimit is {IsReadOnly:false})bodyLimit.MaxRequestBodySize=maximum;
            await next(http);
        }
        catch(Exception exception)when(!http.Response.HasStarted&&exception is not OperationCanceledException)
        {
            if(exception is ApiFault fault){if(fault.Status==429)http.Response.Headers.RetryAfter="1";await ApiProblems.WriteAsync(http,fault.Status,fault.Code,fault.Message,fault.OperationId);return;}
            if(exception is BadHttpRequestException {StatusCode:413}){await ApiProblems.WriteAsync(http,413,"api.body_too_large","Превышен размер запроса.");return;}
            var error=BusError.FromException(exception);
            if(exception is not (BusRequestFailedException or ArgumentException or UnauthorizedAccessException))
                logger.LogWarning("API request {RequestId} failed: {ErrorType}",http.TraceIdentifier,exception.GetType().Name);
            await ApiProblems.WriteAsync(http,ApiProblems.Status(error.Code),error.Code,error.Message);
        }
    }
}

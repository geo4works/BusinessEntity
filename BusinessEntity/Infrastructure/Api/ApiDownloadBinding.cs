using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.Infrastructure.Api;

public sealed record ApiFileDownload(Guid LeaseId,string FileName,string ContentType,long Length);
public static class ApiDownloadRegistration
{
    // Ответ владельца содержит opaque byte-lease; HTTP adapter не видит storage path/модель сущности.
    public static void AddApiDownload<TRequest,TResponse>(this IServiceCollection services,ApiCapability definition,Func<HttpRequest,TRequest> request,Func<TResponse,ApiFileDownload> project)
        where TRequest:BusRequest where TResponse:BusResponse=>services.AddSingleton<IApiRouteBinding>(new DownloadBinding<TRequest,TResponse>(definition,request,project));
    private sealed class DownloadBinding<TRequest,TResponse>(ApiCapability definition,Func<HttpRequest,TRequest> request,Func<TResponse,ApiFileDownload> project):IApiRouteBinding where TRequest:BusRequest where TResponse:BusResponse
    {
        public ApiCapability Definition=>definition;
        public void Map(IEndpointRouteBuilder endpoints)=>endpoints.MapGet("/api/v1"+definition.Route,async(HttpContext http,ApiRequestDispatcher dispatcher,IApiStagingDataPlane plane)=>
        {var response=await dispatcher.SendAsync<TRequest,TResponse>(http,definition.Id,request(http.Request),http.RequestAborted);var download=project(response);return Results.Stream(plane.OpenRead(download.LeaseId),download.ContentType,download.FileName,enableRangeProcessing:true);})
            .RequireAuthorization("ExternalApi").RequireRateLimiting("external-api").WithName(definition.Id).WithMetadata(definition).WithTags("External API v1").Produces(200,contentType:"application/octet-stream").Produces(206,contentType:"application/octet-stream");
    }
}

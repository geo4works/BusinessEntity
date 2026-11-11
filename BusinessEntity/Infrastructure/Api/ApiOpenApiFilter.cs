using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi.Any;

namespace BusinessEntity.Infrastructure.Api;

// OpenAPI описывает действительный Bearer/session contract, не обычную browser cookie.
public sealed class ApiOpenApiFilter:IOperationFilter
{
    // Только v1 API endpoints получают API security requirements и session header.
    public void Apply(OpenApiOperation operation,OperationFilterContext context)
    {
        var path=context.ApiDescription.RelativePath??string.Empty;if(!path.StartsWith("api/v1/",StringComparison.Ordinal))return;
        operation.Security=[new OpenApiSecurityRequirement {{new OpenApiSecurityScheme {Reference=new(){Id="Bearer",Type=ReferenceType.SecurityScheme}},Array.Empty<string>()}}];
        if(!(path=="api/v1/sessions"&&context.ApiDescription.HttpMethod=="POST"))operation.Parameters.Add(new(){Name="X-BE-Session-Id",In=ParameterLocation.Header,Required=true,Schema=new(){Type="string",Format="uuid"},Description="Серверная сессия; каждый запрос также требует действующий access token."});
        var capability=context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<ApiCapability>().SingleOrDefault();
        var input=context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<ApiJsonBodySchema>().SingleOrDefault();
        if(input!=null)operation.RequestBody=new(){Required=input.Required,Content=new Dictionary<string,OpenApiMediaType>{{"application/json",new(){Schema=context.SchemaGenerator.GenerateSchema(input.InputType,context.SchemaRepository)}}}};
        if(context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<ApiBinaryRequestLimit>().Any())
        {operation.RequestBody=new(){Required=true,Content=new Dictionary<string,OpenApiMediaType>{{"application/octet-stream",new(){Schema=new(){Type="string",Format="binary"}}}}};operation.Parameters.Add(new(){Name="X-Part-SHA256",In=ParameterLocation.Header,Required=true,Schema=new(){Type="string",Pattern="^[A-Fa-f0-9]{64}$"},Description="SHA-256 bytes части; Content-Length обязателен."});}
        foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(path,"\\{([^}:]+)(?::[^}]+)?\\}"))
        {var name=match.Groups[1].Value;if(!operation.Parameters.Any(p=>p.In==ParameterLocation.Path&&p.Name==name))operation.Parameters.Add(new(){Name=name,In=ParameterLocation.Path,Required=true,Schema=new(){Type=name=="version"||name=="partNumber"?"integer":"string",Format=name=="version"||name=="partNumber"?"int32":name.EndsWith("Id",StringComparison.OrdinalIgnoreCase)||name=="id"?"uuid":null}});}
        if(capability!=null)
        {
            operation.Extensions["x-be-capability"]=new OpenApiString(capability.Id);operation.Extensions["x-be-input-schema"]=new OpenApiString(capability.InputSchema);operation.Extensions["x-be-output-schema"]=new OpenApiString(capability.OutputSchema);
            if(capability.Mutates)operation.Parameters.Add(new(){Name="Idempotency-Key",In=ParameterLocation.Header,Required=true,Schema=new(){Type="string",MinLength=1,MaxLength=128,Pattern="^[A-Za-z0-9_.:-]+$"},Description="Устойчивый ключ данного subject/client/capability, гарантированный повтор 7 дней."});
        }
        var problem=new OpenApiSchema{Type="object",Required=new HashSet<string>{"status","code","requestId"},Properties=new Dictionary<string,OpenApiSchema>{{"status",new(){Type="integer"}},{"title",new(){Type="string"}},{"code",new(){Type="string"}},{"requestId",new(){Type="string"}},{"operationId",new(){Type="string",Format="uuid"}},{"statusUrl",new(){Type="string"}}}};
        foreach(var status in new[]{"400","401","403","404","409","413","415","429","503"})operation.Responses.TryAdd(status,new(){Description="Стабильная ошибка API, без SQL/stack/credentials",Content=new Dictionary<string,OpenApiMediaType>{{"application/problem+json",new(){Schema=problem}}}});
    }
}

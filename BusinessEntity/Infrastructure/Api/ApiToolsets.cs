using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using ReactiveUI;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.Infrastructure.Api;

// Декларация capability описывает только фиксированный типизированный HTTP/bus contract.
public sealed record ApiCapability(string ToolsetId,int ToolsetVersion,string Id,int Version,string OwnerMiniAppId,
    string Title,string Method,string Route,string InputSchema,string OutputSchema,bool Mutates=false,ApiWriteMode WriteMode=ApiWriteMode.Canonical);
public sealed record GiveMeApiToolsets(Guid RequestId);
public sealed record HereAreApiToolsets(Guid RequestId,string OwnerMiniAppId,IReadOnlyList<ApiCapability> Capabilities);
public sealed record ApiEmptyInput;
// Локальный OpenAPI metadata type не участвует в маршрутизации по Content-Type и не передаётся по шине.
public sealed record ApiJsonBodySchema(Type InputType,bool Required);

// Сигнатура публикации маршрута не позволяет выбирать произвольный сервис или CLR-метод.
public interface IApiRouteBinding
{
    ApiCapability Definition {get;}
    void Map(IEndpointRouteBuilder endpoints);
}

// Metadata передаётся по единственной ReactiveUI шине, а runtime binding остаётся локальным типизированным объектом.
public sealed class ApiToolsetHost : IDisposable
{
    private readonly IDisposable _subscription;
    public string Owner {get;}
    // Registration владельца предоставляет только собственные декларации.
    public ApiToolsetHost(IMessageBus bus,string owner,IEnumerable<ApiCapability> definitions)
    {Owner=owner;var snapshot=definitions.ToArray();_subscription=bus.Listen<GiveMeApiToolsets>().Subscribe(r=>bus.SendMessage(new HereAreApiToolsets(r.RequestId,owner,snapshot)));}
    // Остановка сервера освобождает единственную подписку владельца.
    public void Dispose()=>_subscription.Dispose();
}

// Каталог после старта фиксирован; дубликаты и отсутствующие bindings являются ошибкой запуска.
public sealed class ApiCapabilityCatalog(IMessageBus bus,IEnumerable<ApiToolsetHost> owners,IEnumerable<IApiRouteBinding> bindings)
{
    private IReadOnlyList<ApiCapability> _capabilities=Array.Empty<ApiCapability>();
    public IReadOnlyList<ApiCapability> Definitions=>_capabilities;
    // Приём HereAre начинается раньше GiveMe; все владельцы отвечают на один correlation ID.
    public Task InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var hosts=owners.ToArray();var routes=bindings.ToArray();var id=Guid.NewGuid();var replies=new Dictionary<string,HereAreApiToolsets>();
        using var subscription=bus.Listen<HereAreApiToolsets>().Where(x=>x.RequestId==id).Subscribe(x=>replies.Add(x.OwnerMiniAppId,x));
        bus.SendMessage(new GiveMeApiToolsets(id));
        if(replies.Count!=hosts.Length)throw new InvalidOperationException("Не все владельцы объявили API toolsets.");
        if(replies.Values.Any(reply=>reply.Capabilities.Any(x=>x.OwnerMiniAppId!=reply.OwnerMiniAppId)))throw new InvalidOperationException("Чужой владелец API capability.");
        var definitions=replies.Values.SelectMany(x=>x.Capabilities).ToArray();
        if(definitions.Length!=routes.Length||definitions.Any(x=>x.Version<1||x.ToolsetVersion<1||!new[]{"GET","POST","PUT","PATCH","DELETE"}.Contains(x.Method)
            ||!x.Route.StartsWith('/')||x.Route.StartsWith("//")||x.Route.IndexOfAny(['?','#'])>=0||new[]{"/sessions","/operations","/capabilities"}.Any(path=>x.Route==path||x.Route.StartsWith(path+"/",StringComparison.Ordinal)))
            ||definitions.GroupBy(x=>x.ToolsetId).Any(g=>g.Select(x=>x.OwnerMiniAppId).Distinct().Count()>1)
            ||definitions.GroupBy(x=>x.Id).Any(x=>x.Count()!=1)||routes.GroupBy(x=>x.Definition.Id).Any(x=>x.Count()!=1)
            ||definitions.GroupBy(x=>x.Method+" "+System.Text.RegularExpressions.Regex.Replace(x.Route,"\\{[^}]+\\}","{}")).Any(x=>x.Count()!=1)
            ||definitions.Any(x=>!routes.Any(r=>r.Definition==x)))throw new InvalidOperationException("Дубликат или отсутствие API binding.");
        if(definitions.SelectMany(x=>new[]{x.InputSchema,x.OutputSchema}).GroupBy(x=>x).Any(g=>g.Count()>1))throw new InvalidOperationException("API schema identifiers должны быть уникальны.");
        _capabilities=Array.AsReadOnly(definitions.OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray());return Task.CompletedTask;
    }
    // HTTP map создаёт только зарегистрированные production routes.
    public void Map(IEndpointRouteBuilder endpoints){foreach(var binding in bindings)binding.Map(endpoints);}
}

// Каждый вызов повторно проверяет User security session до отправки конкретного предметного запроса.
public sealed class ApiRequestDispatcher(IUserApiConnector security,BusRequestClient client)
{
    // Session header не является credential: проверенный Bearer principal уже установлен протоколом.
    public async Task<ApiSessionInfo> SessionAsync(HttpContext http,string? capability,CancellationToken ct)
    {if(!Guid.TryParse(http.Request.Headers["X-BE-Session-Id"],out var id))throw new ApiFault(401,"api.session_required","Требуется X-BE-Session-Id.");return await security.ValidateAsync(id,capability,ct);}
    // Шина получает TRequest/TResponse, а не string Type + object Payload.
    public async Task<TResponse> SendAsync<TRequest,TResponse>(HttpContext http,string capability,TRequest request,CancellationToken ct)
        where TRequest:BusRequest where TResponse:BusResponse
    {await SessionAsync(http,capability,ct);return await client.SendAsync<TRequest,TResponse>(request,ct);}
}

// Один generic transport adapter не реализует бизнес-CRUD и не вызывает чужие Internal.
internal sealed class ApiRouteBinding<TInput,TRequest,TResponse,TOutput>(ApiCapability definition,
    Func<TInput,HttpRequest,TRequest> request,Func<TResponse,TOutput> project):IApiRouteBinding
    where TInput:notnull where TRequest:BusRequest where TResponse:BusResponse
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectNullableAnnotations=true,RespectRequiredConstructorParameters=true};
    public ApiCapability Definition=>definition;
    // Minimal API получает только scoped dispatcher и HttpContext; input type известен при регистрации.
    public void Map(IEndpointRouteBuilder endpoints)
    {
        if(definition.Mutates&&definition.WriteMode==ApiWriteMode.Canonical&&typeof(TOutput)!=typeof(ApiMutationOutcome))throw new InvalidOperationException("Canonical API binding возвращает ApiMutationOutcome.");
        var route=endpoints.MapMethods("/api/v1"+definition.Route,[definition.Method],async(HttpContext http,ApiRequestDispatcher dispatcher,ApiOperationExecutor operations)=>
        {
            TInput input;
            if(definition.Method=="GET"||http.Request.ContentLength==0||http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>()?.CanHaveBody==false)input=JsonSerializer.Deserialize<TInput>("{}",Json)!;
            else
            {
                if(http.Request.ContentType?.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)!=true)throw new ApiFault(415,"payload.unsupported","Требуется application/json.");
                input=await http.Request.ReadFromJsonAsync<TInput>(Json,http.RequestAborted)??throw new ArgumentException("Не заполнено тело запроса.");
            }
            var typed=request(input,http.Request);
            if(definition.Mutates)
            {
                var session=await dispatcher.SessionAsync(http,definition.Id,http.RequestAborted);
                if(definition.WriteMode!=ApiWriteMode.Canonical)
                {
                    var registered=await operations.ExecuteRegisteredAsync<TRequest,TResponse>(http,definition,typed,session,http.RequestAborted);
                    return Results.Json(project(registered),statusCode:definition.WriteMode==ApiWriteMode.Deferred?202:200);
                }
                return Results.Json(await operations.ExecuteAsync<TRequest,TResponse>(http,definition,typed,session,http.RequestAborted));
            }
            var response=await dispatcher.SendAsync<TRequest,TResponse>(http,definition.Id,typed,http.RequestAborted);
            return Results.Json(project(response));
        }).RequireAuthorization("ExternalApi").RequireRateLimiting("external-api").WithName(definition.Id).WithMetadata(definition).WithTags("External API v1").Produces<TOutput>(definition.WriteMode==ApiWriteMode.Deferred?202:200);
        if(definition.Method!="GET")route.WithMetadata(new ApiJsonBodySchema(typeof(TInput),typeof(TInput)!=typeof(ApiEmptyInput)));
    }
}

// Registration владельца связывает только публичные typed Request/Response и DTO.
public static class ApiToolsetRegistration
{
    // Проекции являются локальным transport binding; в bus payload делегатов нет.
    public static IServiceCollection AddApiBinding<TInput,TRequest,TResponse,TOutput>(this IServiceCollection services,
        ApiCapability definition,Func<TInput,HttpRequest,TRequest> request,Func<TResponse,TOutput> project)
        where TInput:notnull where TRequest:BusRequest where TResponse:BusResponse
    {services.AddSingleton<IApiRouteBinding>(new ApiRouteBinding<TInput,TRequest,TResponse,TOutput>(definition,request,project));return services;}
    // Host объявляет только metadata своих bindings; композиционный корень вызывает Registration владельца.
    public static IServiceCollection AddApiToolset(this IServiceCollection services,string owner)
    {services.AddSingleton(p=>new ApiToolsetHost(p.GetRequiredService<IMessageBus>(),owner,p.GetServices<IApiRouteBinding>().Where(x=>x.Definition.OwnerMiniAppId==owner).Select(x=>x.Definition)));return services;}
}

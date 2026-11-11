using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.Infrastructure.Modularity;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Claims;

namespace BusinessEntity.MiniApps.ExternalApiMiniApp.Registration;

// Логический миниапп предоставляет external transport и каталог, предметные владельцы регистрируют свои toolsets.
public static class ExternalApiMiniAppRegistration
{
    // Один request scope на HTTP; никакого UI renderer или второго IMessageBus здесь нет.
    public static IServiceCollection AddExternalApiMiniApp(this IServiceCollection services)
    {
        services.AddApiBearer();services.AddSingleton<ApiCapabilityCatalog>();services.AddScoped<ApiRequestDispatcher>();
        services.AddSingleton<ApiOperationGate>();services.AddScoped<ApiOperationExecutor>();services.AddScoped<ApiWriteContext>();
        services.AddScoped<ApiDomainScope>();services.AddSingleton<ApiPagination>();
        services.AddSingleton<ApiTransportLimits>();services.AddSingleton<ApiConcurrencyGate>();
        services.AddRateLimiter(_=>{});
        services.AddOptions<RateLimiterOptions>().Configure<ApiTransportLimits>((options,limits)=>
        {
            options.RejectionStatusCode=429;
            options.AddPolicy("external-api",http=>RateLimitPartition.GetFixedWindowLimiter(
                (http.User.FindFirstValue(IdentityClaimNames.UserId)??http.Connection.RemoteIpAddress?.ToString()??"unknown")+":"+http.User.FindFirstValue(IdentityClaimNames.ClientId),
                _=>new FixedWindowRateLimiterOptions {PermitLimit=limits.RequestsPerMinute,QueueLimit=0,Window=TimeSpan.FromMinutes(1),AutoReplenishment=true}));
            options.OnRejected=async(context,ct)=>{context.HttpContext.Response.Headers.RetryAfter="60";await ApiProblems.WriteAsync(context.HttpContext,429,"api.rate_limited","Превышен лимит запросов.");};
        });
        services.AddMiniAppHost("ExternalApiMiniApp",new ModuleDefinition("ExternalApiMiniApp","catalog","External API",null,ModulePlacement.Headless,ModuleInstancePolicy.AppSingleton,Order:70,OpenOnStartup:true));
        services.AddModuleRuntime<ApiCatalogRuntime>("ExternalApiMiniApp:catalog");return services;
    }
    // Композиционный корень публикует только фиксированный проверенный набор bindings.
    public static void MapExternalApi(this IEndpointRouteBuilder endpoints)=>endpoints.ServiceProvider.GetRequiredService<ApiCapabilityCatalog>().Map(endpoints);
}

// Каталог фиксируется после деклараций владельцев в установленном startup порядке.
internal sealed class ApiCatalogRuntime(ApiCapabilityCatalog catalog):IModuleRuntime
{
    public Task StartAsync(CancellationToken ct)=>catalog.InitializeAsync(ct);
    public Task StopAsync(CancellationToken ct)=>Task.CompletedTask;
}

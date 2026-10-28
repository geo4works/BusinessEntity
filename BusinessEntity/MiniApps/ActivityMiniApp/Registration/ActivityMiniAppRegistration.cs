using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.ActivityMiniApp.Connectors;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.ActivityMiniApp.Internal;

namespace BusinessEntity.MiniApps.ActivityMiniApp.Registration;

/// <summary>
/// Регистрирует ActivityMiniApp и его публичный connector в DI.
/// </summary>
public static class ActivityMiniAppRegistration
{
    // Подключает внутренний сервис, фасад и connector ActivityMiniApp.
    public static IServiceCollection AddActivityMiniApp(this IServiceCollection services)
    {
        services.AddScoped<ActivityCommentService>();
        services.AddScoped<IActivityMiniApp, Facade.ActivityMiniApp>();
        services.AddScoped<IActivityConnector, ActivityConnector>();

        services.AddScoped<ActivityMiniAppMessageHandler>();

            services.AddBusEndpoint<ActivityMiniAppMessageHandler>(typeof(GetCommentsRequest), typeof(CreateCommentRequest));

            return services;
    }
}

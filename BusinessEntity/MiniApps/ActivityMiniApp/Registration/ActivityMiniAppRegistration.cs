using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Modularity;
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
        services.AddScoped<IActivityConnector, ActivityConnector>();

        services.AddScoped<ActivityMiniAppMessageHandler>();

            services.AddBusEndpoint<ActivityMiniAppMessageHandler>(typeof(GetCommentsRequest), typeof(CreateCommentRequest));

            services.AddMiniAppHost("ActivityMiniApp", new ModuleDefinition("ActivityMiniApp", "Activity.Comments", "Комментарии", null,
                ModulePlacement.Page, ModuleInstancePolicy.PerContext, "activity.comments", RequiredPermission: "ReadContent"));
            services.AddModuleComponent<BusinessEntity.MiniApps.ActivityMiniApp.Components.BusinessEntityCommentsSection>("activity.comments");
            services.AddMiniAppButtons(new ButtonDefinition("ActivityMiniApp", "comment.submit", "Document.Toolbar", "Отправить", RequiredPermission: "ReadContent"));
            return services;
    }
}

using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.TreeMiniApp.Connectors;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.TreeMiniApp.Internal;

namespace BusinessEntity.MiniApps.TreeMiniApp.Registration
{
    // Регистрирует mini-app дерева.
    public static class TreeMiniAppRegistration
    {
        public static IServiceCollection AddTreeMiniApp(this IServiceCollection services)
        {
            services.AddScoped<TreeVisibilityService>();
            services.AddScoped<TreeMiniAppService>();
            services.AddScoped<TreeMiniAppMessageHandler>();
            services.AddScoped<ITreeMiniApp, Facade.TreeMiniApp>();
            services.AddScoped<ITreeConnector, TreeConnector>();

            services.AddBusEndpoint<TreeMiniAppMessageHandler>(typeof(GetTreeForSpaceRequest));
            return services;
        }
    }
}

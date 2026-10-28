using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Classes;
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
            services.AddSingleton(new EntityTypeOwnership(BusinessEntityTypeEnum.Folder, "TreeMiniApp"));
            services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(BusinessEntityTypeEnum.Folder, false));
            services.AddMiniAppHost("TreeMiniApp", new ModuleDefinition("TreeMiniApp", "tree", "Дерево", "account_tree",
                ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "tree.main", "LeftTree", OpenOnStartup: true));
            services.AddModuleComponent<BusinessEntity.MiniApps.TreeMiniApp.Components.TreeMiniAppComponent>("tree.main");
            return services;
        }
    }
}

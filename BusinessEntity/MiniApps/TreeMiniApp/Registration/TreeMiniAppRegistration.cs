using BusinessEntity.Core.Types;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.TreeMiniApp.Internal.Serialization;
using BusinessEntity.Infrastructure.Commands;
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
            services.AddScoped<ITreeSelectionService, TreeSelectionService>();
            services.AddScoped<TreeVisibilityService>();
            services.AddScoped<TreeMiniAppService>();
            services.AddScoped<TreeMiniAppMessageHandler>();
            services.AddScoped<ITreeConnector, TreeConnector>();

            services.AddBusEndpoint<TreeMiniAppMessageHandler>(typeof(GetTreeForSpaceRequest), typeof(DeleteTreeEntitiesRequest));
            // Владелец объявляет тип; общий Core не перечисляет предметные классы.
        services.AddSingleton<IEntityTypeDescriptor>(new EntityTypeDescriptor<global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.Entities.Folder>(
            global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, "TreeMiniApp", global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Key, false, false, "NewFolder",
            (id, name, text) => new EntityInitialContent(null), allowedParents: [global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id, global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id]));
        services.AddSingleton<IEntityDataStorageConverter, FolderEntityDataStorageConverter>();
            services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, false));
            services.AddMiniAppHost("TreeMiniApp", new ModuleDefinition("TreeMiniApp", "tree", "Дерево", "account_tree",
                ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "tree.main", "LeftTree", OpenOnStartup: true),
            new ModuleDefinition("TreeMiniApp", "Tree.SelectionInfo", "Информация", null, ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "tree.selection", "RightPanel", Order: 20, OpenOnStartup: true));
            services.AddModuleComponent<BusinessEntity.MiniApps.TreeMiniApp.Components.TreeMiniAppComponent>("tree.main");

        services.AddModuleComponent<global::BusinessEntity.MiniApps.TreeMiniApp.Components.SelectionInformation>("tree.selection");

        services.AddMiniAppButtons(
                new ButtonDefinition("TreeMiniApp", "tree.createFolder", "Tree.ContextMenu", "Создать папку", "create_new_folder", 10, RequiredPermission: "AdminItems", EntityTypes: ["Space", "Folder"], Argument: "Folder"),
                new ButtonDefinition("TreeMiniApp", "tree.open", "Tree.ContextMenu", "Открыть", "open_in_new", 0, RequiredPermission: "ReadContent", EntityTypes: ["Document", "RichTextDocument"]),
                new ButtonDefinition("TreeMiniApp", "tree.edit", "Tree.ContextMenu", "Редактировать", "edit", 5, RequiredPermission: "EditContent", EntityTypes: ["Document"]),
                new ButtonDefinition("TreeMiniApp", "tree.rename", "Tree.ContextMenu", "Переименовать", "edit", 30, RequiredPermission: "AdminItems", EntityTypes: ["Folder"]),
                new ButtonDefinition("TreeMiniApp", "tree.delete", "Tree.ContextMenu", "Удалить", "delete", 40, RequiredPermission: "DeleteContent", EntityTypes: ["Folder", "Document", "RichTextDocument"]));
            return services;
        }
    }
}

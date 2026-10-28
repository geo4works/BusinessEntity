using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DocumentsMiniApp.Internal;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Registration;

// Регистрирует предметное правило истории документов; UI подключается своим этапом.
public static class DocumentsMiniAppRegistration
{
    // Владелец типа передаёт движку правило версии через общий контракт.
    public static IServiceCollection AddDocumentsMiniApp(this IServiceCollection services)
    {
        services.AddSingleton(new EntityTypeOwnership(BusinessEntityTypeEnum.Document, "DocumentsMiniApp"));
        services.AddSingleton<IEntityArchivePolicy, DocumentArchivePolicy>();
        services.AddSingleton(new EntityUrlPattern("/document/"));
        services.AddSingleton<IEntityVersionPolicy, DocumentVersionPolicy>();
        services.AddMiniAppHost("DocumentsMiniApp",
            new ModuleDefinition("DocumentsMiniApp", "Documents.Document", "Документ", null, ModulePlacement.Page, ModuleInstancePolicy.PerContext, "documents.page", RequiredPermission: "ReadContent", HasSaveContext: true));

        services.AddModuleComponent<global::BusinessEntity.MiniApps.DocumentsMiniApp.Components.DocumentContent>("documents.page");
        services.AddMiniAppButtons(
            new ButtonDefinition("DocumentsMiniApp", "document.edit", "Document.Toolbar", "Edit", null, 0, RequiredPermission: "EditContent"),
            new ButtonDefinition("DocumentsMiniApp", "document.save", "Document.Toolbar", "Сохранить", null, 1, RequiredPermission: "EditContent"),
            new ButtonDefinition("DocumentsMiniApp", "document.cancel", "Document.Toolbar", "Отмена", null, 2),
            new ButtonDefinition("DocumentsMiniApp", "document.delete", "Document.Toolbar", "Удалить", null, 3, RequiredPermission: "DeleteContent"));

        services.AddMiniAppButtons(new ButtonDefinition("DocumentsMiniApp", "tree.createDocument", "Tree.ContextMenu", "Создать документ", "note_add", 20,
            RequiredPermission: "AdminItems", ExecutionMiniAppId: "TreeMiniApp", EntityTypes: ["Space", "Folder"], Argument: "Document"));
        services.AddMiniAppButtons(new ButtonDefinition("DocumentsMiniApp", "document.print", "Document.Toolbar", "Печать", RequiredPermission: "ReadContent",
            TargetRoute: "/print/{contextId}", CssClass: "btn btn-sm btn-outline-secondary d-inline-flex align-items-center gap-1"));
        return services;
    }
}

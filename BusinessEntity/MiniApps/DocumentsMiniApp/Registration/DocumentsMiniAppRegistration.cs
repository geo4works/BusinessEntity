using BusinessEntity.Core.Types;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.DocumentsMiniApp.Internal.Serialization;
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
        // Владелец объявляет тип; общий Core не перечисляет предметные классы.
        services.AddSingleton<IEntityTypeDescriptor>(new EntityTypeDescriptor<global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document>(
            global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, "DocumentsMiniApp", global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Key, true, true, "NewDocument",
            (id, name, text) => new EntityInitialContent(new global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document { Id = id, Name = name, Tag = global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Key, Text = text ?? string.Concat(Enumerable.Repeat("Новый документ. ", 100)) }), allowedParents: [global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id, global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id], pageRoute: "/document/{id}"));
        services.AddSingleton<IEntityDataStorageConverter, DocumentEntityDataStorageConverter>();
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

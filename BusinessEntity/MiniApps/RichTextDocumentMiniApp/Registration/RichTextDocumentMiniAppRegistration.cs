using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Registration;

// Регистрирует правило полной авторской истории статьи; UI переносится отдельно.
public static class RichTextDocumentMiniAppRegistration
{
    // Передаёт DataProvider объявление истории от владельца статьи.
    public static IServiceCollection AddRichTextDocumentMiniApp(this IServiceCollection services)
    {
        services.AddSingleton(new EntityTypeOwnership(BusinessEntityTypeEnum.RichTextDocument, "RichTextDocumentMiniApp"));
        services.AddSingleton<IEntityArchivePolicy, RichTextDocumentArchivePolicy>();
        services.AddSingleton(new EntityUrlPattern("/rich-document/"));
        services.AddSingleton<IEntityVersionPolicy, RichTextDocumentVersionPolicy>();
        services.AddSingleton(new EntityUrlPattern("/rich-document-files/"));
        services.AddMiniAppHost("RichTextDocumentMiniApp",
            new ModuleDefinition("RichTextDocumentMiniApp", "RichText.Document", "Статья", null, ModulePlacement.Page, ModuleInstancePolicy.PerContext, "richtext.page", RequiredPermission: "ReadContent", HasSaveContext: true),
            new ModuleDefinition("RichTextDocumentMiniApp", "RichText.Print", "Печать", null, ModulePlacement.Page, ModuleInstancePolicy.PerContext, "richtext.print", RequiredPermission: "ReadContent"));

        services.AddModuleComponent<global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Components.RichDocumentContent>("richtext.page");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Components.DocumentPrintContent>("richtext.print");
        services.AddMiniAppButtons(
            new ButtonDefinition("RichTextDocumentMiniApp", "article.edit", "Document.Toolbar", "Edit", null, 0, RequiredPermission: "EditContent"),
            new ButtonDefinition("RichTextDocumentMiniApp", "article.save", "Document.Toolbar", "Save", null, 1, RequiredPermission: "EditContent"),
            new ButtonDefinition("RichTextDocumentMiniApp", "article.read", "Document.Toolbar", "Перейти в режим чтения", null, 2),
            new ButtonDefinition("RichTextDocumentMiniApp", "article.publish", "Document.Toolbar", "Опубликовать", null, 3, RequiredPermission: "PublishContent"),
            new ButtonDefinition("RichTextDocumentMiniApp", "article.delete", "Document.Toolbar", "Удалить", null, 4, RequiredPermission: "DeleteContent"));
        services.AddScoped<BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextUserMessages>();
        services.AddMiniAppButtons(new ButtonDefinition("RichTextDocumentMiniApp", "tree.createArticle", "Tree.ContextMenu", "Создать рич-документ", "note_add", 20,
            RequiredPermission: "AdminItems", ExecutionMiniAppId: "TreeMiniApp", EntityTypes: ["Space", "Folder"], Argument: "RichTextDocument"));
        services.AddMiniAppButtons(new ButtonDefinition("RichTextDocumentMiniApp", "article.print", "Document.Toolbar", "Печать", RequiredPermission: "ReadContent",
            TargetRoute: "/print/{contextId}", CssClass: "btn btn-sm btn-outline-secondary d-inline-flex align-items-center gap-1"));
        services.AddMiniAppButtons(new ButtonDefinition("RichTextDocumentMiniApp", "article.import", "Document.Toolbar", "Импорт", RequiredPermission: "EditContent", Argument: ".txt,.md,.markdown,.html,.htm"));
        return services;
    }
}

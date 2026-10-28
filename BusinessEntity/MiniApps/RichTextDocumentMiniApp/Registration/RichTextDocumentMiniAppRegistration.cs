using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextPaste;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.Core.Types;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.Serialization;
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
        // Владелец объявляет тип; общий Core не перечисляет предметные классы.
        services.AddSingleton<IEntityTypeDescriptor>(new EntityTypeDescriptor<global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities.RichTextDocument>(
            global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id, "RichTextDocumentMiniApp", global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Key, true, true, "NewRichTextDocument",
            (id, name, text) =>
            {
                var chunk = new BusinessEntity.Core.Chunks.BusinessEntityDataChunk { BusinessEntityId = id };
                return new EntityInitialContent(new global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities.RichTextDocument { Id = id, Name = name, Tag = global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Key }, [chunk], [chunk.Id]);
            }, allowedParents: [global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id, global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id], pageRoute: "/rich-document/{id}"));
        services.AddSingleton<IEntityDataStorageConverter, RichTextDocumentEntityDataStorageConverter>();
        services.AddScoped<RichTextDocumentReader>();
        services.AddScoped<RichTextOutlineService>();
        services.AddScoped<RichTextSearchService>();
        services.AddScoped<RichTextEditService>();
        services.AddScoped<RichTextWorkingSetFactory>();
        services.AddScoped<RichTextFileService>();
            services.AddScoped<BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextDocumentSettingsService>();
            services.AddScoped<HtmlToRichTextBlocksConverter>();
            services.AddScoped<IRichDocFormatConverter, PlainTextRichTextImportConverter>();
            services.AddScoped<IRichDocFormatConverter, MarkdownRichTextImportConverter>();
            services.AddScoped<IRichDocFormatConverter, HtmlRichTextImportConverter>();
            services.AddScoped<IRichDocFormatConverterFactory, RichDocFormatConverterFactory>();
            services.AddScoped<BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextDocumentImportService>();
            services.AddScoped<IRichTextClipboardPasteDetector, RichTextClipboardPasteDetector>();
            services.AddScoped<RichTextClipboardImportHelper>();
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

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
        services.AddMiniAppHost("RichTextDocumentMiniApp");
            return services;
    }
}

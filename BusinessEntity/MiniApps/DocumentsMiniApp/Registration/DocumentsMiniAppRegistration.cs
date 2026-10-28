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
        return services;
    }
}

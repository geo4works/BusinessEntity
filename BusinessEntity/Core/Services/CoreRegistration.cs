using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Types;

namespace BusinessEntity.Core.Services;

// Подключает общий движок; предметные типы и их правила объявляют владельцы.
public static class CoreRegistration
{
    // Общий helper, фабрика и реестр представлены в одном экземпляре соответствующего DI lifetime.
    public static IServiceCollection AddBusinessEntityCore(this IServiceCollection services)
    {
        services.AddSingleton<EntityTypeRegistry>(); services.AddSingleton<EntitySaveBatchFactory>();
        services.AddScoped<IBusinessEntityFactory, BusinessEntityFactory>(); services.AddScoped<BusinessEntityHelper>();
        services.AddScoped<IPossibleEntityRelationTypesProvider, PossibleEntityRelationTypesProvider>(); return services;
    }
}

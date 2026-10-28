using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Registration;

// Объявляет владельца пространства; пользовательские экраны переносятся на своём этапе.
public static class SpacesMiniAppRegistration
{
    // Регистрирует логическую ответственность типа в общем движке.
    public static IServiceCollection AddSpacesMiniApp(this IServiceCollection services)
    {
        services.AddSingleton(new EntityTypeOwnership(BusinessEntityTypeEnum.Space, "SpacesMiniApp"));
        services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(BusinessEntityTypeEnum.Space, false));
        services.AddMiniAppHost("SpacesMiniApp");
            return services;
    }
}

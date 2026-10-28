using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.Core.Types;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.SpacesMiniApp.Internal.Serialization;
using BusinessEntity.Infrastructure.Commands;
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
        // Владелец объявляет тип; общий Core не перечисляет предметные классы.
        services.AddScoped<BusinessEntity.MiniApps.SpacesMiniApp.Internal.SpacesMessageHandler>();
        services.AddScoped<BusinessEntity.MiniApps.SpacesMiniApp.Contracts.ISpacesConnector, BusinessEntity.MiniApps.SpacesMiniApp.Connectors.SpacesConnector>();
        services.AddBusEndpoint<BusinessEntity.MiniApps.SpacesMiniApp.Internal.SpacesMessageHandler>(typeof(SpacesGetSpaceByIdRequest), typeof(SpacesGetSpacesRequest), typeof(SpacesGetGenericSpacePropertiesRequest));
        services.AddSingleton<IEntityTypeDescriptor>(new EntityTypeDescriptor<global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities.Space>(
            global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id, "SpacesMiniApp", global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Key, false, false, "NewSpace",
            (id, name, text) => new EntityInitialContent(null)));
        services.AddSingleton<IEntityDataStorageConverter, SpaceEntityDataStorageConverter>();
            services.AddScoped<BusinessEntity.MiniApps.SpacesMiniApp.Internal.SpaceHelper>();
        services.AddScoped<BusinessEntity.MiniApps.SpacesMiniApp.Internal.SpacePropertiesWorkingSetFactory>();
            services.AddScoped<BusinessEntity.MiniApps.SpacesMiniApp.Contracts.IUserContextService, BusinessEntity.MiniApps.SpacesMiniApp.Internal.UserContextService>();
        services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id, false));
        services.AddMiniAppHost("SpacesMiniApp",
            new ModuleDefinition("SpacesMiniApp", "Spaces.Home", "Главная", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "spaces.home"),
            new ModuleDefinition("SpacesMiniApp", "Spaces.Selection", "Выбор пространства", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "spaces.selection"),
            new ModuleDefinition("SpacesMiniApp", "Spaces.Administration", "Пространства", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "spaces.administration", RequiredPermission: "AdminSpace", HasSaveContext: true),
            new ModuleDefinition("SpacesMiniApp", "Spaces.Header", "Пространства", null, ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "spaces.header", "User.HeaderSpaces", Order: 0, OpenOnStartup: true));

        services.AddModuleComponent<global::BusinessEntity.MiniApps.SpacesMiniApp.Components.SpacesHome>("spaces.home");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.SpacesMiniApp.Components.SpacesSelection>("spaces.selection");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.SpacesMiniApp.Components.SpacesAdministration>("spaces.administration");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.SpacesMiniApp.Components.SpacesHeaderMenu>("spaces.header");
        services.AddMiniAppButtons(
            new ButtonDefinition("SpacesMiniApp", "nav.home", "Nav.Main", "Home", "oi oi-home", 0, TargetRoute: "/"),
            new ButtonDefinition("SpacesMiniApp", "admin.spaces", "Administration.Navigation", "Пространства", null, 20, RequiredPermission: "AdminSpace", TargetRoute: "/administration/spaces"));

        return services;
    }
}

using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.NavigationMiniApp.Components;

namespace BusinessEntity.MiniApps.NavigationMiniApp.Registration;

// Регистрирует общий приёмник навигационных деклараций.
public static class NavigationMiniAppRegistration
{
    // Навигация не знает перечня страниц владельцев.
    public static IServiceCollection AddNavigationMiniApp(this IServiceCollection services)
    {
        services.AddMiniAppHost("NavigationMiniApp", new ModuleDefinition("NavigationMiniApp", "Navigation.Main", "Навигация", null,
            ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "navigation.main", "LeftNav", OpenOnStartup: true));
        services.AddModuleComponent<NavigationView>("navigation.main"); return services;
    }
}

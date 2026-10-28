using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.MiniApps.AdministrationMiniApp.Components;

namespace BusinessEntity.MiniApps.AdministrationMiniApp.Registration;

// Административный вход владеет только навигацией по разделам, а не их данными.
public static class AdministrationMiniAppRegistration
{
    // Серверный guard допускает GlobalAdmin или применимый AdminSpace.
    public static IServiceCollection AddAdministrationMiniApp(this IServiceCollection services)
    {
        services.AddMiniAppHost("AdministrationMiniApp", new ModuleDefinition("AdministrationMiniApp", "Administration.Page", "Администрирование", null,
            ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "administration.navigation", RequiredPermission: "Administration"));
        services.AddModuleComponent<AdministrationNavigation>("administration.navigation");
        services.AddMiniAppButtons(new ButtonDefinition("AdministrationMiniApp", "nav.administration", "Nav.Main", "Администрирование", "oi oi-wrench", 60, RequiredPermission: "Administration", TargetRoute: "/administration"));
        return services;
    }
}

using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.RightPanelMiniApp.Components;
namespace BusinessEntity.MiniApps.RightPanelMiniApp.Registration;
// Панель владеет host-ом, прикладное содержимое объявляют отдельные владельцы.
public static class RightPanelMiniAppRegistration
{
    // Shell получает один модуль панели на окно, без знания её вкладок.
    public static IServiceCollection AddRightPanelMiniApp(this IServiceCollection services)
    {
        services.AddMiniAppHost("RightPanelMiniApp", new ModuleDefinition("RightPanelMiniApp", "Panel", "Правая панель", null, ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "right.panel", "RightPanel", OpenOnStartup: true, RequiredPermission: "Authenticated"));
        services.AddModuleComponent<RightPanel>("right.panel");
        return services;
    }
}

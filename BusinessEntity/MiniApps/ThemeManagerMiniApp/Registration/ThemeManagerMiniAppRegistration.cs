using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Settings;
using BusinessEntity.MiniApps.ThemeManagerMiniApp.Internal;
using BusinessEntity.MiniApps.ThemeManagerMiniApp.Components;
namespace BusinessEntity.MiniApps.ThemeManagerMiniApp.Registration;
// Владелец оформления объявляет компоненты и контракты настроек без зависимости от Geo4.
public static class ThemeManagerMiniAppRegistration
{
    // Состояние темы и layout scoped, декларации общие для приложения.
    public static IServiceCollection AddThemeManagerMiniApp(this IServiceCollection s)
    {
        s.AddScoped<ThemeSession>();
        s.AddMiniAppHost("ThemeManagerMiniApp", new ModuleDefinition("ThemeManagerMiniApp", "Bootstrap", "", null, ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "theme.bootstrap", "TopBar", Order: -90, OpenOnStartup: true, RequiredPermission: "Authenticated"), new ModuleDefinition("ThemeManagerMiniApp", "Themes", "Темы оформления", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "theme.editor", RequiredPermission: "Administration"), new ModuleDefinition("ThemeManagerMiniApp", "Layouts", "Компоновка", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "theme.layouts", RequiredPermission: "Administration"));
        s.AddModuleComponent<ThemeBootstrap>("theme.bootstrap");
        s.AddModuleComponent<ThemeEditor>("theme.editor");
        s.AddModuleComponent<LayoutSettings>("theme.layouts");
        s.AddSingleton(new SettingsPageDefinition("/administration/appearance/themes", "Темы оформления", "ThemeManagerMiniApp:Themes", 100, "Administration"));
        s.AddSingleton(new SettingsPageDefinition("/administration/appearance/layouts", "Компоновка", "ThemeManagerMiniApp:Layouts", 110, "Administration"));
        return s;
    }
}

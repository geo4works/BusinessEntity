using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.MiniApps.DiagnosticsMiniApp.Components;

namespace BusinessEntity.MiniApps.DiagnosticsMiniApp.Registration;

// Диагностика имеет собственные страницы с проверкой глобального административного права.
public static class DiagnosticsMiniAppRegistration
{
    // Объявляет страницы и навигацию без выполнения диагностических действий в host-service.
    public static IServiceCollection AddDiagnosticsMiniApp(this IServiceCollection services)
    {
        services.AddMiniAppHost("DiagnosticsMiniApp",
            new("DiagnosticsMiniApp", "Diagnostics.Page", "Диагностика", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "diagnostics.page", RequiredPermission: "GlobalAdmin"),
            new("DiagnosticsMiniApp", "Diagnostics.Logging", "Логгинг", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "diagnostics.logging", RequiredPermission: "GlobalAdmin"));
        services.AddModuleComponent<DiagnosticsContent>("diagnostics.page"); services.AddModuleComponent<LoggingContent>("diagnostics.logging");
        services.AddMiniAppButtons(
            new("DiagnosticsMiniApp", "nav.logging", "Nav.Main", "Логгинг", "oi oi-pencil", 30, RequiredPermission: "GlobalAdmin", TargetRoute: "/logging"),
            new("DiagnosticsMiniApp", "nav.diagnostics", "Nav.Main", "Диагностика", "oi oi-list", 40, RequiredPermission: "GlobalAdmin", TargetRoute: "/diagnostics"));
        return services;
    }
}

using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;

namespace BusinessEntity.MiniApps.SysParametersMiniApp.Registration;

// Объявляет владельца системных параметров без знания этого домена в Core.
public static class SysParametersMiniAppRegistration
{
    // Регистрирует тип настроек у его логического владельца.
    public static IServiceCollection AddSysParametersMiniApp(this IServiceCollection services)
    {
        services.AddSingleton(new EntityTypeOwnership(BusinessEntityTypeEnum.SysParametersTp, "SysParametersMiniApp"));
        services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(BusinessEntityTypeEnum.SysParametersTp, true));
        services.AddMiniAppHost("SysParametersMiniApp", new ModuleDefinition("SysParametersMiniApp", "defaults", "Параметры системы", null,
                ModulePlacement.Headless, ModuleInstancePolicy.AppSingleton, Order: 30, OpenOnStartup: true),
            new ModuleDefinition("SysParametersMiniApp", "SysParameters.Administration", "Параметры системы", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "sys.administration", RequiredPermission: "GlobalAdmin"),
            new ModuleDefinition("SysParametersMiniApp", "SysParameters.Header", "Компания", null, ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "sys.company", "TopBar", Order: 0, OpenOnStartup: true));
            services.AddModuleRuntime<BusinessEntity.MiniApps.SysParametersMiniApp.Internal.SysParametersStartupRuntime>("SysParametersMiniApp:defaults");

        services.AddModuleComponent<global::BusinessEntity.MiniApps.SysParametersMiniApp.Components.SysParametersAdministration>("sys.administration");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.SysParametersMiniApp.Components.CompanyHeader>("sys.company");
        services.AddMiniAppButtons(
            new ButtonDefinition("SysParametersMiniApp", "admin.sys", "Administration.Navigation", "Параметры системы", null, 10, RequiredPermission: "GlobalAdmin", TargetRoute: "/administration/system"));

        return services;
    }
}

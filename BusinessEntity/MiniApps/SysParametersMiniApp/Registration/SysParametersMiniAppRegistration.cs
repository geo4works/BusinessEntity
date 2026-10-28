using BusinessEntity.Core.Types;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.SysParametersMiniApp.Internal.Serialization;
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
        // Владелец объявляет тип; общий Core не перечисляет предметные классы.
        services.AddSingleton<IEntityTypeDescriptor>(new EntityTypeDescriptor<global::BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities.SysParameters>(
            global::BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.SysParametersTpType.Id, "SysParametersMiniApp", global::BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.SysParametersTpType.Key, false, true, "SysParameters",
            (id, name, text) => new EntityInitialContent(new global::BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities.SysParameters { Id = id, Name = name, Tag = global::BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.SysParametersTpType.Key }), singleton: true));
        services.AddSingleton<IEntityDataStorageConverter, SysParametersEntityDataStorageConverter>();
        services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(global::BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.SysParametersTpType.Id, true));
        services.AddMiniAppHost("SysParametersMiniApp", new ModuleDefinition("SysParametersMiniApp", "defaults", "Параметры системы", null,
                ModulePlacement.Headless, ModuleInstancePolicy.AppSingleton, Order: 30, OpenOnStartup: true),
            new ModuleDefinition("SysParametersMiniApp", "SysParameters.Administration", "Параметры системы", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "sys.administration", RequiredPermission: "GlobalAdmin", HasSaveContext: true),
            new ModuleDefinition("SysParametersMiniApp", "SysParameters.Header", "Компания", null, ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "sys.company", "TopBar", Order: 0, OpenOnStartup: true));
            services.AddModuleRuntime<BusinessEntity.MiniApps.SysParametersMiniApp.Internal.SysParametersStartupRuntime>("SysParametersMiniApp:defaults");

        services.AddModuleComponent<global::BusinessEntity.MiniApps.SysParametersMiniApp.Components.SysParametersAdministration>("sys.administration");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.SysParametersMiniApp.Components.CompanyHeader>("sys.company");
        services.AddMiniAppButtons(
            new ButtonDefinition("SysParametersMiniApp", "admin.sys", "Administration.Navigation", "Параметры системы", null, 10, RequiredPermission: "GlobalAdmin", TargetRoute: "/administration/system"));

        return services;
    }
}

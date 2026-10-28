using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.SampleDataMiniApp.Contracts;
using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.SampleDataMiniApp.Internal;

namespace BusinessEntity.MiniApps.SampleDataMiniApp.Registration
{
    // Регистрирует все публичные и внутренние компоненты mini-app тестовой заливки в DI.
    public static class SampleDataMiniAppRegistration
    {
        // Подключает mini-app тестовой заливки и все его внутренние зависимости.
        public static IServiceCollection AddSampleDataMiniApp(this IServiceCollection services)
        {
            services.AddScoped<BusinessEntity.MiniApps.SampleDataMiniApp.Internal.SampleDataMessageHandler>();
        services.AddScoped<BusinessEntity.MiniApps.SampleDataMiniApp.Contracts.ISampleDataConnector, BusinessEntity.MiniApps.SampleDataMiniApp.Connectors.SampleDataConnector>();
        services.AddBusEndpoint<BusinessEntity.MiniApps.SampleDataMiniApp.Internal.SampleDataMessageHandler>(typeof(SampleDataForceReseedRequest));
        services.AddScoped<ISampleDataService, SampleDataService>();
            services.AddScoped<IDataFillLineProvider, DataFillLineProvider>();

            services.AddMiniAppHost("SampleDataMiniApp", new ModuleDefinition("SampleDataMiniApp", "seed", "Начальные данные", null,
                ModulePlacement.Headless, ModuleInstancePolicy.AppSingleton, Order: 40, OpenOnStartup: true),
            new ModuleDefinition("SampleDataMiniApp", "SampleData.Administration", "Общее", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "sample.administration", RequiredPermission: "GlobalAdmin"));
            services.AddModuleRuntime<SampleDataStartupRuntime>("SampleDataMiniApp:seed");

        services.AddModuleComponent<global::BusinessEntity.MiniApps.SampleDataMiniApp.Components.GeneralAdministration>("sample.administration");
        services.AddMiniAppButtons(
            new ButtonDefinition("SampleDataMiniApp", "admin.general", "Administration.Navigation", "Общее", null, 0, RequiredPermission: "GlobalAdmin", TargetRoute: "/administration/general"));

        return services;
        }
    }
}

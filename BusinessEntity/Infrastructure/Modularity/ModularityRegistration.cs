using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Saving;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Modularity;

// Композиция общей модульности без списка конкретных владельцев или компонентов.
public static class ModularityRegistration
{
    // Устанавливает каталог приложения и менеджер отдельного circuit.
    public static IServiceCollection AddModularity(this IServiceCollection services)
    {
        services.AddSingleton<ButtonCatalog>(); services.AddScoped<ModuleCommandRegistry>();
        services.AddScoped<ModuleCommandEndpoint>(); services.AddBusEndpoint<ModuleCommandEndpoint>(typeof(ButtonCommandInvoked));
        services.AddSingleton<ApplicationStartupState>(); services.AddSingleton<ModuleCatalog>();
        services.AddHostedService(provider => provider.GetRequiredService<ModuleCatalog>());
        services.AddSingleton<IModuleUiFactory, ModuleUiFactory>();
        services.TryAddScoped<ISaveBatchTransport, SaveBatchTransport>();
        services.AddScoped<WindowModularityManager>();
        return services;
    }
    // Регистрирует один декларативный singleton на логический миниапп.
    public static IServiceCollection AddMiniAppHost(this IServiceCollection services, string miniAppId, params ModuleDefinition[] definitions)
    { services.AddSingleton(provider => new MiniAppHostService(provider.GetRequiredService<IMessageBus>(), miniAppId, definitions, provider.GetServices<ButtonRegistration>().Where(x => x.Definition.MiniAppId == miniAppId).Select(x => x.Definition))); return services; }
    // Декларации команд не несут делегатов; runtime binding подключает сам владелец.
    public static IServiceCollection AddMiniAppButtons(this IServiceCollection services, params ButtonDefinition[] definitions)
    { foreach (var definition in definitions) services.AddSingleton(new ButtonRegistration(definition)); return services; }
    // Только Registration владельца знает свой компонент.
    public static IServiceCollection AddModuleComponent<T>(this IServiceCollection services, string key) where T : IComponent
    { services.AddSingleton(new ModuleComponentRegistration(key, typeof(T))); return services; }
    // Локально связывает headless декларацию с runtime своего владельца.
    public static IServiceCollection AddModuleRuntime<T>(this IServiceCollection services, string definitionKey) where T : class, IModuleRuntime
    { services.AddScoped<T>(); services.AddSingleton(new ModuleRuntimeRegistration(definitionKey, provider => provider.GetRequiredService<T>())); return services; }
}

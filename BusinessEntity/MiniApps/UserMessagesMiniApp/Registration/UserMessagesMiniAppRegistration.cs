using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Internal;

namespace BusinessEntity.MiniApps.UserMessagesMiniApp.Registration;

// Регистрирует mini-app пользовательских сообщений в DI.
public static class UserMessagesMiniAppRegistration
{
    // Подключает state, bus handler и фасад mini-app пользовательских сообщений.
    public static IServiceCollection AddUserMessagesMiniApp(this IServiceCollection services)
    {
        services.AddScoped<UserMessagesMiniAppState>();
        services.AddScoped<UserMessagesMiniAppMessageHandler>();
        services.AddScoped<IUserMessagesMiniApp, Facade.UserMessagesMiniApp>();

        services.AddSingleton<IUserMessageFlashStore, UserMessageFlashStore>();
        services.AddMiniAppHost("UserMessagesMiniApp",
            new ModuleDefinition("UserMessagesMiniApp", "UserMessages.Panel", "Сообщения", null, ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "messages.panel", "RightPanel", Order: 30, OpenOnStartup: true, RequiredPermission: "Authenticated"));

        services.AddModuleComponent<global::BusinessEntity.MiniApps.UserMessagesMiniApp.Components.UserMessagesPanel>("messages.panel");

        return services;
    }
}

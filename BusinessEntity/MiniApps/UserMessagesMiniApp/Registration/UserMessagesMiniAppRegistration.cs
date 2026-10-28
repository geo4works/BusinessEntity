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
        services.AddMiniAppHost("UserMessagesMiniApp");
            return services;
    }
}

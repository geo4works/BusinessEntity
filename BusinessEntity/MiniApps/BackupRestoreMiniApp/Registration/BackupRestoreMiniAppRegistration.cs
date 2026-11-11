using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.BackupRestoreMiniApp.Internal;

namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Registration;

// Регистрирует владельца фонового backup; предметные файлы переносятся в этапе 6.
public static class BackupRestoreMiniAppRegistration
{
    // Каталог владеет временем жизни задачи, host-service объявляет только возможность.
    public static IServiceCollection AddBackupRestoreMiniApp(this IServiceCollection services)
    {
        services.AddScoped<BusinessEntity.MiniApps.BackupRestoreMiniApp.Internal.BackupRestoreMessageHandler>();
        services.AddScoped<BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts.IBackupRestoreConnector, BusinessEntity.MiniApps.BackupRestoreMiniApp.Connectors.BackupRestoreConnector>();
        services.AddBusEndpoint<BusinessEntity.MiniApps.BackupRestoreMiniApp.Internal.BackupRestoreMessageHandler>(typeof(BackupRestoreRunManualBackupRequest), typeof(BackupRestoreRunBackupCycleRequest), typeof(BackupRestoreClearSpaceBackupRequest), typeof(BackupRestoreGetDisplaySpaceBackupFolderRequest), typeof(BackupRestoreRestoreSpaceRequest));
        services.AddSingleton<SpaceBackupService>(); services.AddScoped<SpaceRestoreService>(); services.AddSingleton<SpaceArchivePaths>();
        services.AddMiniAppHost("BackupRestoreMiniApp", new ModuleDefinition("BackupRestoreMiniApp", "scheduler", "Резервное копирование", null,
            ModulePlacement.Headless, ModuleInstancePolicy.AppSingleton, Order: 60, OpenOnStartup: true));
        services.AddSingleton(new ModuleRuntimeRegistration("BackupRestoreMiniApp:scheduler", provider =>
            new HostedModuleRuntime(provider.GetRequiredService<SpaceBackupService>())));
        return services;
    }
}

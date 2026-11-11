using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;
using ReactiveUI;
namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Internal;

// Делегирует запрос своему владельцу в server scope.
internal sealed class BackupRestoreMessageHandler : ScopedBusEndpoint
{
    private readonly SpaceBackupService _backup;
    private readonly SpaceRestoreService _restore;
    // Подключает собственные сервисы и адрес обработчика.
    public BackupRestoreMessageHandler(SpaceBackupService _backup, SpaceRestoreService _restore, IMessageBus bus, BusRequestScope scope, ILogger<BackupRestoreMessageHandler> logger) : base(bus, scope, logger)
    { this._backup = _backup; this._restore = _restore; }
    // Подписки освобождаются вместе с окном.
    protected override void SubscribeRequests()
    {
        Subscribe<BackupRestoreRunManualBackupRequest, BackupRestoreRunManualBackupResponse>(async request => new BackupRestoreRunManualBackupResponse(request.RequestId, await _backup.RunManualBackupAsync(request.SpaceId)), static (id, error) => new BackupRestoreRunManualBackupResponse(id, default!, error));
        Subscribe<BackupRestoreRunBackupCycleRequest, BackupRestoreRunBackupCycleResponse>(async request => new BackupRestoreRunBackupCycleResponse(request.RequestId, await _backup.RunBackupCycleAsync()), static (id, error) => new BackupRestoreRunBackupCycleResponse(id, default!, error));
        Subscribe<BackupRestoreClearSpaceBackupRequest, BackupRestoreClearSpaceBackupResponse>(async request => new BackupRestoreClearSpaceBackupResponse(request.RequestId, await _backup.ClearSpaceBackupAsync(request.SpaceId, request.Settings)), static (id, error) => new BackupRestoreClearSpaceBackupResponse(id, default!, error));
        Subscribe<BackupRestoreGetDisplaySpaceBackupFolderRequest, BackupRestoreGetDisplaySpaceBackupFolderResponse>(async request => new BackupRestoreGetDisplaySpaceBackupFolderResponse(request.RequestId, await Task.FromResult(_backup.GetDisplaySpaceBackupFolder(request.SpaceId, request.Settings))), static (id, error) => new BackupRestoreGetDisplaySpaceBackupFolderResponse(id, default!, error));
        Subscribe<BackupRestoreRestoreSpaceRequest, BackupRestoreRestoreSpaceResponse>(async request => new BackupRestoreRestoreSpaceResponse(request.RequestId, await _restore.RestoreSpaceAsync(request.Request)), static (id, error) => new BackupRestoreRestoreSpaceResponse(id, default!, error));
    }
}

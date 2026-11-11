using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;
namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Connectors;

// Только адресный транспорт, без предметной логики.
public sealed class BackupRestoreConnector(BusRequestClient client) : IBackupRestoreConnector
{
    // Отправляет RunManualBackupAsync по адресу текущего scope.
    public async Task<SpaceBackupRunResult> RunManualBackupAsync(Guid spaceId, CancellationToken ct = default)
    {
        var response = await client.SendAsync<BackupRestoreRunManualBackupRequest, BackupRestoreRunManualBackupResponse>(new BackupRestoreRunManualBackupRequest(Guid.NewGuid() , spaceId), ct);
        return response.Value;
    }
    // Отправляет RunBackupCycleAsync по адресу текущего scope.
    public async Task<IReadOnlyList<SpaceBackupRunResult>> RunBackupCycleAsync(CancellationToken ct = default)
    {
        var response = await client.SendAsync<BackupRestoreRunBackupCycleRequest, BackupRestoreRunBackupCycleResponse>(new BackupRestoreRunBackupCycleRequest(Guid.NewGuid()), ct);
        return response.Value;
    }
    // Отправляет ClearSpaceBackupAsync по адресу текущего scope.
    public async Task<SpaceBackupClearResult> ClearSpaceBackupAsync(Guid spaceId, GenericSpaceProperties settings, CancellationToken ct = default)
    {
        var response = await client.SendAsync<BackupRestoreClearSpaceBackupRequest, BackupRestoreClearSpaceBackupResponse>(new BackupRestoreClearSpaceBackupRequest(Guid.NewGuid() , spaceId, settings), ct);
        return response.Value;
    }
    // Отправляет GetDisplaySpaceBackupFolderAsync по адресу текущего scope.
    public async Task<string> GetDisplaySpaceBackupFolderAsync(Guid spaceId, GenericSpaceProperties settings, CancellationToken ct = default)
    {
        var response = await client.SendAsync<BackupRestoreGetDisplaySpaceBackupFolderRequest, BackupRestoreGetDisplaySpaceBackupFolderResponse>(new BackupRestoreGetDisplaySpaceBackupFolderRequest(Guid.NewGuid() , spaceId, settings), ct);
        return response.Value;
    }
    // Отправляет RestoreSpaceAsync по адресу текущего scope.
    public async Task<SpaceRestoreResult> RestoreSpaceAsync(SpaceRestoreRequest request, CancellationToken ct = default)
    {
        var response = await client.SendAsync<BackupRestoreRestoreSpaceRequest, BackupRestoreRestoreSpaceResponse>(new BackupRestoreRestoreSpaceRequest(Guid.NewGuid() , request), ct);
        return response.Value;
    }
}

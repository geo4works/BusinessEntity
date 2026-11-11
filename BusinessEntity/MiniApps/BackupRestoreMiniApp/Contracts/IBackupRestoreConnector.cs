using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;

// Публичные запросы к владельцу через единственную шину.
public interface IBackupRestoreConnector
{
    // Выполняет RunManualBackupAsync у владельца.
    Task<SpaceBackupRunResult> RunManualBackupAsync(Guid spaceId, CancellationToken ct = default);
    // Выполняет RunBackupCycleAsync у владельца.
    Task<IReadOnlyList<SpaceBackupRunResult>> RunBackupCycleAsync(CancellationToken ct = default);
    // Выполняет ClearSpaceBackupAsync у владельца.
    Task<SpaceBackupClearResult> ClearSpaceBackupAsync(Guid spaceId, GenericSpaceProperties settings, CancellationToken ct = default);
    // Выполняет GetDisplaySpaceBackupFolderAsync у владельца.
    Task<string> GetDisplaySpaceBackupFolderAsync(Guid spaceId, GenericSpaceProperties settings, CancellationToken ct = default);
    // Выполняет RestoreSpaceAsync у владельца.
    Task<SpaceRestoreResult> RestoreSpaceAsync(SpaceRestoreRequest request, CancellationToken ct = default);
}

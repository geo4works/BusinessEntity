namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;

// Значения результата backup/restore без ссылок на сервис владельца.
public sealed class SpaceRestoreRequest
    {
        public string BackupRootPath { get; init; } = string.Empty;
        public string? TargetSpaceName { get; init; }
        public bool DisableBackupForRestoredSpace { get; init; } = true;
    }

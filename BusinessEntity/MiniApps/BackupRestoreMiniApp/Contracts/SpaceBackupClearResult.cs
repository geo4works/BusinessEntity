namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;

// Значения результата backup/restore без ссылок на сервис владельца.
public sealed class SpaceBackupClearResult
    {
        public Guid SpaceId { get; init; }
        public string BackupFolder { get; init; } = string.Empty;
        public string DisplayBackupFolder { get; init; } = string.Empty;
        public bool Deleted { get; init; }
    }

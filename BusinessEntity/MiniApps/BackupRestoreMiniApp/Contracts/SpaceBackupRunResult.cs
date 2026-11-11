namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;

// Значения результата backup/restore без ссылок на сервис владельца.
public sealed class SpaceBackupRunResult
    {
        public Guid SpaceId { get; init; }
        public string SpaceName { get; init; } = string.Empty;
        public int ChangedEntityCount { get; init; }
        public bool RelationsChanged { get; init; }
        public bool ManifestUpdated { get; init; }
        public DateTime FinishedUtc { get; init; }
        public TimeSpan NextInterval { get; init; }
    }

namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;

// Значения результата backup/restore без ссылок на сервис владельца.
public sealed class SpaceRestoreResult
    {
        public Guid RestoreSessionId { get; init; }
        public string SourceBackupRoot { get; init; } = string.Empty;
        public Guid SourceSpaceId { get; init; }
        public Guid TargetSpaceId { get; init; }
        public string TargetSpaceName { get; init; } = string.Empty;
        public int RestoredEntityCount { get; init; }
        public int RestoredRelationCount { get; init; }
        public string RestoreReportPath { get; init; } = string.Empty;
        public IReadOnlyList<string> Warnings { get; init; } = [];
    }

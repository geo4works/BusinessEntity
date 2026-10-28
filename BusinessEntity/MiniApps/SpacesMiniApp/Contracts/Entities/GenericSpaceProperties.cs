namespace BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities
{
    // Общие настройки пространства, хранящиеся в BusinessEntityProperties.
    public sealed class GenericSpaceProperties
    {
        public int SchemaVersion { get; set; } = 1;

        public string Kind { get; set; } = nameof(GenericSpaceProperties);

        public bool DoBackup { get; set; } = true;

        public string BackupFolder { get; set; } = string.Empty;

        public int BackupIntervalMinutes { get; set; } = 5;
    }
}

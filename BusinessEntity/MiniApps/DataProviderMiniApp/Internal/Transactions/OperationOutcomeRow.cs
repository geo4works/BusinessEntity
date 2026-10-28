namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Исход принятой операции пишется в одной SQL-транзакции с бизнес-изменениями.
internal sealed class OperationOutcomeRow
{
    public Guid OperationId { get; set; }
    public string PackageHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public Guid? AuthorUserId { get; set; }
    public DateTime CommittedAt { get; set; }
}
// Конкретная строка чанка и её положение в полном снимке статьи.
internal sealed class ChunkReferenceRow
{
    public Guid BusinessEntityId { get; set; }
    public int Version { get; set; }
    public int Position { get; set; }
    public Guid ChunkId { get; set; }
    public int ChunkRowVersion { get; set; }
}
// Метаданные неизменяемого файла; ссылка становится видима вместе с SQL-коммитом.
internal sealed class EntityFileRow
{
    public Guid EntityId { get; set; }
    public string FileId { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long Length { get; set; }
}
// Единственная поддерживаемая версия схемы, без миграций и догоняющих ALTER.
internal sealed class StorageSchemaRow
{
    public int Id { get; set; }
    public int Version { get; set; }
}
// Зависимость сохраняемой версии от файла защищает и исторические вложения, и встроенное видео.
internal sealed class VersionFileReferenceRow
{
    public Guid SourceEntityId { get; set; }
    public int Version { get; set; }
    public Guid FileEntityId { get; set; }
    public string FileId { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
}

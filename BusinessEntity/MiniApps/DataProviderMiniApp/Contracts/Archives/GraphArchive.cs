using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;

// Полный переносимый снимок подграфа: история, точные составы чанков, свойства, activity и файловый manifest.
public sealed record GraphArchive(int FormatVersion, Guid RootEntityId,
    IReadOnlyList<ArchivedEntity> Entities, IReadOnlyList<BusinessEntityRelation> Relations);
// Экспортируется только текущий формат; старые архивы не интерпретируются.
public static class GraphArchiveFormat
{
    public const int CurrentVersion = 2;
}
// Полный набор принадлежащих сущности строк, без EF-моделей и репозиторных callback.
public sealed record ArchivedEntity(BusinessEntity.Core.Classes.BusinessEntity Card,
    IReadOnlyList<ArchivedPayload> Payloads, IReadOnlyList<ArchivedChunk> Chunks,
    IReadOnlyList<ArchivedChunkReference> ChunkReferences, IReadOnlyList<StoredEntityProperty> Properties,
    IReadOnlyList<StoredEntityComment> Comments, IReadOnlyList<ArchivedFile> Files);
// Неизменяемое содержимое конкретной версии с оригинальными автором и временем.
public sealed record ArchivedPayload(int Version, string Name, string EnvelopeJson, string ContentHash,
    string Description, Guid? SavedByUserId, DateTime CreatedDate, DateTime LastModifiedDate);
// Полная техническая строка чанка; несколько строк одного ChunkId различаются RowVersion.
public sealed record ArchivedChunk(Guid Id, int RowVersion, long SortOrder, string EnvelopeJson,
    string Checksum, DateTime CreatedDate, DateTime LastModifiedDate);
// Порядок и точная строка каждого элемента конкретного снимка.
public sealed record ArchivedChunkReference(int Version, int Position, Guid ChunkId, int ChunkRowVersion);
// Manifest файла отделён от temporary source и не содержит путь машины, создавшей архив.
public sealed record ArchivedFile(string FileId, string Variant, string FileName, string ContentType,
    string ContentHash, long Length, string ArchiveEntry);
// Политика обхода задаётся владельцем backup, а не знанием доменных типов внутри storage.
public sealed record SubgraphExportScope(Guid RootEntityId, IReadOnlyList<string> FollowOutgoingRelations,
    IReadOnlyList<BusinessEntityTypeEnum> ExcludedDescendantTypes);
// Временная копия принадлежит DataProvider до явного освобождения lease или сборки после рестарта.
public sealed record ExportFileSource(string ArchiveEntry, string TemporaryPath);
// Метаданные переносимы; временные пути используются только внутри серверной операции формирования архива.
public sealed record SubgraphExportResult(Guid ExportId, GraphArchive Archive, IReadOnlyList<ExportFileSource> Files);

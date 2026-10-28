using BusinessEntity.Core.Saving;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

// Проекция самостоятельной activity-записи без EF-сущности или репозитория наружу.
public sealed record StoredEntityComment(Guid Id, Guid EntityId, Guid? ParentId, string Data,
    DateTime CreatedDate, DateTime LastModifiedDate);
// Проекция свойства одного уровня графа, payload или чанка.
public sealed record StoredEntityProperty(Guid Id, Guid ParentEntityId, EntityPropertyOwner Owner,
    int PropertyType, string Data, string Metadata, DateTime CreatedDate, DateTime LastModifiedDate);
// Серверное описание файла для тонкого HTTP-адаптера; путь вычисляет только DataProvider.
public sealed record StoredEntityFile(Guid EntityId, string FileId, string Variant, string FileName,
    string ContentType, long Length, string PhysicalPath);
// Лёгкий состав версии без загрузки текста и HTML всех чанков.
public sealed record VersionChunkMembership(Guid ChunkId, int ChunkRowVersion, int Position);

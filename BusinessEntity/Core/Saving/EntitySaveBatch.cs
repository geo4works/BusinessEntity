using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Chunks;

namespace BusinessEntity.Core.Saving;

// Снимок связанных правок рабочего экземпляра; не содержит EF, SQL или исполняемых callback.
public sealed record EntitySaveBatch(Guid OperationId, string MiniAppId, Guid? EditSessionId)
{
    public BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives.GraphArchive? ImportSnapshot { get; init; }
    public IReadOnlyList<BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions.OwnedServiceStorageChange> OwnedServiceChanges { get; init; } = [];
    public IReadOnlyList<Classes.BusinessEntity> Entities { get; init; } = [];
    public IReadOnlyList<Guid> DeleteEntities { get; init; } = [];
    public IReadOnlyList<EntityPayloadChange> Payloads { get; init; } = [];
    public IReadOnlyList<BusinessEntityRelation> Relations { get; init; } = [];
    public IReadOnlyList<Guid> DeleteRelations { get; init; } = [];
    public IReadOnlyList<EntityPropertyChange> Properties { get; init; } = [];
    public IReadOnlyList<EntityPropertyKey> DeleteProperties { get; init; } = [];
    public IReadOnlyList<EntityCommentChange> Comments { get; init; } = [];
    public IReadOnlyList<EntityFileChange> Files { get; init; } = [];
    public IReadOnlyList<EntityPublicationChange> Publications { get; init; } = [];
    public IReadOnlyList<EntityVersionDescriptionChange> VersionDescriptions { get; init; } = [];
    public IReadOnlyList<EntityVersionRestore> Restores { get; init; } = [];
}

// Полный manifest автора и изменения чанков относительно явно указанной базы.
// ChunkOrder задаёт весь порядок автора; null сохраняет порядок базовой версии.
public sealed record EntityPayloadChange(Guid EntityId, int BaseVersion, IBusinessEntityData Data,
    IReadOnlyList<BusinessEntityDataChunk>? ChangedChunks = null, IReadOnlyList<Guid>? ChunkOrder = null,
    string VersionDescription = "");

// Свойства адресуются своему уровню без передачи storage DTO.
public enum EntityPropertyOwner { Entity, Data, Chunk }
// Независимое свойство движка; Data и Metadata — его объявленный владельцем формат.
public sealed record EntityPropertyChange(Guid Id, Guid ParentEntityId, EntityPropertyOwner Owner,
    int PropertyType, string Data, string Metadata);
// Удаление конкретного свойства своего уровня.
public sealed record EntityPropertyKey(Guid Id, EntityPropertyOwner Owner);
// Отдельная activity-запись, физически применяемая DataProvider.
public sealed record EntityCommentChange(Guid Id, Guid EntityId, Guid? ParentId, string Data,
    DateTime CreatedDate);
// Неизменяемый постоянный файл и его логический адрес у сущности.
public sealed record EntityFileChange(Guid EntityId, string FileId, string Variant,
    string FileName, string ContentType, byte[] Content, string? TemporaryFilePath = null);
// Публикация выбирает существующий снимок вне payload истории.
public sealed record EntityPublicationChange(Guid EntityId, int Version);
// Описание является метаданными версии, а не новым содержимым.
public sealed record EntityVersionDescriptionChange(Guid EntityId, int Version, string Description);
// Восстановление явно создаёт следующий снимок с автором восстановившего.
public sealed record EntityVersionRestore(Guid EntityId, int Version);
// Ссылка содержимого версии на неизменяемый постоянный файл.
public sealed record EntityFileReference(Guid EntityId, string FileId, string Variant);

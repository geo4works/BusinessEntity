using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.RichText;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Полный архив принимается как одна операция; ни одна сущность не видна до общего коммита.
internal sealed partial class DataProviderTransactionService
{
    // Проверяет текущий формат, идентичности, payload, составы версий и файловый manifest.
    private async Task<IReadOnlyList<SavedEntityVersion>> ImportArchiveAsync(KmsBusinessEntityDbContext db,
        GraphArchive archive, IReadOnlyList<PreparedEntityFile> files, DateTime now, CancellationToken ct)
    {
        if (archive.FormatVersion != GraphArchiveFormat.CurrentVersion)
            throw new NotSupportedException("Формат архива не поддерживается.");
        var ids = archive.Entities.Select(x => x.Card.Id).ToArray();
        if (ids.Length == 0 || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Length || !ids.Contains(archive.RootEntityId))
            throw new InvalidDataException("Некорректный набор сущностей архива.");
        if (await db.BusinessEntities.AnyAsync(x => ids.Contains(x.Id), ct))
            throw new InvalidDataException("Архив должен создавать новые идентичности.");
        var expectedFiles = archive.Entities.SelectMany(x => x.Files.Select(f => (Entity: x.Card.Id, File: f))).ToArray();
        if (expectedFiles.Length != files.Count || expectedFiles.Any(x => !files.Any(f =>
            f.Change.EntityId == x.Entity && f.Change.FileId == x.File.FileId && f.Change.Variant == x.File.Variant &&
            f.Change.FileName == x.File.FileName && f.Change.ContentType == x.File.ContentType &&
            f.Hash == x.File.ContentHash && f.Length == x.File.Length)))
            throw new InvalidDataException("Файлы не соответствуют manifest архива.");
        var chunkOwners = new Dictionary<Guid, Guid>();
        foreach (var entity in archive.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Card.Name) || !_archivePolicies.TryGetValue(entity.Card.EntityType, out var archivePolicy))
                throw new InvalidDataException("Неизвестный тип или пустое имя сущности архива.");
            if (archivePolicy.RequiresPayload && entity.Payloads.Count == 0)
                throw new InvalidDataException("Отсутствует обязательный payload архива.");
            _policies.TryGetValue(entity.Card.EntityType, out var policy);
            var versions = entity.Payloads.Select(x => x.Version).Order().ToArray();
            if (!versions.SequenceEqual(Enumerable.Range(1, versions.Length)) || (policy == null && versions.Length > 1))
                throw new InvalidDataException("Нарушена последовательность версий архива.");
            if (entity.Card.PublishedVersion < 0 || (entity.Card.PublishedVersion != 0 && !versions.Contains(entity.Card.PublishedVersion)))
                throw new InvalidDataException("Указатель публикации отсутствует в истории.");
            if (entity.Chunks.Select(x => (x.Id, x.RowVersion)).Distinct().Count() != entity.Chunks.Count)
                throw new InvalidDataException("Повторная строка чанка архива.");
            foreach (var chunk in entity.Chunks)
            {
                if (chunk.Id == Guid.Empty || chunk.RowVersion < 1 || !versions.Contains(chunk.RowVersion) || Hash(chunk.EnvelopeJson) != chunk.Checksum)
                    throw new InvalidDataException("Повреждён чанк архива.");
                if (chunkOwners.TryGetValue(chunk.Id, out var owner) && owner != entity.Card.Id)
                    throw new InvalidDataException("Один ChunkId принадлежит разным сущностям.");
                chunkOwners[chunk.Id] = entity.Card.Id;
            }
            if (policy?.UsesChunks != true && (entity.Chunks.Count != 0 || entity.ChunkReferences.Count != 0))
                throw new InvalidDataException("Тип не поддерживает чанки.");
            if (entity.ChunkReferences.Any(x => !versions.Contains(x.Version) || x.ChunkRowVersion > x.Version ||
                !entity.Chunks.Any(c => c.Id == x.ChunkId && c.RowVersion == x.ChunkRowVersion)))
                throw new InvalidDataException("Версия ссылается на отсутствующую строку чанка.");
            foreach (var group in entity.ChunkReferences.GroupBy(x => x.Version))
                if (!group.Select(x => x.Position).Order().SequenceEqual(Enumerable.Range(0, group.Count())) ||
                    group.Select(x => x.ChunkId).Distinct().Count() != group.Count())
                    throw new InvalidDataException("Повреждён порядок чанков версии.");
            db.BusinessEntities.Add(DataProviderMapper.ToDto(entity.Card));
        }
        if (archive.Relations.Select(x => x.Id).Distinct().Count() != archive.Relations.Count || archive.Relations.Any(x =>
            x.Id == Guid.Empty || !ids.Contains(x.ObjectAId) || !ids.Contains(x.ObjectBId) || string.IsNullOrWhiteSpace(x.RelationType)))
            throw new InvalidDataException("Некорректные связи архива.");
        await db.SaveChangesAsync(ct);
        var saved = new List<SavedEntityVersion>();
        foreach (var entity in archive.Entities)
        {
            var card = entity.Card;
            _policies.TryGetValue(card.EntityType, out var policy);
            var rows = new Dictionary<(Guid, int), BusinessEntityDataChunkDto>();
            foreach (var chunk in entity.Chunks)
            {
                var blocks = RichTextChunkStorageSerializer.DeserializeChunkData(chunk.EnvelopeJson);
                var row = PrepareChunk(new RichTextDocumentChunk { Id = chunk.Id, BusinessEntityId = card.Id,
                    SortOrder = chunk.SortOrder, Blocks = blocks });
                row.Version = chunk.RowVersion; row.CreatedDate = chunk.CreatedDate; row.LastModifiedDate = chunk.LastModifiedDate;
                rows.Add((row.Id, row.Version), row); db.BusinessEntityDataChunks.Add(row);
            }
            foreach (var payload in entity.Payloads.OrderBy(x => x.Version))
            {
                if (string.IsNullOrWhiteSpace(payload.Name)) throw new InvalidDataException("Пустое название версии.");
                var data = _codec.DeserializeEnvelope<IBusinessEntityData>(payload.EnvelopeJson);
                if (data.EntityType != card.EntityType) throw new InvalidDataException("Payload не соответствует типу карточки.");
                var refs = entity.ChunkReferences.Where(x => x.Version == payload.Version).OrderBy(x => x.Position).ToArray();
                var body = _codec.SerializePayload(data);
                var hash = ComputeContentHash(payload.Name, policy == null ? body : policy.GetContent(data),
                    refs.Select(x => (x.ChunkId, rows[(x.ChunkId, x.ChunkRowVersion)].Checksum!)));
                var envelope = DataPayloadEnvelopeSerializer.ReadEnvelope(payload.EnvelopeJson);
                db.BusinessEntityDataItems.Add(new BusinessEntityDataDto { Id = card.Id, BusinessEntityId = card.Id,
                    Version = payload.Version, Name = payload.Name, Data = DataPayloadEnvelopeSerializer.CreateEnvelopeJson(envelope.Kind,
                        body, envelope.CreatedByUserId, envelope.LastModifiedByUserId), ContentHash = hash, VersionDescription = payload.Description,
                    SavedByUserId = payload.SavedByUserId, CreatedDate = payload.CreatedDate, LastModifiedDate = payload.LastModifiedDate });
                foreach (var reference in refs)
                    db.ChunkReferences.Add(new ChunkReferenceRow { BusinessEntityId = card.Id, Version = payload.Version,
                        Position = reference.Position, ChunkId = reference.ChunkId, ChunkRowVersion = reference.ChunkRowVersion });
                if (policy != null)
                    foreach (var file in policy.GetFileReferences(card.Id, refs.Select(x => new RichTextDocumentChunk { Id = x.ChunkId,
                        BusinessEntityId = card.Id, Blocks = RichTextChunkStorageSerializer.DeserializeChunkData(rows[(x.ChunkId, x.ChunkRowVersion)].Data) }).ToArray()))
                        db.VersionFileReferences.Add(new VersionFileReferenceRow { SourceEntityId = card.Id, Version = payload.Version,
                            FileEntityId = file.EntityId, FileId = file.FileId, Variant = file.Variant });
            }
            foreach (var property in entity.Properties)
            {
                if (property.Id == Guid.Empty || (property.Owner == EntityPropertyOwner.Chunk
                    ? !entity.Chunks.Any(x => x.Id == property.ParentEntityId) : property.ParentEntityId != card.Id))
                    throw new InvalidDataException("Свойство не принадлежит сущности архива.");
                switch (property.Owner)
                {
                    case EntityPropertyOwner.Entity: db.BusinessEntityProperties.Add(ImportProperty<BusinessEntityPropertyDto>(property)); break;
                    case EntityPropertyOwner.Data:
                        if (entity.Payloads.Count == 0) throw new InvalidDataException("У свойства отсутствует payload.");
                        db.BusinessEntityDataProperties.Add(ImportProperty<BusinessEntityDataPropertyDto>(property)); break;
                    case EntityPropertyOwner.Chunk: db.BusinessEntityDataChunkProperties.Add(ImportProperty<BusinessEntityDataChunkPropertyDto>(property)); break;
                    default: throw new InvalidDataException("Неизвестный владелец свойства.");
                }
            }
            if (entity.Comments.Select(x => x.Id).Distinct().Count() != entity.Comments.Count)
                throw new InvalidDataException("Повторный комментарий.");
            foreach (var comment in entity.Comments)
            {
                if (comment.Id == Guid.Empty || comment.EntityId != card.Id ||
                    (comment.ParentId.HasValue && !entity.Comments.Any(x => x.Id == comment.ParentId)))
                    throw new InvalidDataException("Комментарий ссылается на чужую сущность или отсутствующий parent.");
                var seen = new HashSet<Guid> { comment.Id }; var parent = comment.ParentId;
                while (parent.HasValue)
                {
                    if (!seen.Add(parent.Value)) throw new InvalidDataException("Цикл комментариев архива.");
                    parent = entity.Comments.Single(x => x.Id == parent).ParentId;
                }
                db.BusinessEntityComments.Add(new BusinessEntityCommentDto { Id = comment.Id, BusinessEntityId = card.Id,
                    ParentId = comment.ParentId, Data = comment.Data, CreatedDate = comment.CreatedDate, LastModifiedDate = comment.LastModifiedDate });
            }
            if (entity.Payloads.Count > 0) saved.Add(new(card.Id, entity.Payloads.Max(x => x.Version), true));
        }
        db.BusinessEntityRelations.AddRange(archive.Relations.Select(DataProviderMapper.ToDto));
        await ValidateContainmentAsync(db, ct);
        await _files.PrepareAsync(db, Guid.NewGuid(), files, ct);
        await _files.AssertVersionReferencesAsync(db, ct);
        return saved;
    }

    // Технические свойства сохраняются без потери исходного времени и metadata.
    private static T ImportProperty<T>(StoredEntityProperty source) where T : BaseEntity, IPropertyDto, new() =>
        new() { Id = source.Id, ParentEntityId = source.ParentEntityId, PropertyType = source.PropertyType,
            Data = source.Data, Metadata = source.Metadata, CreatedDate = source.CreatedDate, LastModifiedDate = source.LastModifiedDate };
}

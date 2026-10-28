using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Применяет свойства, activity и публикацию в той же единице сохранения.
internal sealed partial class DataProviderTransactionService
{
    // Изменяет дополнительные записи без отдельных репозиторных коммитов.
    private static async Task ApplySupplementaryAsync(KmsBusinessEntityDbContext db, FrozenSupplement batch,
        DateTime now, CancellationToken ct)
    {
        foreach (var property in batch.Properties)
        {
            if (property.Id == Guid.Empty || property.ParentEntityId == Guid.Empty)
                throw new ArgumentException("Некорректное свойство.");
            var ownerExists = property.Owner switch
            {
                EntityPropertyOwner.Entity => await db.BusinessEntities.FindAsync([property.ParentEntityId], ct) != null,
                EntityPropertyOwner.Data => db.BusinessEntityDataItems.Local.Any(x => x.BusinessEntityId == property.ParentEntityId)
                    || await db.BusinessEntityDataItems.AnyAsync(x => x.BusinessEntityId == property.ParentEntityId, ct),
                EntityPropertyOwner.Chunk => db.BusinessEntityDataChunks.Local.Any(x => x.Id == property.ParentEntityId)
                    || await db.BusinessEntityDataChunks.AnyAsync(x => x.Id == property.ParentEntityId, ct),
                _ => false
            };
            if (!ownerExists) throw new ArgumentException("У свойства отсутствует владелец.");
            switch (property.Owner)
            {
                case EntityPropertyOwner.Entity:
                    await UpsertPropertyAsync(db, db.BusinessEntityProperties, property, now, ct); break;
                case EntityPropertyOwner.Data:
                    await UpsertPropertyAsync(db, db.BusinessEntityDataProperties, property, now, ct); break;
                case EntityPropertyOwner.Chunk:
                    await UpsertPropertyAsync(db, db.BusinessEntityDataChunkProperties, property, now, ct); break;
                default: throw new ArgumentOutOfRangeException(nameof(property.Owner));
            }
        }
        foreach (var property in batch.DeleteProperties)
        {
            switch (property.Owner)
            {
                case EntityPropertyOwner.Entity: await DeletePropertyAsync(db, db.BusinessEntityProperties, property.Id, ct); break;
                case EntityPropertyOwner.Data: await DeletePropertyAsync(db, db.BusinessEntityDataProperties, property.Id, ct); break;
                case EntityPropertyOwner.Chunk: await DeletePropertyAsync(db, db.BusinessEntityDataChunkProperties, property.Id, ct); break;
                default: throw new ArgumentOutOfRangeException(nameof(property.Owner));
            }
        }
        if (batch.Comments.Select(x => x.Id).Distinct().Count() != batch.Comments.Count)
            throw new ArgumentException("Повторный ID комментария.");
        var newComments = batch.Comments.ToDictionary(x => x.Id);
        foreach (var comment in batch.Comments)
        {
            if (comment.Id == Guid.Empty || comment.EntityId == Guid.Empty) throw new ArgumentException("ID комментария обязателен.");
            var seen = new HashSet<Guid> { comment.Id }; var parent = comment.ParentId;
            while (parent.HasValue && newComments.TryGetValue(parent.Value, out var ancestor))
            {
                if (!seen.Add(parent.Value)) throw new ArgumentException("Цикл комментариев запрещён.");
                parent = ancestor.ParentId;
            }
            if (comment.ParentId.HasValue && !await db.BusinessEntityComments.AnyAsync(x => x.Id == comment.ParentId && x.BusinessEntityId == comment.EntityId, ct)
                && !batch.Comments.Any(x => x.Id == comment.ParentId && x.EntityId == comment.EntityId))
                throw new ArgumentException("Ответ относится к другому или отсутствующему комментарию.");
            db.BusinessEntityComments.Add(new BusinessEntityCommentDto { Id = comment.Id, BusinessEntityId = comment.EntityId,
                ParentId = comment.ParentId, Data = comment.Data, CreatedDate = comment.CreatedDate, LastModifiedDate = now });
        }
        foreach (var publication in batch.Publications)
        {
            var entity = await db.BusinessEntities.FindAsync([publication.EntityId], ct)
                ?? throw new KeyNotFoundException("Публикуемая сущность не найдена.");
            if (publication.Version < 0 || (publication.Version != 0 && !db.BusinessEntityDataItems.Local.Any(x => x.Id == publication.EntityId && x.Version == publication.Version)
                && !await db.BusinessEntityDataItems.AnyAsync(x => x.Id == publication.EntityId && x.Version == publication.Version, ct)))
                throw new ArgumentException("Публикуемая версия не найдена.");
            entity.PublishedVersion = publication.Version;
        }
        foreach (var description in batch.VersionDescriptions)
        {
            var row = await db.BusinessEntityDataItems.FindAsync([description.EntityId, description.Version], ct)
                ?? throw new KeyNotFoundException("Версия для описания не найдена.");
            row.VersionDescription = description.Description.Trim();
        }
    }

    // Сохраняет одно свойство в текущем общем контексте.
    private static async Task UpsertPropertyAsync<T>(KmsBusinessEntityDbContext db, DbSet<T> set,
        EntityPropertyChange change, DateTime now, CancellationToken ct) where T : BaseEntity, IPropertyDto, new()
    {
        var row = await set.FindAsync([change.Id], ct);
        if (row == null) { row = new T { Id = change.Id, CreatedDate = now }; set.Add(row); }
        row.LastModifiedDate = now; row.ParentEntityId = change.ParentEntityId; row.PropertyType = change.PropertyType;
        row.Data = change.Data; row.Metadata = change.Metadata;
    }

    // Удаляет свойство без отдельного SaveChanges.
    private static async Task DeletePropertyAsync<T>(KmsBusinessEntityDbContext db, DbSet<T> set, Guid id, CancellationToken ct) where T : class
    {
        var row = await set.FindAsync([id], ct);
        if (row != null) set.Remove(row);
    }

    // Собирает всё поддерево Contains до удаления; FK и удаление свойств входят в тот же коммит.
    private static async Task<(Guid[] Entities, Guid[] Relations)> DeleteEntitiesAsync(KmsBusinessEntityDbContext db, IReadOnlyList<Guid> roots, IReadOnlyList<Guid> editedEntities, CancellationToken ct)
    {
        if (roots.Count == 0) return ([], []);
        var allEdges = await EffectiveRelationsAsync(db, ct);
        var edges = allEdges.Where(x => x.RelationType == "Contains").ToArray();
        var ids = roots.ToHashSet(); var queue = new Queue<Guid>(roots);
        while (queue.TryDequeue(out var id))
            foreach (var edge in edges.Where(e => e.ObjectAId == id))
                if (ids.Add(edge.ObjectBId)) queue.Enqueue(edge.ObjectBId);
        if (editedEntities.Any(ids.Contains))
            throw new ArgumentException("Редактирование и удаление одной сущности нельзя смешивать в пакете.");
        await db.VersionFileReferences.Where(x => ids.Contains(x.SourceEntityId)).ExecuteDeleteAsync(ct);
        var chunkIds = await db.BusinessEntityDataChunks.Where(x => ids.Contains(x.BusinessEntityId)).Select(x => x.Id).ToListAsync(ct);
        db.BusinessEntityProperties.RemoveRange(db.BusinessEntityProperties.Local.Where(x => ids.Contains(x.ParentEntityId)).ToArray());
        db.BusinessEntityDataProperties.RemoveRange(db.BusinessEntityDataProperties.Local.Where(x => ids.Contains(x.ParentEntityId)).ToArray());
        db.BusinessEntityDataChunkProperties.RemoveRange(db.BusinessEntityDataChunkProperties.Local.Where(x => chunkIds.Contains(x.ParentEntityId)).ToArray());
        db.BusinessEntityProperties.RemoveRange(await db.BusinessEntityProperties.Where(x => ids.Contains(x.ParentEntityId)).ToListAsync(ct));
        db.BusinessEntityDataProperties.RemoveRange(await db.BusinessEntityDataProperties.Where(x => ids.Contains(x.ParentEntityId)).ToListAsync(ct));
        db.BusinessEntityDataChunkProperties.RemoveRange(await db.BusinessEntityDataChunkProperties.Where(x => chunkIds.Contains(x.ParentEntityId)).ToListAsync(ct));
        db.BusinessEntities.RemoveRange(await db.BusinessEntities.Where(x => ids.Contains(x.Id)).ToListAsync(ct));
        var deletedEdges = allEdges.Where(x => ids.Contains(x.ObjectAId) || ids.Contains(x.ObjectBId)).ToArray();
        foreach (var edge in deletedEdges)
        {
            var tracked = await db.BusinessEntityRelations.FindAsync([edge.Id], ct);
            if (tracked != null) db.BusinessEntityRelations.Remove(tracked);
        }
        return (ids.ToArray(), deletedEdges.Select(x => x.Id).ToArray());
    }
}

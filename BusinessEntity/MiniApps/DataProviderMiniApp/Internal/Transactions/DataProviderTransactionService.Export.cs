using System.Data;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Экспорт использует тот же writer gate, поэтому metadata и неизменяемые файлы образуют один снимок.
internal sealed partial class DataProviderTransactionService
{
    // Копирует файлы потоком во временный lease; большие видео не загружаются в byte[].
    public async Task<SubgraphExportResult> ExportAsync(SubgraphExportScope scope, BusRequestContext actor,
        Storage.ExportFileLeases leases, CancellationToken ct = default)
    {
        await _writer.WaitAsync(ct);
        (Guid Id, string Directory) lease = default;
        try
        {
            lease = leases.Create(actor);
            await using var db = new KmsBusinessEntityDbContext(_options);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var cards = await db.BusinessEntities.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
            if (!cards.ContainsKey(scope.RootEntityId)) throw new KeyNotFoundException("Корень экспорта не найден.");
            var edges = await db.BusinessEntityRelations.AsNoTracking().ToListAsync(ct);
            var dependencies = await db.VersionFileReferences.AsNoTracking().ToListAsync(ct);
            var selected = new HashSet<Guid> { scope.RootEntityId }; var pending = new Queue<Guid>(selected);
            while (pending.TryDequeue(out var current))
            {
                foreach (var edge in edges.Where(x => x.ObjectAId == current && scope.FollowOutgoingRelations.Contains(x.RelationType)))
                    if (cards.TryGetValue(edge.ObjectBId, out var card) && !scope.ExcludedDescendantTypes.Contains(card.EntityType)
                        && selected.Add(card.Id)) pending.Enqueue(card.Id);
                // Историческая файловая зависимость также входит в экспорт, даже без видимого ребра дерева.
                foreach (var dependency in dependencies.Where(x => x.SourceEntityId == current))
                    if (selected.Add(dependency.FileEntityId)) pending.Enqueue(dependency.FileEntityId);
            }
            var entities = new List<ArchivedEntity>(); var files = new Dictionary<string, ExportFileSource>(StringComparer.Ordinal);
            foreach (var id in selected.Order())
            {
                var versions = await db.BusinessEntityDataItems.AsNoTracking().Where(x => x.BusinessEntityId == id).OrderBy(x => x.Version)
                    .Select(x => new ArchivedPayload(x.Version, x.Name, x.Data, x.ContentHash, x.VersionDescription,
                        x.SavedByUserId, x.CreatedDate, x.LastModifiedDate)).ToListAsync(ct);
                var chunks = await db.BusinessEntityDataChunks.AsNoTracking().Where(x => x.BusinessEntityId == id).OrderBy(x => x.Id).ThenBy(x => x.Version)
                    .Select(x => new ArchivedChunk(x.Id, x.Version, x.SortOrder, x.Data, x.Checksum!, x.CreatedDate, x.LastModifiedDate)).ToListAsync(ct);
                var chunkIds = chunks.Select(x => x.Id).Distinct().ToArray();
                var references = await db.ChunkReferences.AsNoTracking().Where(x => x.BusinessEntityId == id).OrderBy(x => x.Version).ThenBy(x => x.Position)
                    .Select(x => new ArchivedChunkReference(x.Version, x.Position, x.ChunkId, x.ChunkRowVersion)).ToListAsync(ct);
                var properties = new List<StoredEntityProperty>();
                properties.AddRange(await db.BusinessEntityProperties.AsNoTracking().Where(x => x.ParentEntityId == id).Select(x =>
                    new StoredEntityProperty(x.Id, x.ParentEntityId, EntityPropertyOwner.Entity, x.PropertyType, x.Data, x.Metadata, x.CreatedDate, x.LastModifiedDate)).ToListAsync(ct));
                properties.AddRange(await db.BusinessEntityDataProperties.AsNoTracking().Where(x => x.ParentEntityId == id).Select(x =>
                    new StoredEntityProperty(x.Id, x.ParentEntityId, EntityPropertyOwner.Data, x.PropertyType, x.Data, x.Metadata, x.CreatedDate, x.LastModifiedDate)).ToListAsync(ct));
                properties.AddRange(await db.BusinessEntityDataChunkProperties.AsNoTracking().Where(x => chunkIds.Contains(x.ParentEntityId)).Select(x =>
                    new StoredEntityProperty(x.Id, x.ParentEntityId, EntityPropertyOwner.Chunk, x.PropertyType, x.Data, x.Metadata, x.CreatedDate, x.LastModifiedDate)).ToListAsync(ct));
                var comments = await db.BusinessEntityComments.AsNoTracking().Where(x => x.BusinessEntityId == id).OrderBy(x => x.CreatedDate).Select(x =>
                    new StoredEntityComment(x.Id, x.BusinessEntityId, x.ParentId, x.Data, x.CreatedDate, x.LastModifiedDate)).ToListAsync(ct);
                var fileRows = await db.EntityFiles.AsNoTracking().Where(x => x.EntityId == id).OrderBy(x => x.FileId).ThenBy(x => x.Variant).ToListAsync(ct);
                var manifest = new List<ArchivedFile>();
                foreach (var file in fileRows)
                {
                    var entry = $"files/{id:D}/{file.ContentHash}.blob";
                    manifest.Add(new(file.FileId, file.Variant, file.FileName, file.ContentType, file.ContentHash, file.Length, entry));
                    if (files.ContainsKey(entry)) continue;
                    var temporary = Path.Combine(lease.Directory, Guid.NewGuid().ToString("N") + ".blob");
                    await using (var source = File.OpenRead(_files.ResolvePath(file)))
                    await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                        await source.CopyToAsync(destination, ct);
                    files.Add(entry, new(entry, temporary));
                }
                entities.Add(new(DataProviderMapper.ToBusinessEntity(cards[id]), versions, chunks, references, properties, comments, manifest));
            }
            var relations = edges.Where(x => selected.Contains(x.ObjectAId) && selected.Contains(x.ObjectBId))
                .Select(DataProviderMapper.ToBusinessEntityRelation).ToArray();
            await transaction.CommitAsync(ct);
            return new(lease.Id, new(GraphArchiveFormat.CurrentVersion, scope.RootEntityId, entities, relations), files.Values.ToArray());
        }
        catch { if (lease.Id != Guid.Empty) leases.Release(lease.Id, actor); throw; }
        finally { _writer.Release(); }
    }
}

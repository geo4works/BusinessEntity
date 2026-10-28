using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Механизм полных авторских версий; предметное правило содержимого передаётся политикой типа.
internal sealed partial class DataProviderTransactionService
{
    // Строит снимок от базы автора, а не от последнего сохранения другого окна.
    private async Task<SavedEntityVersion> SavePayloadAsync(KmsBusinessEntityDbContext db,
        PreparedPayload payload, BusRequestContext actor, DateTime now, CancellationToken ct)
    {
        var entity = await db.BusinessEntities.FindAsync([payload.EntityId], ct)
            ?? throw new KeyNotFoundException("Владелец payload не найден.");
        if (entity.EntityType != payload.Type || string.IsNullOrWhiteSpace(payload.Name) || payload.BaseVersion < 0)
            throw new ArgumentException("Payload не соответствует карточке или базовой версии.");
        var latest = await db.BusinessEntityDataItems.Where(x => x.BusinessEntityId == payload.EntityId)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        BusinessEntityDataDto? baseRow = null;
        if (payload.BaseVersion > 0)
            baseRow = await db.BusinessEntityDataItems.FindAsync([payload.EntityId, payload.BaseVersion], ct)
                ?? throw new KeyNotFoundException("Исходная версия автора не найдена.");
        else if (latest != null) throw new ArgumentException("Для существующего payload обязательна BaseVersion.");
        _policies.TryGetValue(payload.Type, out var policy);
        var keepsHistory = policy != null;
        var nextVersion = latest == null ? 1 : keepsHistory ? checked(latest.Version + 1) : latest.Version;
        var baseReferences = baseRow == null ? [] : await db.ChunkReferences.AsNoTracking()
            .Where(x => x.BusinessEntityId == payload.EntityId && x.Version == baseRow.Version)
            .OrderBy(x => x.Position).ToListAsync(ct);
        var references = baseReferences.ToDictionary(x => x.ChunkId, x => x.ChunkRowVersion);
        var changed = payload.Chunks ?? [];
        var order = payload.Order ?? baseReferences.Select(x => x.ChunkId).ToArray();
        if (changed.Select(x => x.Id).Distinct().Count() != changed.Length || order.Distinct().Count() != order.Length)
            throw new ArgumentException("Повторный ChunkId в снимке.");
        if (policy?.UsesChunks != true && (changed.Length != 0 || order.Length != 0))
            throw new ArgumentException("Тип не поддерживает чанки.");
        var newRows = new List<BusinessEntityDataChunkDto>();
        var checksums = new Dictionary<Guid, string>();
        var candidateRows = new Dictionary<Guid, BusinessEntityDataChunkDto>();
        foreach (var row in changed)
        {
            if (row.BusinessEntityId != payload.EntityId || !order.Contains(row.Id))
                throw new ArgumentException("Изменённый чанк отсутствует в составе версии своего владельца.");
            if (await db.BusinessEntityDataChunks.AnyAsync(x => x.Id == row.Id && x.BusinessEntityId != payload.EntityId, ct))
                throw new ArgumentException("ChunkId принадлежит другой сущности.");
            var old = references.TryGetValue(row.Id, out var oldVersion)
                ? await db.BusinessEntityDataChunks.FindAsync([row.Id, oldVersion], ct) : null;
            if (old != null && old.Data == row.Data) { checksums[row.Id] = old.Checksum!; candidateRows[row.Id] = old; continue; }
            row.Version = nextVersion; row.CreatedDate = now; row.LastModifiedDate = now;
            references[row.Id] = nextVersion; checksums[row.Id] = row.Checksum!; candidateRows[row.Id] = row; newRows.Add(row);
        }
        foreach (var id in order.Where(id => !checksums.ContainsKey(id)))
        {
            if (!references.TryGetValue(id, out var rowVersion)) throw new ArgumentException("Чанк не находится в базе автора.");
            var row = await db.BusinessEntityDataChunks.FindAsync([id, rowVersion], ct)
                ?? throw new InvalidDataException("Отсутствует конкретная строка чанка базовой версии.");
            if (row.BusinessEntityId != payload.EntityId) throw new InvalidDataException("Чужая строка в составе версии.");
            checksums[id] = row.Checksum!; candidateRows[id] = row;
        }
        var contentHash = ComputeContentHash(payload.Name, keepsHistory ? payload.SemanticContent : payload.Json,
            order.Select(id => (id, checksums[id])));
        if (latest?.ContentHash == contentHash)
        {
            if (!string.IsNullOrWhiteSpace(payload.Description)) latest.VersionDescription = payload.Description.Trim();
            return new(payload.EntityId, latest.Version, false);
        }
        entity.Name = payload.Name; entity.LastModifiedDate = now; entity.LastModifiedByUserId = actor.UserId;
        var envelope = DataPayloadEnvelopeSerializer.CreateEnvelopeJson(_codec.GetStorageKind(payload.Type), payload.Json,
            entity.CreatedByUserId, actor.UserId);
        if (latest != null && !keepsHistory)
        {
            latest.Data = envelope; latest.Name = payload.Name; latest.ContentHash = contentHash;
            latest.LastModifiedDate = now; latest.SavedByUserId = actor.UserId;
            return new(payload.EntityId, latest.Version, false);
        }
        db.BusinessEntityDataItems.Add(new BusinessEntityDataDto
        {
            Id = payload.EntityId, BusinessEntityId = payload.EntityId, Version = nextVersion,
            Name = payload.Name, ContentHash = contentHash, Data = envelope, SavedByUserId = actor.UserId,
            CreatedDate = now, LastModifiedDate = now, VersionDescription = payload.Description
        });
        db.BusinessEntityDataChunks.AddRange(newRows);
        for (var position = 0; position < order.Length; position++)
            db.ChunkReferences.Add(new ChunkReferenceRow { BusinessEntityId = payload.EntityId, Version = nextVersion,
                Position = position, ChunkId = order[position], ChunkRowVersion = references[order[position]] });
        if (policy != null)
            foreach (var file in policy.GetFileReferences(payload.EntityId, order.Select(id => new Core.Chunks.BusinessEntityDataChunk
                { Id = id, BusinessEntityId = payload.EntityId, Blocks = RichTextChunkStorageSerializer.DeserializeChunkData(candidateRows[id].Data) }).ToArray()))
                db.VersionFileReferences.Add(new VersionFileReferenceRow { SourceEntityId = payload.EntityId, Version = nextVersion,
                    FileEntityId = file.EntityId, FileId = file.FileId, Variant = file.Variant });
        return new(payload.EntityId, nextVersion, true);
    }

    // Единый fingerprint используется обычным сохранением и восстановлением архива.
    private static string ComputeContentHash(string name, string content, IEnumerable<(Guid Id, string Checksum)> chunks) =>
        Hash(JsonSerializer.Serialize(new { Name = name, Content = content,
            Chunks = chunks.Select(x => new { x.Id, x.Checksum }) }, StorageJsonOptions.Default));

    // Восстанавливает полный выбранный снимок без переписывания прежних строк и публикации.
    private async Task<SavedEntityVersion> RestoreAsync(KmsBusinessEntityDbContext db, EntityVersionRestore restore,
        BusRequestContext actor, DateTime now, CancellationToken ct)
    {
        var entity = await db.BusinessEntities.FindAsync([restore.EntityId], ct)
            ?? throw new KeyNotFoundException("Сущность для восстановления не найдена.");
        if (!_policies.ContainsKey(entity.EntityType)) throw new ArgumentException("Тип не хранит историю.");
        var selected = await db.BusinessEntityDataItems.FindAsync([restore.EntityId, restore.Version], ct)
            ?? throw new KeyNotFoundException("Восстанавливаемая версия не найдена.");
        var latest = await db.BusinessEntityDataItems.Where(x => x.BusinessEntityId == restore.EntityId).MaxAsync(x => x.Version, ct);
        var version = checked(latest + 1);
        var envelope = DataPayloadEnvelopeSerializer.ReadEnvelope(selected.Data);
        db.BusinessEntityDataItems.Add(new BusinessEntityDataDto
        {
            Id = restore.EntityId, BusinessEntityId = restore.EntityId, Version = version,
            Name = selected.Name, ContentHash = selected.ContentHash, SavedByUserId = actor.UserId,
            Data = DataPayloadEnvelopeSerializer.CreateEnvelopeJson(envelope.Kind, envelope.PayloadJson, entity.CreatedByUserId, actor.UserId),
            CreatedDate = now, LastModifiedDate = now, VersionDescription = $"Восстановлена версия {restore.Version}"
        });
        var references = await db.ChunkReferences.AsNoTracking().Where(x => x.BusinessEntityId == restore.EntityId && x.Version == restore.Version).ToListAsync(ct);
        db.ChunkReferences.AddRange(references.Select(x => new ChunkReferenceRow { BusinessEntityId = restore.EntityId,
            Version = version, Position = x.Position, ChunkId = x.ChunkId, ChunkRowVersion = x.ChunkRowVersion }));
        var files = await db.VersionFileReferences.AsNoTracking().Where(x => x.SourceEntityId == restore.EntityId && x.Version == restore.Version).ToListAsync(ct);
        db.VersionFileReferences.AddRange(files.Select(x => new VersionFileReferenceRow { SourceEntityId = restore.EntityId, Version = version,
            FileEntityId = x.FileEntityId, FileId = x.FileId, Variant = x.Variant }));
        entity.Name = selected.Name; entity.LastModifiedDate = now; entity.LastModifiedByUserId = actor.UserId;
        return new(restore.EntityId, version, true);
    }
}

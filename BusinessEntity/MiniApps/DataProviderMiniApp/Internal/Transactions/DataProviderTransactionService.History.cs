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
        var baseReferences=baseRow==null?[]:await db.ChunkReferences.AsNoTracking().Where(x=>x.BusinessEntityId==payload.EntityId&&x.Version==baseRow.Version).OrderBy(x=>x.Position).ToListAsync(ct);
        var baseline=baseRow==null?[]:await (from reference in db.ChunkReferences.AsNoTracking() where reference.BusinessEntityId==payload.EntityId&&reference.Version==baseRow.Version
            join chunk in db.BusinessEntityDataChunks.AsNoTracking() on new{Id=reference.ChunkId,Version=reference.ChunkRowVersion} equals new{chunk.Id,chunk.Version}
            select new ChunkStamp(chunk.Id,chunk.Version,chunk.Checksum!)).ToListAsync(ct);
        if(baseline.Count!=baseReferences.Count)throw new InvalidDataException("Отсутствует конкретная строка базы автора.");
        StoredPreparedChunks? staged=null;
        if(payload.PreparedChunkSetId is {} prepared){if(payload.Chunks?.Length>0)throw new ArgumentException("Смешаны bytes и sealed chunk set.");staged=await RequirePreparedChunksAsync(db,prepared,payload.EntityId,actor,ct);}
        var changes=staged?.Public.Index.Select(x=>new ChunkStamp(x.Id,0,x.Checksum)).ToArray()??(payload.Chunks??[]).Select(x=>new ChunkStamp(x.Id,0,x.Checksum!)).ToArray();
        var order=payload.Order??baseReferences.Select(x=>x.ChunkId).ToArray();var positions=order.Select((id,index)=>(id,index)).ToDictionary(x=>x.id,x=>x.index);
        if(changes.Select(x=>x.Id).Distinct().Count()!=changes.Length||order.Distinct().Count()!=order.Length)throw new ArgumentException("Повторный ChunkId.");
        if(policy?.UsesChunks!=true&&(changes.Length>0||order.Length>0||staged!=null))throw new ArgumentException("Тип не поддерживает чанки.");
        var changeIds=changes.Select(x=>x.Id).ToArray();if(await db.BusinessEntityDataChunks.AsNoTracking().AnyAsync(x=>changeIds.Contains(x.Id)&&x.BusinessEntityId!=payload.EntityId,ct))throw new ArgumentException("Чужой ChunkId.");
        var references=baseline.ToDictionary(x=>x.Id,x=>x.Version);var checksums=baseline.ToDictionary(x=>x.Id,x=>x.Checksum);var inserts=new HashSet<Guid>();
        foreach(var changed in changes)
        {if(!positions.ContainsKey(changed.Id))throw new ArgumentException("Изменённый чанк отсутствует в полном составе.");if(!checksums.TryGetValue(changed.Id,out var old)||old!=changed.Checksum){references[changed.Id]=nextVersion;inserts.Add(changed.Id);}checksums[changed.Id]=changed.Checksum;}
        if(order.Any(id=>!checksums.ContainsKey(id)))throw new ArgumentException("Чанк отсутствует в базе автора.");
        var contentHash=ComputeContentHash(payload.Name,keepsHistory?payload.SemanticContent:payload.Json,order.Select(id=>(id,checksums[id])));
        if(latest?.ContentHash==contentHash){if(!string.IsNullOrWhiteSpace(payload.Description))latest.VersionDescription=payload.Description.Trim();return new(payload.EntityId,latest.Version,false);}
        entity.Name=payload.Name;entity.LastModifiedDate=now;entity.LastModifiedByUserId=actor.UserId;
        var envelope=DataPayloadEnvelopeSerializer.CreateEnvelopeJson(_codec.GetStorageKind(payload.Type),payload.Json,entity.CreatedByUserId,actor.UserId);
        if(latest!=null&&!keepsHistory){latest.Data=envelope;latest.Name=payload.Name;latest.ContentHash=contentHash;latest.LastModifiedDate=now;latest.SavedByUserId=actor.UserId;return new(payload.EntityId,latest.Version,false);}
        db.BusinessEntityDataItems.Add(new BusinessEntityDataDto{Id=payload.EntityId,BusinessEntityId=payload.EntityId,Version=nextVersion,Name=payload.Name,ContentHash=contentHash,Data=envelope,SavedByUserId=actor.UserId,CreatedDate=now,LastModifiedDate=now,VersionDescription=payload.Description});
        // Все flush остаются внутри единственной SQL transaction; тела отсоединяются после ограниченного окна.
        await db.SaveChangesAsync(ct);var pending=new List<BusinessEntityDataChunkDto>(64);var seen=new HashSet<Guid>();
        if(staged!=null)
        {
            await using var stream=File.OpenRead(_files.ApiStaging.PreparedPath(staged.Public.Id));var index=0;
            await foreach(var row in ParsePreparedChunksAsync(stream,payload.EntityId,ct))
            {if(index>=staged.Public.Index.Count||staged.Public.Index[index].Id!=row.Id||staged.Public.Index[index].Checksum!=row.Checksum)throw new InvalidDataException("Sealed chunks изменены.");index++;await AddChunkAsync(row);}
            if(index!=staged.Public.Index.Count)throw new InvalidDataException("Sealed chunks неполны.");
        }
        else foreach(var row in payload.Chunks??[])await AddChunkAsync(row);
        await FlushChunksAsync();if(seen.Count!=changes.Length)throw new InvalidDataException("Не все changed chunks прочитаны.");
        foreach(var window in order.Chunk(128))
        {
            var refs=window.Select(id=>new ChunkReferenceRow{BusinessEntityId=payload.EntityId,Version=nextVersion,Position=positions[id],ChunkId=id,ChunkRowVersion=references[id]}).ToArray();db.ChunkReferences.AddRange(refs);await db.SaveChangesAsync(ct);foreach(var reference in refs)db.Entry(reference).State=EntityState.Detached;
        }
        if(policy!=null)
        {
            var files=new HashSet<EntityFileReference>();
            for(var offset=0;offset<order.Length;offset+=64)
            {
                var window=await(from reference in db.ChunkReferences.AsNoTracking() where reference.BusinessEntityId==payload.EntityId&&reference.Version==nextVersion&&reference.Position>=offset&&reference.Position<offset+64
                    join chunk in db.BusinessEntityDataChunks.AsNoTracking() on new{Id=reference.ChunkId,Version=reference.ChunkRowVersion} equals new{chunk.Id,chunk.Version} select new{chunk.Id,chunk.Data}).ToArrayAsync(ct);
                foreach(var file in policy.GetFileReferences(payload.EntityId,window.Select(row=>new Core.Chunks.BusinessEntityDataChunk{Id=row.Id,BusinessEntityId=payload.EntityId,Blocks=RichTextChunkStorageSerializer.DeserializeChunkData(row.Data)}).ToArray()))files.Add(file);
            }
            foreach(var file in files)db.VersionFileReferences.Add(new VersionFileReferenceRow{SourceEntityId=payload.EntityId,Version=nextVersion,FileEntityId=file.EntityId,FileId=file.FileId,Variant=file.Variant});
        }
        return new(payload.EntityId,nextVersion,true);
        async Task AddChunkAsync(BusinessEntityDataChunkDto row)
        {
            if(row.BusinessEntityId!=payload.EntityId||!checksums.TryGetValue(row.Id,out var expected)||row.Checksum!=expected||!seen.Add(row.Id))throw new InvalidDataException("Неправильный normalized chunk.");
            if(!inserts.Contains(row.Id))return;row.Version=nextVersion;row.CreatedDate=now;row.LastModifiedDate=now;row.SortOrder=positions[row.Id];db.BusinessEntityDataChunks.Add(row);pending.Add(row);if(pending.Count>=64)await FlushChunksAsync();
        }
        async Task FlushChunksAsync(){if(pending.Count==0)return;await db.SaveChangesAsync(ct);foreach(var row in pending)db.Entry(row).State=EntityState.Detached;pending.Clear();}
    }
    private sealed record ChunkStamp(Guid Id,int Version,string Checksum);

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

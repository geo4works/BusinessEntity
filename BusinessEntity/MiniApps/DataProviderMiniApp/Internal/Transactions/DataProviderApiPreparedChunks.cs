using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessEntity.Core.Chunks;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

internal sealed partial class DataProviderTransactionService
{
    public void AbandonPrepared(AbandonApiPreparedChunksRequest request)=>_files.ApiStaging.AbandonPrepared(request.LeaseId,request.Context);
    private const string PreparedChunksKind="ApiPreparedChunksV1";
    public async Task<OpenApiPreparedChunksResponse> OpenPreparedChunksAsync(OpenApiPreparedChunksRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);if(actor.ApiOperationId is not {} operation||request.SetId!=StableOperationIdentity.Create(operation,"prepared-chunks")||request.EntityId==Guid.Empty)throw new UnauthorizedAccessException();
        await using var db=new KmsBusinessEntityDbContext(_options);var (_,upload)=await RequireUploadAsync(db,request.UploadId,actor,ct);var receipt=DeserializeReceipt(await db.TechnicalStates.FindAsync([operation],ct)??throw new UnauthorizedAccessException());
        if(receipt.Identity.WriteMode!=ApiWriteMode.Deferred||receipt.Identity.OwnerMiniAppId!=_persistenceRules.UploadPurposes[upload.Public.Purpose].OwnerMiniAppId||upload.Public.State!="Ready"||upload.ConsumedBy.HasValue)throw new UnauthorizedAccessException();
        await AuthorizeUploadAsync(db,upload,actor,receipt.Identity.CapabilityId,ct);
        var row=await db.TechnicalStates.FindAsync([request.SetId],ct);if(row!=null){VerifyTechnicalOwner(row,actor,PreparedChunksKind,receipt.Identity.OwnerMiniAppId);var prepared=ReadPrepared(row);if(prepared.OperationId!=operation||prepared.Public.UploadId!=request.UploadId||prepared.Public.EntityId!=request.EntityId)throw new UnauthorizedAccessException();return new(request.RequestId,null,prepared.Public);}
        // Новый процесс может найти физический orphan незафиксированного seal, только у собственного op-derived ID.
        _files.ApiStaging.CancelPrepared(request.SetId);return new(request.RequestId,_files.ApiStaging.BeginPrepared(request.SetId,request.EntityId,request.UploadId,actor),null);
    }
    public async Task<ApiPreparedChunkSet> SealPreparedChunksAsync(SealApiPreparedChunksRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);var lease=_files.ApiStaging.Prepared(request.LeaseId);
        if(lease.UserId!=actor.UserId||lease.ClientId!=actor.ApiClientId||lease.OperationId!=actor.ApiOperationId)throw new UnauthorizedAccessException();
        var index=new List<ApiPreparedChunkIndex>();var ids=new HashSet<Guid>();
        await using(var stream=new FileStream(lease.Path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true))
        await foreach(var row in ParsePreparedChunksAsync(stream,lease.EntityId,ct))
        {if(!ids.Add(row.Id)||index.Count>=100000)throw new ArgumentException("Повторный ID/слишком много чанков.");index.Add(new(row.Id,index.Count,row.Checksum!));}
        string hash;await using(var stream=File.OpenRead(lease.Path))hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));
        using var admitted=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);var (_,upload)=await RequireUploadAsync(db,lease.UploadId,actor,ct);var operation=await db.TechnicalStates.FindAsync([lease.OperationId],ct)??throw new KeyNotFoundException();VerifyReceiptOwner(operation,actor);var receipt=DeserializeReceipt(operation);
            if(receipt.Identity.WriteMode!=ApiWriteMode.Deferred||receipt.Identity.OwnerMiniAppId!=_persistenceRules.UploadPurposes[upload.Public.Purpose].OwnerMiniAppId||upload.Public.State!="Ready"||upload.ConsumedBy.HasValue)throw new UnauthorizedAccessException();await AuthorizeUploadAsync(db,upload,actor,receipt.Identity.CapabilityId,ct);
            var view=new ApiPreparedChunkSet(lease.SetId,lease.EntityId,lease.UploadId,hash,index,new FileInfo(lease.Path).Length);var previous=await db.TechnicalStates.FindAsync([lease.SetId],ct);
            if(previous!=null){VerifyTechnicalOwner(previous,actor,PreparedChunksKind,receipt.Identity.OwnerMiniAppId);var existing=ReadPrepared(previous);if(existing.Public.Sha256!=hash)throw new PublicOperationException("job.prepared_conflict","Нормализованный input уже зафиксирован иначе.");return existing.Public;}
            var existingSets=await db.TechnicalStates.AsNoTracking().Where(x=>x.Kind==PreparedChunksKind&&x.AuthorUserId==actor.UserId&&x.ClientId==actor.ApiClientId&&x.ExpiresAt>DateTime.UtcNow).ToListAsync(ct);var active=existingSets.Select(ReadPrepared).Where(x=>x.ExpiresAt>DateTime.UtcNow&&x.ConsumedBy==null).ToArray();
            if(active.Length>=8||active.Sum(x=>x.Public.Length)+view.Length>2L*1024*1024*1024)throw new PublicOperationException("job.staging_quota_conflict","Превышена квота нормализованного staging.");
            _files.ApiStaging.PublishPrepared(request.LeaseId);var stored=new StoredPreparedChunks(view,lease.OperationId,upload.Public.ExpiresAt,null);
            db.TechnicalStates.Add(new(){Id=view.Id,OwnerMiniAppId=receipt.Identity.OwnerMiniAppId,Kind=PreparedChunksKind,AuthorUserId=actor.UserId,ClientId=actor.ApiClientId!,Json=JsonSerializer.Serialize(stored,StorageJsonOptions.Default),ModifiedAt=DateTime.UtcNow,ExpiresAt=receipt.ExpiresAt});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return view;
        }
        finally{_writer.Release();}
    }
    private async Task<StoredPreparedChunks> RequirePreparedChunksAsync(KmsBusinessEntityDbContext db,Guid id,Guid entity,BusRequestContext actor,CancellationToken ct)
    {
        RequireApiActor(actor);var row=await db.TechnicalStates.FindAsync([id],ct)??throw new KeyNotFoundException();var receipt=DeserializeReceipt(await db.TechnicalStates.FindAsync([actor.ApiOperationId],ct)??throw new UnauthorizedAccessException());VerifyTechnicalOwner(row,actor,PreparedChunksKind,receipt.Identity.OwnerMiniAppId);var stored=ReadPrepared(row);
        if(stored.OperationId!=actor.ApiOperationId||stored.Public.EntityId!=entity||stored.ExpiresAt<=DateTime.UtcNow||stored.ConsumedBy.HasValue&&stored.ConsumedBy!=actor.ApiOperationId)throw new UnauthorizedAccessException();
        row.Json=JsonSerializer.Serialize(stored with{ConsumedBy=actor.ApiOperationId},StorageJsonOptions.Default);row.Revision++;return stored;
    }
    // Читает один ограниченный chunk DTO; не строит дерево/строку размером с книгу.
    private static async IAsyncEnumerable<BusinessEntityDataChunkDto> ParsePreparedChunksAsync(Stream stream,Guid entity,[EnumeratorCancellation]CancellationToken ct)
    {
        using var reader=new StreamReader(stream,new UTF8Encoding(false,true),false,65536,true);var position=0;
        await foreach(var line in BoundedTextLines.ReadAsync(reader,1024*1024,ct))
        {
            var chunk=JsonSerializer.Deserialize<ApiStagedChunk>(line,StorageJsonOptions.Default)??throw new InvalidDataException();if(chunk.Blocks==null||chunk.Blocks.Count>256||chunk.Blocks.Sum(x=>x.Html?.Length??0)>65536)throw new ArgumentException("Нормализованный чанк превышает лимит.");
            yield return PrepareChunk(new BusinessEntityDataChunk{Id=chunk.Id,BusinessEntityId=entity,SortOrder=position++,Blocks=chunk.Blocks.ToList()});
        }
    }
    private static StoredPreparedChunks ReadPrepared(TechnicalStateRow row)=>JsonSerializer.Deserialize<StoredPreparedChunks>(row.Json,StorageJsonOptions.Default)??throw new InvalidDataException();
    private sealed record StoredPreparedChunks(ApiPreparedChunkSet Public,Guid OperationId,DateTime ExpiresAt,Guid? ConsumedBy);
}

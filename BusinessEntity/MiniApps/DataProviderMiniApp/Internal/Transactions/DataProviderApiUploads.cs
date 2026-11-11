using System.Text.Json;
using System.Text.RegularExpressions;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

internal sealed partial class DataProviderTransactionService
{
    private const string UploadKind="ApiUploadV1";
    internal const int UploadPartSize=4*1024*1024;
    public async Task<ApiUploadStatus> CreateUploadAsync(CreateApiUploadRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);var input=request.Input;
        if(!_persistenceRules.UploadPurposes.TryGetValue(input.Purpose,out var purpose))throw new ArgumentException("Неизвестное назначение загрузки.");
        if(input.Length<=0)throw new ArgumentException("Длина должна быть положительной.");if(input.Length>purpose.MaxBytes)throw new PublicOperationException("upload.too_large","Превышен предел загрузки.");
        if(string.IsNullOrWhiteSpace(input.FileName)||input.FileName.Length>240||input.FileName.IndexOfAny(['\\','/','\r','\n','\0'])>=0||input.FileName is "." or "..")throw new ArgumentException("Нужно простое имя файла.");
        if(input.Sha256!=null)ValidateUploadHash(input.Sha256);purpose.ValidateFormat(input.FileName,input.ContentType);
        using var admitted=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);
            var operation=await TechnicalReceiptAsync(db,actor,ApiWriteMode.Technical,ct);var receipt=DeserializeReceipt(operation);
            var id=StableOperationIdentity.Create(receipt.Identity.OperationId,"upload");
            var existing=await db.TechnicalStates.FindAsync([id],ct);if(existing!=null){VerifyTechnicalOwner(existing,actor,UploadKind,"DataProviderMiniApp");return ReadUpload(existing).Public;}
            var now=DateTime.UtcNow;var view=new ApiUploadStatus(id,input.Purpose,input.SpaceId,input.TargetId,input.Length,input.FileName,input.ContentType,UploadPartSize,"Uploading",null,[],now.AddDays(1));
            var stored=new StoredUpload(view,input.Sha256?.ToUpperInvariant(),null);
            await AuthorizeUploadAsync(db,stored,actor,"uploads.create",ct);
            var live=await db.TechnicalStates.AsNoTracking().Where(x=>x.Kind==UploadKind&&x.AuthorUserId==actor.UserId&&x.ClientId==actor.ApiClientId&&x.ExpiresAt>now).ToListAsync(ct);
            var active=live.Select(ReadUpload).Where(x=>x.ConsumedBy==null&&x.Public.ExpiresAt>now&&x.Public.State!="Cancelled").ToArray();
            if(active.Length>=4||active.Sum(x=>x.Public.Length)+input.Length>2L*1024*1024*1024)throw new PublicOperationException("upload.quota_conflict","Превышена квота незавершённых загрузок.");
            db.TechnicalStates.Add(new(){Id=id,OwnerMiniAppId="DataProviderMiniApp",Kind=UploadKind,AuthorUserId=actor.UserId,ClientId=actor.ApiClientId!,Json=JsonSerializer.Serialize(stored,StorageJsonOptions.Default),ModifiedAt=now,ExpiresAt=receipt.ExpiresAt});
            RegisterUploadReceipt(operation,receipt,view);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return view;
        }
        finally{_writer.Release();}
    }
    public async Task<ApiUploadStatus> ReadUploadAsync(Guid id,BusRequestContext actor,CancellationToken ct=default)
    {
        RequireApiActor(actor);await using var db=new KmsBusinessEntityDbContext(_options);var (_,upload)=await RequireUploadAsync(db,id,actor,ct);
        await AuthorizeUploadAsync(db,upload,actor,"uploads.read",ct);return upload.Public;
    }
    public async Task<Guid> OpenUploadPartAsync(OpenApiUploadPartRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);ValidateUploadHash(request.Sha256);
        await using var db=new KmsBusinessEntityDbContext(_options);var (_,upload)=await RequireUploadAsync(db,request.UploadId,actor,ct);
        await AuthorizeUploadAsync(db,upload,actor,"uploads.part.put",ct);RequireWritableUpload(upload);
        var count=(upload.Public.Length+UploadPartSize-1)/UploadPartSize;
        if(request.PartNumber<0||request.PartNumber>=count||request.Length!=Math.Min(UploadPartSize,upload.Public.Length-(long)request.PartNumber*UploadPartSize))throw new ArgumentException("Неправильные номер/размер части.");
        var old=upload.Public.Parts.FirstOrDefault(x=>x.Number==request.PartNumber);if(old!=null&&!string.Equals(old.Sha256,request.Sha256,StringComparison.OrdinalIgnoreCase))throw new PublicOperationException("upload.part_conflict","Эта часть уже содержит другие bytes.");
        return _files.ApiStaging.BeginPart(request.UploadId,request.PartNumber,request.Length,request.Sha256.ToUpperInvariant(),actor);
    }
    public async Task<ApiUploadStatus> CompleteUploadPartAsync(CompleteApiUploadPartRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;var leaseId=request.LeaseId;RequireApiActor(actor);using var admitted=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            var lease=_files.ApiStaging.Part(leaseId);if(lease.UploadId!=request.UploadId||lease.Number!=request.PartNumber||lease.Length!=request.Length||!string.Equals(lease.Hash,request.Sha256,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException();await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);var operation=await TechnicalReceiptAsync(db,actor,ApiWriteMode.Technical,ct);var receipt=DeserializeReceipt(operation);var (row,upload)=await RequireUploadAsync(db,lease.UploadId,actor,ct);
            await AuthorizeUploadAsync(db,upload,actor,"uploads.part.put",ct);RequireWritableUpload(upload);
            var accepted=_files.ApiStaging.CommitPart(leaseId,actor);var old=upload.Public.Parts.FirstOrDefault(x=>x.Number==accepted.Number);
            if(old!=null&&old.Sha256!=accepted.Hash)throw new PublicOperationException("upload.part_conflict","Другая часть уже принята.");
            var view=upload.Public with {Parts=upload.Public.Parts.Where(x=>x.Number!=accepted.Number).Append(new(accepted.Number,accepted.Length,accepted.Hash)).OrderBy(x=>x.Number).ToArray()};
            SaveUpload(row,upload with {Public=view});RegisterUploadReceipt(operation,receipt,view);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return view;
        }
        finally{_writer.Release();}
    }
    // IO выполняется вне writer; перед Ready повторно проверяются epoch/grant/ACL/состояние.
    public async Task<ApiUploadStatus> CompleteUploadAsync(CompleteApiUploadRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);StoredUpload upload;
        await using(var readDb=new KmsBusinessEntityDbContext(_options))
        {
            var (_,value)=await RequireUploadAsync(readDb,request.UploadId,actor,ct);upload=value;
            await AuthorizeUploadAsync(readDb,upload,actor,"uploads.complete",ct);
            if(upload.Public.State is not ("Uploading" or "Ready"))throw new PublicOperationException("upload.state_conflict","Загрузка недоступна.");
            if(upload.Public.Parts.Count!=(upload.Public.Length+UploadPartSize-1)/UploadPartSize)throw new PublicOperationException("upload.parts_conflict","Загружены не все части.");
        }
        var hash=upload.Public.State=="Ready"?upload.Public.Sha256!:await _files.ApiStaging.AssembleAsync(request.UploadId,upload.Public.Parts.Count,upload.Public.Length,upload.ExpectedHash,ct);
        using var admitted=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);var operation=await TechnicalReceiptAsync(db,actor,ApiWriteMode.Technical,ct);var receipt=DeserializeReceipt(operation);
            var (row,current)=await RequireUploadAsync(db,request.UploadId,actor,ct);await AuthorizeUploadAsync(db,current,actor,"uploads.complete",ct);
            if(current.Public.State is not ("Uploading" or "Ready"))throw new PublicOperationException("upload.state_conflict","Загрузка изменена.");
            var view=current.Public with {State="Ready",Sha256=hash};SaveUpload(row,current with {Public=view});RegisterUploadReceipt(operation,receipt,view);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return view;
        }
        finally{_writer.Release();}
    }
    public async Task<ApiUploadStatus> CancelUploadAsync(CancelApiUploadRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);using var admitted=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);var operation=await TechnicalReceiptAsync(db,actor,ApiWriteMode.Technical,ct);var receipt=DeserializeReceipt(operation);
            var (row,upload)=await RequireUploadAsync(db,request.UploadId,actor,ct);await _committedGraph.AuthorizeApiAsync(receipt.Identity,new(receipt.Identity.OperationId,receipt.Identity.RequestHash,null,upload.Public.SpaceId,[new("Self",actor.UserId)]),actor,ct);if(upload.ConsumedBy!=null||upload.ReservedBy!=null)throw new PublicOperationException("upload.state_conflict","Загрузка уже использована сохранением.");
            var view=upload.Public with {State="Cancelled"};SaveUpload(row,upload with {Public=view});RegisterUploadReceipt(operation,receipt,view);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            try{_files.ApiStaging.Cancel(request.UploadId);}catch(IOException){}catch(UnauthorizedAccessException){}return view;
        }
        finally{_writer.Release();}
    }
    public async Task<ReadApiUploadLeaseResponse> ReadUploadLeaseAsync(ReadApiUploadLeaseRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);await using var db=new KmsBusinessEntityDbContext(_options);var (_,upload)=await RequireUploadAsync(db,request.UploadId,actor,ct);
        if(upload.Public.Purpose!=request.Purpose||upload.Public.State!="Ready"||upload.ConsumedBy.HasValue||upload.ReservedBy.HasValue&&upload.ReservedBy!=actor.ApiOperationId)throw new PublicOperationException("upload.state_conflict","Нужна готовая неиспользованная загрузка правильного назначения.");
        var receipt=DeserializeReceipt(await db.TechnicalStates.FindAsync([actor.ApiOperationId],ct)??throw new UnauthorizedAccessException());var purpose=_persistenceRules.UploadPurposes[request.Purpose];
        if(receipt.Identity.OwnerMiniAppId!=purpose.OwnerMiniAppId)throw new UnauthorizedAccessException();
        await AuthorizeUploadAsync(db,upload,actor,receipt.Identity.CapabilityId,ct);return new(request.RequestId,_files.ApiStaging.BeginRead(upload.Public.Id),upload.Public);
    }
    private async Task ValidateApiUploadFilesAsync(KmsBusinessEntityDbContext db,PreparedBatch batch,BusRequestContext actor,CancellationToken ct)
    {
        foreach(var file in batch.Files.Where(x=>x.Change.UploadId.HasValue))
        {
            var (row,upload)=await RequireUploadAsync(db,file.Change.UploadId!.Value,actor,ct);var purpose=_persistenceRules.UploadPurposes[upload.Public.Purpose];
            if(upload.Public.State!="Ready"||upload.ConsumedBy.HasValue&&upload.ConsumedBy!=actor.ApiOperationId||upload.ReservedBy.HasValue&&upload.ReservedBy!=actor.ApiOperationId||purpose.OwnerMiniAppId!=DeserializeReceipt(await db.TechnicalStates.FindAsync([actor.ApiOperationId],ct)??throw new UnauthorizedAccessException()).Identity.OwnerMiniAppId||(!purpose.CreatesEntity&&upload.Public.TargetId!=file.Change.EntityId)||batch.ApiOperation!.SpaceId!=upload.Public.SpaceId||file.Hash!=upload.Public.Sha256||file.Length!=upload.Public.Length)throw new PublicOperationException("upload.state_conflict","Загрузка не соответствует сохранению.");
            await AuthorizeUploadAsync(db,upload,actor,purpose.RequiredCapability,ct);SaveUpload(row,upload with {ConsumedBy=actor.ApiOperationId,ReservedBy=null});
        }
        var consumed=batch.ApiOperation!.ConsumeUploads??[];
        foreach(var payload in batch.Payloads.Where(x=>x.PreparedChunkSetId.HasValue))
        {var row=await db.TechnicalStates.FindAsync([payload.PreparedChunkSetId],ct)??throw new KeyNotFoundException();var prepared=ReadPrepared(row);if(!consumed.Contains(prepared.Public.UploadId))throw new ArgumentException("Sealed input должен потребляться тем же canonical commit.");}
        foreach(var uploadId in consumed.Distinct())
        {
            var(row,upload)=await RequireUploadAsync(db,uploadId,actor,ct);var purpose=_persistenceRules.UploadPurposes[upload.Public.Purpose];var receipt=DeserializeReceipt(await db.TechnicalStates.FindAsync([actor.ApiOperationId],ct)??throw new UnauthorizedAccessException());
            if(upload.Public.State!="Ready"||upload.ConsumedBy.HasValue&&upload.ConsumedBy!=actor.ApiOperationId||upload.ReservedBy.HasValue&&upload.ReservedBy!=actor.ApiOperationId||purpose.OwnerMiniAppId!=receipt.Identity.OwnerMiniAppId||batch.ApiOperation.SpaceId!=upload.Public.SpaceId)throw new UnauthorizedAccessException();
            await AuthorizeUploadAsync(db,upload,actor,purpose.RequiredCapability,ct);SaveUpload(row,upload with{ConsumedBy=actor.ApiOperationId,ReservedBy=null});
        }

    }
    private async Task AuthorizeUploadAsync(KmsBusinessEntityDbContext db,StoredUpload upload,BusRequestContext actor,string capability,CancellationToken ct)
    {
        var purpose=_persistenceRules.UploadPurposes[upload.Public.Purpose];var target=await db.BusinessEntities.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==upload.Public.TargetId,ct)??throw new KeyNotFoundException();
        var check=purpose.AuthorizeTarget(target.EntityType,target.Id,upload.Public.SpaceId);
        foreach(var cap in new[]{capability,purpose.RequiredCapability}.Distinct())await _committedGraph.AuthorizeApiAsync(new(Guid.Empty,"DataProviderMiniApp",cap,1,"",ApiWriteMode.Technical),new(Guid.Empty,"",target.Id,upload.Public.SpaceId,[check]),actor,ct);
    }
    private static async Task<(TechnicalStateRow,StoredUpload)> RequireUploadAsync(KmsBusinessEntityDbContext db,Guid id,BusRequestContext actor,CancellationToken ct)
    {var row=await db.TechnicalStates.FindAsync([id],ct)??throw new KeyNotFoundException();VerifyTechnicalOwner(row,actor,UploadKind,"DataProviderMiniApp");var value=ReadUpload(row);if(value.Public.ExpiresAt<=DateTime.UtcNow)throw new PublicOperationException("upload.expired_conflict","Загрузка истекла.");return(row,value);}
    private static void RequireWritableUpload(StoredUpload upload){if(upload.Public.State!="Uploading"||upload.ConsumedBy.HasValue)throw new PublicOperationException("upload.state_conflict","Загрузка недоступна для записи частей.");}
    private static void ValidateUploadHash(string value){if(!Regex.IsMatch(value,"^[A-Fa-f0-9]{64}$",RegexOptions.CultureInvariant))throw new ArgumentException("Нужен SHA-256 hex.");}
    private static StoredUpload ReadUpload(TechnicalStateRow row)=>JsonSerializer.Deserialize<StoredUpload>(row.Json,StorageJsonOptions.Default)??throw new InvalidDataException();
    private static void SaveUpload(TechnicalStateRow row,StoredUpload upload){row.Json=JsonSerializer.Serialize(upload,StorageJsonOptions.Default);row.Revision++;row.ModifiedAt=DateTime.UtcNow;}
    private static void RegisterUploadReceipt(TechnicalStateRow row,ApiOperationReceipt receipt,ApiUploadStatus upload){row.Json=JsonSerializer.Serialize(receipt with {State="Registered",SpaceId=upload.SpaceId,ReferenceId=upload.Id},StorageJsonOptions.Default);row.Revision++;row.ModifiedAt=DateTime.UtcNow;}
    private sealed record StoredUpload(ApiUploadStatus Public,string? ExpectedHash,Guid? ConsumedBy,Guid? ReservedBy=null);
}

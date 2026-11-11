using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

internal sealed partial class DataProviderTransactionService
{
    private const string JobKind="ApiJobV1";
    public async Task<ApiJobAccepted> CreateJobAsync(CreateApiJobRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);VerifyJobAuthorization(request.Authorization,actor);
        if(request.InputJson.Length>32768||request.JobId==Guid.Empty||request.SpaceId==Guid.Empty)throw new ArgumentException("Неверный job input.");
        using(var json=JsonDocument.Parse(request.InputJson))if(json.RootElement.ValueKind!=JsonValueKind.Object)throw new ArgumentException();
        if(!_persistenceRules.Jobs.TryGetValue(request.Owner+":"+request.Kind,out var definition))throw new ArgumentException("Незарегистрированный job kind.");
        using var accepted=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);var operation=await TechnicalReceiptAsync(db,actor,ApiWriteMode.Deferred,ct);var receipt=DeserializeReceipt(operation);
            if(receipt.Identity.OwnerMiniAppId!=request.Owner||receipt.Identity.CapabilityId!=definition.CapabilityId)throw new UnauthorizedAccessException();
            var previous=await db.TechnicalStates.FindAsync([request.JobId],ct);if(previous!=null){VerifyTechnicalOwner(previous,actor,JobKind,request.Owner);var stored=ReadJob(previous);if(stored.Operation!=receipt.Identity)throw new UnauthorizedAccessException();return AcceptedJob(stored);}
            await _committedGraph.AuthorizeApiAsync(receipt.Identity,new(receipt.Identity.OperationId,receipt.Identity.RequestHash,null,request.SpaceId,request.Checks??throw new ArgumentException("Нужны предметные права job.")),actor,ct);
            await ReserveJobUploadsAsync(db,request.UploadIds??[],receipt.Identity,actor,request.SpaceId,ct);
            var count=await db.TechnicalStates.CountAsync(x=>x.Kind==JobKind&&x.AuthorUserId==actor.UserId&&x.ClientId==actor.ApiClientId&&x.ExpiresAt>DateTime.UtcNow&&(x.Json.Contains("\"state\":\"Queued\"")||x.Json.Contains("\"state\":\"Running\"")||x.Json.Contains("\"state\":\"NeedsReauthorization\"")),ct);if(count>=100)throw new PublicOperationException("job.quota_conflict","Слишком много работ в сроке хранения.");
            var now=DateTime.UtcNow;var job=new StoredApiJob(new(request.JobId,request.Owner,request.Kind,"Queued",receipt.Identity.OperationId,request.SpaceId,0,0,null,null,now,now),request.InputJson,receipt.Identity,request.Authorization,request.Authorization,UploadIds:request.UploadIds);
            db.TechnicalStates.Add(new(){Id=request.JobId,OwnerMiniAppId=request.Owner,Kind=JobKind,AuthorUserId=actor.UserId,ClientId=actor.ApiClientId!,Json=JsonSerializer.Serialize(job,StorageJsonOptions.Default),ModifiedAt=now,ExpiresAt=receipt.ExpiresAt});
            RegisterJobReceipt(operation,receipt,job.Public.Id,job.Public.SpaceId,"Queued");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return AcceptedJob(job);
        }
        finally{_writer.Release();}
    }
    public async Task<StoredApiJob?> ReadAuthorizedJobAsync(ReadApiJobRequest request,CancellationToken ct=default)
    {var job=await ReadJobAsync(request.JobId,request.Context,ct);if(job!=null&&!request.Context.IsSystem&&request.RequiredCapability!=null)await AuthorizeJobMetadataAsync(job,request.Context,request.RequiredCapability,ct);return job;}
    // System читает служебную запись для reconciliation/bootstrap, API — только свою subject/client.
    public async Task<StoredApiJob?> ReadJobAsync(Guid id,BusRequestContext actor,CancellationToken ct=default)
    {
        await using var db=new KmsBusinessEntityDbContext(_options);var row=await db.TechnicalStates.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.Kind==JobKind,ct);if(row==null)return null;
        if(!actor.IsSystem){RequireApiActor(actor);VerifyTechnicalOwner(row,actor,JobKind,null);}return ReadJob(row);
    }
    // PG single-host lease и один worker предотвращают два процесса; RuntimeId отличает restart от живого Running.
    public async Task<StoredApiJob?> ClaimJobAsync(ClaimApiJobRequest request,CancellationToken ct=default)
    {
        if(!request.Context.IsSystem||request.RuntimeId==Guid.Empty)throw new UnauthorizedAccessException();await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);var now=DateTime.UtcNow;
            Guid? candidate=null;
            await foreach(var row in db.TechnicalStates.Where(x=>x.Kind==JobKind&&x.ExpiresAt>now&&(x.Json.Contains("\"state\":\"Queued\"")||x.Json.Contains("\"state\":\"Running\""))).OrderBy(x=>x.ModifiedAt).AsAsyncEnumerable().WithCancellation(ct))
            {
                var job=ReadJob(row);if(!request.RegisteredKinds.Contains(job.Public.Owner+":"+job.Public.Kind)||job.Public.State!="Queued"&&(job.Public.State!="Running"||job.RuntimeId==request.RuntimeId&&job.Public.UpdatedAt>now.AddMinutes(-1)))continue;
                // Не держим открытый reader, когда ниже сохраняется смена состояния.
                candidate=row.Id;break;
            }
            return candidate is {} id?await ClaimOneJobAsync(id,request.RuntimeId,ct):null;
        }
        finally{_writer.Release();}
    }
    private async Task<StoredApiJob> ClaimOneJobAsync(Guid id,Guid runtime,CancellationToken ct)
    {
        await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);var row=await db.TechnicalStates.FindAsync([id],ct)??throw new KeyNotFoundException();var job=ReadJob(row);
        var receiptRow=await db.TechnicalStates.FindAsync([job.Operation.OperationId],ct)??throw new KeyNotFoundException();var receipt=DeserializeReceipt(receiptRow);
        if(receipt.State=="Committed"){job=job with{Public=job.Public with{State="Succeeded",Result=receipt.Result,UpdatedAt=DateTime.UtcNow}};}
        else if(job.CancelRequested){job=job with{Public=job.Public with{State="Cancelled",UpdatedAt=DateTime.UtcNow}};RegisterJobReceipt(receiptRow,receipt,id,job.Public.SpaceId,"Cancelled");}
        else job=job with{RuntimeId=runtime,Public=job.Public with{State="Running",ErrorCode=null,UpdatedAt=DateTime.UtcNow}};
        if(job.Public.State=="Cancelled")await ReleaseJobUploadsAsync(db,job,ct);SaveJob(row,job);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return job;
    }
    public async Task<ApiJobStatus> UpdateJobAsync(UpdateApiJobRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;if(!actor.IsSystem)RequireApiActor(actor);if(request.State is not ("Running" or "Failed" or "Cancelled" or "NeedsReauthorization")||request.ProcessedBytes<0||request.Chunks<0||request.ErrorCode?.Length>128)throw new ArgumentException();
        await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);var row=await db.TechnicalStates.FindAsync([request.JobId],ct)??throw new KeyNotFoundException();if(row.Kind!=JobKind)throw new KeyNotFoundException();var job=ReadJob(row);
            if(!actor.IsSystem){VerifyTechnicalOwner(row,actor,JobKind,null);if(actor.ApiOperationId!=job.Operation.OperationId)throw new UnauthorizedAccessException();}
            var operation=await db.TechnicalStates.FindAsync([job.Operation.OperationId],ct)??throw new KeyNotFoundException();var receipt=DeserializeReceipt(operation);
            if(receipt.State=="Committed")job=job with{Public=job.Public with{State="Succeeded",Result=receipt.Result,ErrorCode=null}};
            else if(job.Public.State is not ("Succeeded" or "Cancelled" or "Failed")){var state=job.CancelRequested?"Cancelled":request.State;job=job with{Public=job.Public with{State=state,ProcessedBytes=Math.Max(job.Public.ProcessedBytes,request.ProcessedBytes),Chunks=Math.Max(job.Public.Chunks,request.Chunks),ErrorCode=request.ErrorCode}};RegisterJobReceipt(operation,receipt,job.Public.Id,job.Public.SpaceId,state);}
            if(job.Public.State is "Failed" or "Cancelled")await ReleaseJobUploadsAsync(db,job,ct);
            job=job with{Public=job.Public with{UpdatedAt=DateTime.UtcNow}};SaveJob(row,job);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return job.Public;
        }
        finally{_writer.Release();}
    }
    public async Task<ApiJobStatus> CancelJobAsync(CancelApiJobRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);using var accepted=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);var current=await TechnicalReceiptAsync(db,actor,ApiWriteMode.Technical,ct);var currentReceipt=DeserializeReceipt(current);
            var row=await db.TechnicalStates.FindAsync([request.JobId],ct)??throw new KeyNotFoundException();VerifyTechnicalOwner(row,actor,JobKind,null);var job=ReadJob(row);await AuthorizeJobMetadataAsync(job,actor,"jobs.cancel",ct);
            var original=await db.TechnicalStates.FindAsync([job.Operation.OperationId],ct)??throw new KeyNotFoundException();var receipt=DeserializeReceipt(original);
            if(receipt.State=="Committed")job=job with{Public=job.Public with{State="Succeeded",Result=receipt.Result}};
            else if(job.Public.State is "Queued" or "NeedsReauthorization"){job=job with{CancelRequested=true,Public=job.Public with{State="Cancelled"}};RegisterJobReceipt(original,receipt,job.Public.Id,job.Public.SpaceId,"Cancelled");}
            else if(job.Public.State=="Running")job=job with{CancelRequested=true};
            if(job.Public.State=="Cancelled")await ReleaseJobUploadsAsync(db,job,ct);
            job=job with{Public=job.Public with{UpdatedAt=DateTime.UtcNow}};SaveJob(row,job);RegisterJobReceipt(current,currentReceipt,job.Public.Id,job.Public.SpaceId,"Registered");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return job.Public;
        }
        finally{_writer.Release();}
    }
    public async Task<ApiJobStatus> ReauthorizeJobAsync(ReauthorizeApiJobRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);VerifyJobAuthorization(request.Authorization,actor);using var accepted=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);var current=await TechnicalReceiptAsync(db,actor,ApiWriteMode.Technical,ct);var currentReceipt=DeserializeReceipt(current);
            var row=await db.TechnicalStates.FindAsync([request.JobId],ct)??throw new KeyNotFoundException();VerifyTechnicalOwner(row,actor,JobKind,null);var job=ReadJob(row);await AuthorizeJobMetadataAsync(job,actor,"jobs.reauthorize",ct);await AuthorizeJobMetadataAsync(job,actor,job.Operation.CapabilityId,ct);
            var original=await db.TechnicalStates.FindAsync([job.Operation.OperationId],ct)??throw new KeyNotFoundException();var receipt=DeserializeReceipt(original);
            if(receipt.State=="Committed")job=job with{Public=job.Public with{State="Succeeded",Result=receipt.Result}};
            else if(job.Public.State!="NeedsReauthorization")throw new PublicOperationException("job.state_conflict","Повторное разрешение доступно при NeedsReauthorization.");
            else{job=job with{Authorization=request.Authorization,RuntimeId=null,Public=job.Public with{State="Queued",ErrorCode=null,UpdatedAt=DateTime.UtcNow}};RegisterJobReceipt(original,receipt,job.Public.Id,job.Public.SpaceId,"Queued");}
            SaveJob(row,job);RegisterJobReceipt(current,currentReceipt,job.Public.Id,job.Public.SpaceId,"Registered");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return job.Public;
        }
        finally{_writer.Release();}
    }
    private async Task ReserveJobUploadsAsync(KmsBusinessEntityDbContext db,IReadOnlyList<Guid> ids,ApiOperationIdentity operation,BusRequestContext actor,Guid space,CancellationToken ct)
    {
        if(ids.Count is <1 or >4||ids.Distinct().Count()!=ids.Count)throw new ArgumentException("Нужны уникальные входы работы.");
        foreach(var id in ids){var(row,upload)=await RequireUploadAsync(db,id,actor,ct);var purpose=_persistenceRules.UploadPurposes[upload.Public.Purpose];if(upload.Public.State!="Ready"||upload.ConsumedBy.HasValue||upload.ReservedBy.HasValue&&upload.ReservedBy!=operation.OperationId||upload.Public.SpaceId!=space||purpose.OwnerMiniAppId!=operation.OwnerMiniAppId||purpose.RequiredCapability!=operation.CapabilityId)throw new PublicOperationException("upload.state_conflict","Вход недоступен для этой работы.");SaveUpload(row,upload with{ReservedBy=operation.OperationId});}
    }
    private static async Task ReleaseJobUploadsAsync(KmsBusinessEntityDbContext db,StoredApiJob job,CancellationToken ct)
    {foreach(var id in job.UploadIds??[]){var row=await db.TechnicalStates.FindAsync([id],ct);if(row?.Kind!=UploadKind)continue;var upload=ReadUpload(row);if(upload.ReservedBy==job.Operation.OperationId&&upload.ConsumedBy==null)SaveUpload(row,upload with{ReservedBy=null});}}
    private async Task AuthorizeJobMetadataAsync(StoredApiJob job,BusRequestContext actor,string cap,CancellationToken ct)=>await _committedGraph.AuthorizeApiAsync(job.Operation with{CapabilityId=cap},new(job.Operation.OperationId,job.Operation.RequestHash,null,job.Public.SpaceId,[new("Self",actor.UserId)]),actor,ct);
    // Canonical acceptance не наследует System; cancellation/expiry/grant остаются проверяемыми.
    private static async Task ValidateDeferredJobAsync(KmsBusinessEntityDbContext db,TechnicalStateRow? receiptRow,BusRequestContext actor,CancellationToken ct)
    {
        if(receiptRow==null)return;var receipt=DeserializeReceipt(receiptRow);if(receipt.Identity.WriteMode!=ApiWriteMode.Deferred)return;
        var row=await db.TechnicalStates.FindAsync([receipt.ReferenceId],ct)??throw new KeyNotFoundException();VerifyTechnicalOwner(row,actor,JobKind,receipt.Identity.OwnerMiniAppId);var job=ReadJob(row);
        if(job.Public.State!="Running"||job.CancelRequested)throw new PublicOperationException("job.state_conflict","Работа остановлена до commit.");
        VerifyJobAuthorization(job.Authorization,actor);
    }
    private static async Task CompleteDeferredJobAsync(KmsBusinessEntityDbContext db,ApiOperationReceipt receipt,ApiMutationOutcome outcome,CancellationToken ct)
    {
        if(receipt.Identity.WriteMode!=ApiWriteMode.Deferred)return;var row=await db.TechnicalStates.FindAsync([receipt.ReferenceId],ct)??throw new KeyNotFoundException();var job=ReadJob(row);SaveJob(row,job with{Public=job.Public with{State="Succeeded",Result=outcome,ErrorCode=null,UpdatedAt=DateTime.UtcNow}});
    }
    private static void VerifyJobAuthorization(ApiJobAuthorization auth,BusRequestContext actor)
    {if(auth.Session.UserId!=actor.UserId||auth.Session.ClientId!=actor.ApiClientId||auth.Session.Id!=actor.ApiSessionId||auth.Session.Epoch!=actor.AuthEpoch||auth.AccessExpiresAt!=actor.ApiAccessExpiresAt||auth.AccessExpiresAt<=DateTime.UtcNow||auth.Session.ExpiresAt<=DateTime.UtcNow||string.IsNullOrEmpty(auth.Issuer)||string.IsNullOrEmpty(auth.Subject))throw new UnauthorizedAccessException();}
    private static StoredApiJob ReadJob(TechnicalStateRow row)=>JsonSerializer.Deserialize<StoredApiJob>(row.Json,StorageJsonOptions.Default)??throw new InvalidDataException();
    private static void SaveJob(TechnicalStateRow row,StoredApiJob job){row.Json=JsonSerializer.Serialize(job,StorageJsonOptions.Default);row.ModifiedAt=DateTime.UtcNow;row.Revision++;}
    private static ApiJobAccepted AcceptedJob(StoredApiJob job)=>new(job.Public.Id,job.Operation.OperationId,"/api/v1/jobs/"+job.Public.Id);
    private static void RegisterJobReceipt(TechnicalStateRow row,ApiOperationReceipt receipt,Guid id,Guid space,string state){row.Json=JsonSerializer.Serialize(receipt with{State=state,ReferenceId=id,SpaceId=space},StorageJsonOptions.Default);row.ModifiedAt=DateTime.UtcNow;row.Revision++;}
}

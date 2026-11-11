using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Chunks;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity=BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

public sealed class ApiJobTests
{
    [PostgresFact]
    public async Task DeletedTargetCannotLeaveQueuedJobOrReservedUploadAlive()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var(job,actor,target,upload)=await CreateAsync(fixture);await fixture.Writer.SaveAsync(new(Guid.NewGuid(),"RichTextDocumentMiniApp",null){DeleteEntities=[target]},SystemActor());
        var status=(await fixture.Writer.ReadJobAsync(job.JobId,actor))!.Public;Assert.Equal("Failed",status.State);Assert.Equal("record.not_found",status.ErrorCode);Assert.Null(await fixture.Writer.ClaimJobAsync(new(Guid.NewGuid(),Guid.NewGuid(),["RichTextDocumentMiniApp:rich.import.v1"]){Context=SystemActor()}));var cancel=await OperationAsync(fixture,actor,"DataProviderMiniApp","uploads.cancel",ApiWriteMode.Technical);Assert.Equal("Cancelled",(await fixture.Writer.CancelUploadAsync(new(Guid.NewGuid(),upload.Id){Context=cancel})).State);
    }
    [PostgresFact]
    public async Task DeferredReceiptAndSealedChunksCommitAtomicallyAndRecoverLostReply()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var(state,actor,target,upload)=await CreateAsync(fixture);Assert.Equal("Queued",(await fixture.Writer.ReadJobAsync(state.JobId,actor))!.Public.State);Assert.Equal(state.JobId,(await fixture.Writer.ReadApiOperationAsync(state.OperationId,actor))!.ReferenceId);
        var system=SystemActor();var runtime=Guid.NewGuid();var claimed=await fixture.Writer.ClaimJobAsync(new(Guid.NewGuid(),runtime,["RichTextDocumentMiniApp:rich.import.v1"]){Context=system});Assert.Equal("Running",claimed!.Public.State);
        var next=await fixture.CreateWriter().ClaimJobAsync(new(Guid.NewGuid(),Guid.NewGuid(),["RichTextDocumentMiniApp:rich.import.v1"]){Context=system});Assert.Equal(state.JobId,next!.Public.Id);
        var setId=StableOperationIdentity.Create(actor.ApiOperationId!.Value,"prepared-chunks");var opened=await fixture.Writer.OpenPreparedChunksAsync(new(Guid.NewGuid(),setId,target,upload.Id){Context=actor});
        var chunkId=Guid.NewGuid();await using(var output=fixture.Files.ApiStaging.OpenPreparedWrite(opened.WriteLeaseId!.Value)){await JsonSerializer.SerializeAsync(output,new ApiStagedChunk(chunkId,[new(){Kind="paragraph",Html="streamed book"}]),new JsonSerializerOptions(JsonSerializerDefaults.Web));await output.WriteAsync(new byte[]{10});}
        var sealedSet=await fixture.Writer.SealPreparedChunksAsync(new(Guid.NewGuid(),opened.WriteLeaseId.Value){Context=actor});Assert.Single(sealedSet.Index);Assert.NotNull((await fixture.Writer.OpenPreparedChunksAsync(new(Guid.NewGuid(),setId,target,upload.Id){Context=actor})).Existing);
        var batch=new EntitySaveBatch(state.OperationId,"RichTextDocumentMiniApp",null){Payloads=[new(target,1,new RichTextDocument{Name="Book"},ChunkOrder:[chunkId],PreparedChunkSetId:setId)],ApiOperation=new(state.OperationId,actor.ApiRequestHash!,target,target,[new("EditDraft",target,target)],ConsumeUploads:[upload.Id])};
        fixture.Faults.ThrowAt=StorageFaultPoint.AfterBusinessChanges;await Assert.ThrowsAsync<IOException>(()=>fixture.Writer.SaveAsync(batch,actor));fixture.Faults.ThrowAt=null;Assert.Equal("Running",(await fixture.Writer.ReadJobAsync(state.JobId,actor))!.Public.State);
        await using(var sql=new KmsBusinessEntityDbContext(fixture.Options)){Assert.Equal(1,await sql.BusinessEntityDataItems.CountAsync());Assert.False(await sql.BusinessEntityDataChunks.AnyAsync());}
        fixture.Faults.ThrowAt=StorageFaultPoint.AfterCommit;await Assert.ThrowsAsync<IOException>(()=>fixture.Writer.SaveAsync(batch,actor));fixture.Faults.ThrowAt=null;
        var status=(await fixture.CreateWriter().ReadJobAsync(state.JobId,actor))!;Assert.Equal("Succeeded",status.Public.State);Assert.Equal(2,status.Public.Result!.Version);await fixture.Writer.SaveAsync(batch,actor);
        await using(var sql=new KmsBusinessEntityDbContext(fixture.Options)){Assert.Equal(2,await sql.BusinessEntityDataItems.CountAsync());Assert.Equal(1,await sql.BusinessEntityDataChunks.CountAsync());Assert.Equal(1,await sql.ChunkReferences.CountAsync());}
        var cancel=await OperationAsync(fixture,actor,"DataProviderMiniApp","jobs.cancel",ApiWriteMode.Technical);Assert.Equal("Succeeded",(await fixture.Writer.CancelJobAsync(new(Guid.NewGuid(),state.JobId){Context=cancel})).State);
    }
    [PostgresFact]
    public async Task CancelReleasesReservationAndForeignIdentityCannotReadOrReauthorize()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var(job,actor,_,upload)=await CreateAsync(fixture);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.Writer.ReadJobAsync(job.JobId,actor with{ApiClientId="other"}));
        var cancelUpload=await OperationAsync(fixture,actor,"DataProviderMiniApp","uploads.cancel",ApiWriteMode.Technical);await Assert.ThrowsAsync<PublicOperationException>(()=>fixture.Writer.CancelUploadAsync(new(Guid.NewGuid(),upload.Id){Context=cancelUpload}));
        var cancel=await OperationAsync(fixture,actor,"DataProviderMiniApp","jobs.cancel",ApiWriteMode.Technical);Assert.Equal("Cancelled",(await fixture.Writer.CancelJobAsync(new(Guid.NewGuid(),job.JobId){Context=cancel})).State);
        Assert.Null(await fixture.Writer.ClaimJobAsync(new(Guid.NewGuid(),Guid.NewGuid(),["RichTextDocumentMiniApp:rich.import.v1"]){Context=SystemActor()}));
        Assert.Equal("Cancelled",(await fixture.Writer.CancelUploadAsync(new(Guid.NewGuid(),upload.Id){Context=cancelUpload})).State);
    }
    [PostgresFact]
    public async Task NewAuthorizationKeepsOperationInputAndOriginalUserClient()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var(job,actor,_,_)=await CreateAsync(fixture);await fixture.Writer.ClaimJobAsync(new(Guid.NewGuid(),Guid.NewGuid(),["RichTextDocumentMiniApp:rich.import.v1"]){Context=SystemActor()});
        await fixture.Writer.UpdateJobAsync(new(Guid.NewGuid(),job.JobId,"NeedsReauthorization",ErrorCode:"access.denied"){Context=SystemActor()});var original=(await fixture.Writer.ReadJobAsync(job.JobId,actor))!;
        var fresh=actor with{ApiSessionId=Guid.NewGuid(),AuthEpoch=actor.AuthEpoch+1,ApiAccessExpiresAt=DateTime.UtcNow.AddMinutes(10)};var reauth=await OperationAsync(fixture,fresh,"DataProviderMiniApp","jobs.reauthorize",ApiWriteMode.Technical);var auth=Authorization(reauth);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.Writer.ReauthorizeJobAsync(new(Guid.NewGuid(),job.JobId,auth with{Session=auth.Session with{UserId=Guid.NewGuid()}}){Context=reauth}));
        Assert.Equal("Queued",(await fixture.Writer.ReauthorizeJobAsync(new(Guid.NewGuid(),job.JobId,auth){Context=reauth})).State);var updated=(await fixture.CreateWriter().ReadJobAsync(job.JobId,reauth))!;Assert.Equal(original.Operation,updated.Operation);Assert.Equal(original.InputJson,updated.InputJson);Assert.Equal(JsonSerializer.Serialize(original.SubmittedAuthorization),JsonSerializer.Serialize(updated.SubmittedAuthorization));Assert.Equal(fresh.ApiSessionId,updated.Authorization.Session.Id);
    }
    private static async Task<(ApiJobAccepted,BusRequestContext,Guid,ApiUploadStatus)> CreateAsync(PostgresTestDatabase fixture)
    {
        var target=Guid.NewGuid();await fixture.Writer.SaveAsync(new(Guid.NewGuid(),"RichTextDocumentMiniApp",null){Entities=[new Entity{Id=target,Name="Base",EntityType=RichTextDocumentType.Id}],Payloads=[new(target,0,new RichTextDocument{Name="Base"})]},SystemActor());var actor=new BusRequestContext(Guid.NewGuid(),Guid.NewGuid(),AuthEpoch:8,ExecutionKind:BusExecutionKind.Api,ApiSessionId:Guid.NewGuid(),ApiClientId:"job-fixture",ApiAccessExpiresAt:DateTime.UtcNow.AddMinutes(10));
        var uploadActor=await OperationAsync(fixture,actor,"DataProviderMiniApp","uploads.create",ApiWriteMode.Technical);var bytes=Encoding.UTF8.GetBytes("fixture book");var hash=Convert.ToHexString(SHA256.HashData(bytes));var upload=await fixture.Writer.CreateUploadAsync(new(Guid.NewGuid(),new("rich.book",target,target,bytes.Length,"book.txt","text/plain",hash)){Context=uploadActor});
        var lease=await fixture.Writer.OpenUploadPartAsync(new(Guid.NewGuid(),upload.Id,0,bytes.Length,hash){Context=actor});await fixture.Files.ApiStaging.CopyPartAsync(lease,new MemoryStream(bytes),default);var part=await OperationAsync(fixture,actor,"DataProviderMiniApp","uploads.part.put",ApiWriteMode.Technical);await fixture.Writer.CompleteUploadPartAsync(new(Guid.NewGuid(),upload.Id,0,bytes.Length,hash){Context=part,LeaseId=lease});var completion=await OperationAsync(fixture,actor,"DataProviderMiniApp","uploads.complete",ApiWriteMode.Technical);upload=await fixture.Writer.CompleteUploadAsync(new(Guid.NewGuid(),upload.Id){Context=completion});
        actor=await OperationAsync(fixture,actor,"RichTextDocumentMiniApp","rich-documents.import",ApiWriteMode.Deferred);var job=await fixture.Writer.CreateJobAsync(new(Guid.NewGuid(),Guid.NewGuid(),"RichTextDocumentMiniApp","rich.import.v1","{\"knownInput\":true}",target,Authorization(actor),[new("EditDraft",target,target)],[upload.Id]){Context=actor});return(job,actor,target,upload);
    }
    private static ApiJobAuthorization Authorization(BusRequestContext actor)=>new(new(actor.ApiSessionId!.Value,actor.UserId!.Value,actor.ApiClientId!,Guid.NewGuid(),actor.AuthEpoch,DateTime.UtcNow.AddMinutes(20),"Active",["rich-documents.import"],new(true)),"https://fixture.invalid/","subject",actor.ApiAccessExpiresAt!.Value);
    private static BusRequestContext SystemActor()=>new(Guid.NewGuid(),null,IsSystem:true);
    private static async Task<BusRequestContext> OperationAsync(PostgresTestDatabase fixture,BusRequestContext source,string owner,string cap,ApiWriteMode mode)
    {var identity=new ApiOperationIdentity(Guid.NewGuid(),owner,cap,1,new string('E',64),mode);var actor=source with{ApiOperationId=identity.OperationId,ApiRequestHash=identity.RequestHash};await fixture.Writer.BeginApiOperationAsync(identity,actor);return actor;}
}

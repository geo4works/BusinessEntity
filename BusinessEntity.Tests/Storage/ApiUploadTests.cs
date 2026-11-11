using System.Security.Cryptography;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity=BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

public sealed class ApiUploadTests
{
    [PostgresFact]
    public async Task DeletingTargetCancelsUnusedUploadAndOwnCancellationStillWorksWithoutTarget()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var target=await SeedAsync(fixture);var actor=Actor();var creation=await OperationAsync(fixture,actor,"uploads.create");var upload=await fixture.Writer.CreateUploadAsync(new(Guid.NewGuid(),new("rich.image",target,target,8,"a.png","image/png")){Context=creation});
        await fixture.Writer.SaveAsync(new(Guid.NewGuid(),"RichTextDocumentMiniApp",null){DeleteEntities=[target]},new(Guid.NewGuid(),null,IsSystem:true));
        var cancel=await OperationAsync(fixture,actor,"uploads.cancel");Assert.Equal("Cancelled",(await fixture.Writer.CancelUploadAsync(new(Guid.NewGuid(),upload.Id){Context=cancel})).State);
        await fixture.Writer.CleanupUploadsAsync(default);Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.Files.ApiStaging.ReadyPath(upload.Id))));
    }
    [PostgresFact]
    public async Task ExplicitAssociationRemovalStillProtectsOtherIncomingDependencies()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var space=Guid.NewGuid();var video=Guid.NewGuid();var other=Guid.NewGuid();var own=Guid.NewGuid();var foreign=Guid.NewGuid();
        await fixture.Writer.SaveAsync(new(Guid.NewGuid(),"MediaServerMiniApp",null){Entities=[new Entity{Id=space,Name="Space",EntityType=global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id},new Entity{Id=video,Name="Video",EntityType=global::BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.MediaVideoType.Id},new Entity{Id=other,Name="Folder",EntityType=global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id}],Payloads=[new(video,0,new global::BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.Entities.MediaVideo{Name="Video",DisplayName="Video",FileName="v.mp4",ContentType="video/mp4",OriginalSizeBytes=1})],Relations=[new(){Id=own,ObjectAId=space,ObjectBId=video,RelationType="RelatesTo",RelationParams="own-association"},new(){Id=foreign,ObjectAId=other,ObjectBId=video,RelationType="RelatesTo"}]},new(Guid.NewGuid(),Guid.NewGuid(),IsSystem:true));
        var identity=new ApiOperationIdentity(Guid.NewGuid(),"MediaServerMiniApp","media-videos.delete",1,new string('D',64));var actor=Actor() with{ApiOperationId=identity.OperationId,ApiRequestHash=identity.RequestHash};await fixture.Writer.BeginApiOperationAsync(identity,actor);
        var batch=new EntitySaveBatch(identity.OperationId,identity.OwnerMiniAppId,null){DeleteEntities=[video],DeleteRelations=[own],ApiOperation=new(identity.OperationId,identity.RequestHash,video,space,[new("Delete",space,space)])};
        await Assert.ThrowsAsync<PublicOperationException>(()=>fixture.Writer.SaveAsync(batch,actor));
        await fixture.Writer.SaveAsync(new(Guid.NewGuid(),"MediaServerMiniApp",null){DeleteRelations=[foreign]},new(Guid.NewGuid(),Guid.NewGuid(),IsSystem:true));await fixture.Writer.SaveAsync(batch,actor);
        await using var sql=new KmsBusinessEntityDbContext(fixture.Options);Assert.False(await sql.BusinessEntities.AnyAsync(x=>x.Id==video));Assert.False(await sql.BusinessEntityRelations.AnyAsync());Assert.True(await sql.BusinessEntities.AnyAsync(x=>x.Id==space));
    }
    // Parts переживают новый writer; повтор/hash/ownership проверяются независимо от HTTP.
    [PostgresFact]
    public async Task PartsResumeAndWholeDigestSurviveRestartWithoutCreatingVersions()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var target=await SeedAsync(fixture);var payload=new byte[4*1024*1024+71];RandomNumberGenerator.Fill(payload);var actor=Actor();
        var creation=await OperationAsync(fixture,actor,"uploads.create");var upload=await fixture.Writer.CreateUploadAsync(new(Guid.NewGuid(),new("rich.image",target,target,payload.Length,"a.png","image/png",Hash(payload))){Context=creation});
        await PutAsync(fixture,actor,upload,0,payload.AsMemory(0,upload.PartSize));
        var restarted=fixture.CreateWriter();var progress=await restarted.ReadUploadAsync(upload.Id,actor);Assert.Single(progress.Parts);Assert.Equal("Uploading",progress.State);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>restarted.ReadUploadAsync(upload.Id,actor with{UserId=Guid.NewGuid()}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>restarted.ReadUploadAsync(upload.Id,actor with{ApiClientId="other"}));
        await Assert.ThrowsAsync<PublicOperationException>(()=>restarted.OpenUploadPartAsync(new(Guid.NewGuid(),upload.Id,0,upload.PartSize,new string('0',64)){Context=actor}));
        await PutAsync(fixture,actor,upload,1,payload.AsMemory(upload.PartSize));var completed=await OperationAsync(fixture,actor,"uploads.complete");
        var result=await restarted.CompleteUploadAsync(new(Guid.NewGuid(),upload.Id){Context=completed});Assert.Equal("Ready",result.State);Assert.Equal(Hash(payload),result.Sha256);
        using(var stream=File.OpenRead(fixture.Files.ApiStaging.ReadyPath(upload.Id)))Assert.Equal(Hash(payload),Convert.ToHexString(await SHA256.HashDataAsync(stream)));
        await using var sql=new KmsBusinessEntityDbContext(fixture.Options);Assert.Equal(1,await sql.BusinessEntityDataItems.CountAsync());Assert.Empty(await sql.EntityFiles.ToListAsync());
    }
    [PostgresFact]
    public async Task BadPartCannotAdvanceProgressAndCancelledUploadCannotResume()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var target=await SeedAsync(fixture);var actor=Actor();var creation=await OperationAsync(fixture,actor,"uploads.create");
        var upload=await fixture.Writer.CreateUploadAsync(new(Guid.NewGuid(),new("rich.image",target,target,20,"a.png","image/png")){Context=creation});
        var lease=await fixture.Writer.OpenUploadPartAsync(new(Guid.NewGuid(),upload.Id,0,20,Hash(new byte[20])){Context=actor});
        await Assert.ThrowsAsync<PublicOperationException>(()=>fixture.Files.ApiStaging.CopyPartAsync(lease,new MemoryStream(Enumerable.Repeat((byte)1,20).ToArray()),default));
        Assert.Empty((await fixture.Writer.ReadUploadAsync(upload.Id,actor)).Parts);
        var cancelled=await OperationAsync(fixture,actor,"uploads.cancel");Assert.Equal("Cancelled",(await fixture.Writer.CancelUploadAsync(new(Guid.NewGuid(),upload.Id){Context=cancelled})).State);
        await Assert.ThrowsAsync<PublicOperationException>(()=>fixture.Writer.OpenUploadPartAsync(new(Guid.NewGuid(),upload.Id,0,20,Hash(new byte[20])){Context=actor}));
        Assert.False(File.Exists(fixture.Files.ApiStaging.ReadyPath(upload.Id)));
    }
    [PostgresFact]
    public async Task MissingPartsAndWholeDigestMismatchCannotBecomeReady()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var target=await SeedAsync(fixture);var actor=Actor();var creation=await OperationAsync(fixture,actor,"uploads.create");
        var upload=await fixture.Writer.CreateUploadAsync(new(Guid.NewGuid(),new("rich.image",target,target,20,"a.png","image/png",new string('0',64))){Context=creation});var complete=await OperationAsync(fixture,actor,"uploads.complete");
        await Assert.ThrowsAsync<PublicOperationException>(()=>fixture.Writer.CompleteUploadAsync(new(Guid.NewGuid(),upload.Id){Context=complete}));await PutAsync(fixture,actor,upload,0,new byte[20]);
        await Assert.ThrowsAsync<PublicOperationException>(()=>fixture.Writer.CompleteUploadAsync(new(Guid.NewGuid(),upload.Id){Context=complete}));Assert.Equal("Uploading",(await fixture.Writer.ReadUploadAsync(upload.Id,actor)).State);
    }
    // Consumption marker и canonical файл должны rollback вместе при сбое SQL.
    [PostgresFact]
    public async Task FileConsumptionRollsBackAndRetriedCommitConsumesExactlyOnce()
    {
        await using var fixture=await PostgresTestDatabase.CreateAsync();var target=await SeedAsync(fixture);var actor=Actor();var bytes=new byte[]{1,2,3};var creation=await OperationAsync(fixture,actor,"uploads.create");
        var upload=await fixture.Writer.CreateUploadAsync(new(Guid.NewGuid(),new("rich.image",target,target,3,"a.png","image/png")){Context=creation});await PutAsync(fixture,actor,upload,0,bytes);
        var complete=await OperationAsync(fixture,actor,"uploads.complete");await fixture.Writer.CompleteUploadAsync(new(Guid.NewGuid(),upload.Id){Context=complete});
        var identity=new ApiOperationIdentity(Guid.NewGuid(),"RichTextDocumentMiniApp","rich-documents.images.attach",1,new string('B',64));var save=actor with{ApiOperationId=identity.OperationId,ApiRequestHash=identity.RequestHash};await fixture.Writer.BeginApiOperationAsync(identity,save);
        var batch=new EntitySaveBatch(identity.OperationId,identity.OwnerMiniAppId,null){Files=[new(target,Guid.NewGuid().ToString("N"),"original","a.png","image/png",[],UploadId:upload.Id)],ApiOperation=new(identity.OperationId,identity.RequestHash,target,target,[new("EditDraft",target,target)])};
        fixture.Faults.ThrowAt=BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions.StorageFaultPoint.AfterBusinessChanges;
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Writer.SaveAsync(batch,save));fixture.Faults.ThrowAt=null;
        var read=new ReadApiUploadLeaseRequest(Guid.NewGuid(),upload.Id,"rich.image"){Context=save};Assert.NotNull((await fixture.Writer.ReadUploadLeaseAsync(read)).LeaseId);
        await fixture.Writer.SaveAsync(batch,save);await fixture.Writer.SaveAsync(batch,save);await Assert.ThrowsAsync<PublicOperationException>(()=>fixture.Writer.ReadUploadLeaseAsync(read));
        await using var sql=new KmsBusinessEntityDbContext(fixture.Options);Assert.Equal(1,await sql.EntityFiles.CountAsync());Assert.Equal("Committed",(await fixture.Writer.ReadApiOperationAsync(identity.OperationId,save))!.State);
    }
    private static async Task PutAsync(PostgresTestDatabase fixture,BusRequestContext actor,ApiUploadStatus upload,int number,ReadOnlyMemory<byte> bytes)
    {
        var hash=Hash(bytes.Span);var lease=await fixture.Writer.OpenUploadPartAsync(new(Guid.NewGuid(),upload.Id,number,bytes.Length,hash){Context=actor});
        await fixture.Files.ApiStaging.CopyPartAsync(lease,new MemoryStream(bytes.ToArray()),default);var commit=await OperationAsync(fixture,actor,"uploads.part.put");await fixture.Writer.CompleteUploadPartAsync(new(Guid.NewGuid(),upload.Id,number,bytes.Length,hash){Context=commit,LeaseId=lease});
    }
    private static string Hash(ReadOnlySpan<byte> data)=>Convert.ToHexString(SHA256.HashData(data));
    private static BusRequestContext Actor()=>new(Guid.NewGuid(),Guid.NewGuid(),ExecutionKind:BusExecutionKind.Api,ApiSessionId:Guid.NewGuid(),ApiClientId:"upload-fixture");
    private static async Task<BusRequestContext> OperationAsync(PostgresTestDatabase fixture,BusRequestContext actor,string cap)
    {var identity=new ApiOperationIdentity(Guid.NewGuid(),"DataProviderMiniApp",cap,1,new string('A',64),ApiWriteMode.Technical);var context=actor with{ApiOperationId=identity.OperationId,ApiRequestHash=identity.RequestHash};await fixture.Writer.BeginApiOperationAsync(identity,context);return context;}
    private static async Task<Guid> SeedAsync(PostgresTestDatabase fixture)
    {var id=Guid.NewGuid();await fixture.Writer.SaveAsync(new(Guid.NewGuid(),"RichTextDocumentMiniApp",null){Entities=[new Entity{Id=id,Name="Upload fixture",EntityType=RichTextDocumentType.Id}],Payloads=[new(id,0,new RichTextDocument{Name="Upload fixture"})]},new(Guid.NewGuid(),Guid.NewGuid(),IsSystem:true));return id;}
}

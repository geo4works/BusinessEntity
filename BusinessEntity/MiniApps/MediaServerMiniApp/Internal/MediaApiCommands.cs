using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

internal sealed class MediaApiCommands(MediaServerService media,IDataProviderConnector data,ApiDomainScope scope,IUserConnector users,ApiWriteContext write,BusRequestClient bus)
{
    internal async Task<EntitySaveBatch> PrepareCreateAsync(Guid space,ApiMediaCreate input,CancellationToken ct)
    {
        await scope.EntityAsync(space,SpaceType.Id,ct);await RequireAdminAsync(space,ct);var session=await scope.CurrentAsync(ct);
        var ready=await bus.SendAsync<ReadApiUploadLeaseRequest,ReadApiUploadLeaseResponse>(new(Guid.NewGuid(),input.ReadyUploadId,"media.video"),ct);var upload=ready.Upload??throw new InvalidDataException();if(upload.TargetId!=space||upload.SpaceId!=space)throw new ArgumentException("Загрузка другого пространства.");
        var id=write.Id("video");var batch=media.PrepareUploadedBatch(write.OperationId,id,write.Id("space-relation"),space,upload.FileName,upload.ContentType,upload.Length,session.UserId,uploadId:upload.Id);
        var payload=(MediaVideo)batch.Payloads.Single().Data;payload.DisplayName=input.DisplayName??payload.DisplayName;payload.Comment=input.Comment;MediaServerService.NormalizeMetadata(payload);batch.Entities.Single().Name=payload.DisplayName;
        return write.Attach(batch,id,space,[new("AdminItems",space,space)]);
    }
    public async Task CreateAsync(Guid space,ApiMediaCreate input,CancellationToken ct)
    {var batch=await PrepareCreateAsync(space,input,ct);await bus.SendAsync<BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages.SaveEntityBatchRequest,BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages.SaveEntityBatchResponse>(new(Guid.NewGuid(),batch),ct,TimeSpan.FromHours(2));}
    public async Task UpdateAsync(Guid id,ApiMediaUpdate input,CancellationToken ct)
    {
        var(_,space)=await scope.EntityAsync(id,MediaVideoType.Id,ct);await RequireAdminAsync(space,ct);var payload=await media.GetEditableVideoDataAsync(id,ct);payload.DisplayName=input.DisplayName;payload.Comment=input.Comment;MediaServerService.NormalizeMetadata(payload);
        await data.SaveAsync(write.Attach(new(write.OperationId,"MediaServerMiniApp",null){Payloads=[new(id,payload.Version,payload)]},id,space,[new("AdminItems",space,space)]),ct);
    }
    public async Task DeleteAsync(Guid id,CancellationToken ct)
    {var(_,space)=await scope.EntityAsync(id,MediaVideoType.Id,ct);await RequireAdminAsync(space,ct);var associations=(await data.GetAllRelationsAsync(ct)).Where(r=>r.ObjectAId==space&&r.ObjectBId==id&&r.RelationType=="RelatesTo"&&r.RelationParams==MediaSpaceRelation.Parameters).Select(r=>r.Id).ToArray();await data.SaveAsync(write.Attach(new(write.OperationId,"MediaServerMiniApp",null){DeleteEntities=[id],DeleteRelations=associations},id,space,[new("Delete",space,space)]),ct);}
    private async Task RequireAdminAsync(Guid space,CancellationToken ct){var p=await users.GetCurrentUserPermissionsForSpaceAsync(space,ct);if(!p.CanAdminItems&&!p.CanAdminSpace&&!p.CanGlobalAdmin)throw new UnauthorizedAccessException();}
}

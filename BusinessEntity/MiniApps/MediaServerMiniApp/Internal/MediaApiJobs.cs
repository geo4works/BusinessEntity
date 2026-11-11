using System.Text.Json;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

internal sealed record MediaApiJobInput(Guid SpaceId,ApiMediaCreate Input);
internal sealed class MediaApiJobs(MediaApiCommands commands,BusRequestClient bus,ApiWriteContext write)
{
    public async Task<ApiJobAccepted> SubmitAsync(Guid space,ApiMediaCreate input,CancellationToken ct)
    {
        var id=write.Id("job");var previous=(await bus.SendAsync<ReadApiJobRequest,ReadApiJobResponse>(new(Guid.NewGuid(),id),ct)).Result;if(previous!=null)return new(id,previous.Operation.OperationId,"/api/v1/jobs/"+id);
        var batch=await commands.PrepareCreateAsync(space,input,ct);var auth=(await bus.SendAsync<CaptureApiJobAuthorizationRequest,CaptureApiJobAuthorizationResponse>(new(Guid.NewGuid(),"media-videos.create"),ct)).Result??throw new UnauthorizedAccessException();
        var result=await bus.SendAsync<CreateApiJobRequest,CreateApiJobResponse>(new(Guid.NewGuid(),id,"MediaServerMiniApp","media.original.v1",JsonSerializer.Serialize(new MediaApiJobInput(space,input)),space,auth,batch.ApiOperation!.PermissionChecks,[input.ReadyUploadId]),ct);return result.Result??throw new InvalidDataException();
    }
    public async Task RunAsync(Guid id,CancellationToken ct)
    {
        var job=(await bus.SendAsync<ReadApiJobRequest,ReadApiJobResponse>(new(Guid.NewGuid(),id),ct)).Result??throw new KeyNotFoundException();
        if(job.Public.Owner!="MediaServerMiniApp"||job.Public.Kind!="media.original.v1"||job.Operation.OperationId!=write.OperationId||job.Public.State!="Running"||job.CancelRequested)throw new UnauthorizedAccessException();
        var input=JsonSerializer.Deserialize<MediaApiJobInput>(job.InputJson)??throw new InvalidDataException();if(input.SpaceId!=job.Public.SpaceId)throw new InvalidDataException();await commands.CreateAsync(input.SpaceId,input.Input,ct);
    }
}

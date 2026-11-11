using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

internal sealed class MediaApiHandler(MediaApiReader reader,MediaApiCommands commands,MediaApiJobs jobs,IMessageBus bus,BusRequestScope scope,ILogger<MediaApiHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<ListApiMediaRequest,ListApiMediaResponse>(async r=>new(r.RequestId,await reader.ListAsync(r,default)),(id,e)=>new(id,null,e));
        Subscribe<ReadApiMediaRequest,ReadApiMediaResponse>(async r=>new(r.RequestId,await reader.ReadAsync(r.VideoId,default)),(id,e)=>new(id,null,e));
        Subscribe<DownloadApiMediaRequest,DownloadApiMediaResponse>(async r=>new(r.RequestId,await reader.DownloadAsync(r.VideoId,default)),(id,e)=>new(id,null,e));
        Subscribe<CreateApiMediaRequest,BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions.CreateApiJobResponse>(async r=>new(r.RequestId,await jobs.SubmitAsync(r.SpaceId,r.Input,default)),(id,e)=>new(id,null,e));
        Subscribe<RunApiMediaJobRequest,ApiMediaWriteResponse>(async r=>{await jobs.RunAsync(r.JobId,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<UpdateApiMediaRequest,ApiMediaWriteResponse>(async r=>{await commands.UpdateAsync(r.VideoId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<DeleteApiMediaRequest,ApiMediaWriteResponse>(async r=>{await commands.DeleteAsync(r.VideoId,default);return new(r.RequestId);},(id,e)=>new(id,e));
    }
}

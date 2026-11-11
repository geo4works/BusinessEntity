using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

internal sealed class RichApiReadHandler(RichApiReader reader,IMessageBus bus,BusRequestScope scope,ILogger<RichApiReadHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<ReadApiRichRequest,ReadApiRichResponse>(async r=>new(r.RequestId,await reader.ReadAsync(r,default)),(id,e)=>new(id,null,e));
        Subscribe<ReadApiRichContentRequest,ReadApiRichContentResponse>(async r=>new(r.RequestId,await reader.ContentAsync(r,default)),(id,e)=>new(id,null,e));
        Subscribe<ListApiRichVersionsRequest,ListApiRichVersionsResponse>(async r=>new(r.RequestId,await reader.VersionsAsync(r,default)),(id,e)=>new(id,null,e));
        Subscribe<ReadApiRichPublicationRequest,ReadApiRichPublicationResponse>(async r=>new(r.RequestId,await reader.PublicationAsync(r.EntityId,default)),(id,e)=>new(id,null,e));
    }
}
internal sealed class RichApiWriteHandler(RichApiCommands commands,RichApiHistoryCommands history,IMessageBus bus,BusRequestScope scope,ILogger<RichApiWriteHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<CreateApiRichRequest,ApiRichWriteResponse>(async r=>{await commands.CreateAsync(r.SpaceId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<SaveApiRichRevisionRequest,ApiRichWriteResponse>(async r=>{await commands.SaveAsync(r.EntityId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<UpdateApiRichMetadataRequest,ApiRichWriteResponse>(async r=>{await history.MetadataAsync(r.EntityId,r.Input.IsPublic,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<RestoreApiRichRequest,ApiRichWriteResponse>(async r=>{await history.RestoreAsync(r.EntityId,r.Input.Version,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<DescribeApiRichVersionRequest,ApiRichWriteResponse>(async r=>{await history.DescribeAsync(r.EntityId,r.Version,r.Input.Description,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<PublishApiRichRequest,ApiRichWriteResponse>(async r=>{await history.PublishAsync(r.EntityId,r.Input.Version,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<DeleteApiRichRequest,ApiRichWriteResponse>(async r=>{await history.DeleteAsync(r.EntityId,r.PlanId,default);return new(r.RequestId);},(id,e)=>new(id,e));
    }
}

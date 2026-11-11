using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Internal;

internal sealed class DocumentApiHandler(DocumentApiReader reader,DocumentApiCommands commands,IMessageBus bus,BusRequestScope scope,ILogger<DocumentApiHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<ReadApiDocumentRequest,ReadApiDocumentResponse>(async r=>new(r.RequestId,await reader.ReadAsync(r.DocumentId,r.Version,default)),(id,e)=>new(id,null,e));
        Subscribe<ListApiDocumentVersionsRequest,ListApiDocumentVersionsResponse>(async r=>new(r.RequestId,await reader.VersionsAsync(r,default)),(id,e)=>new(id,null,e));
        Subscribe<CreateApiDocumentRequest,ApiDocumentWriteResponse>(async r=>{await commands.CreateAsync(r.SpaceId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<UpdateApiDocumentRequest,ApiDocumentWriteResponse>(async r=>{await commands.UpdateAsync(r.DocumentId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<RestoreApiDocumentRequest,ApiDocumentWriteResponse>(async r=>{await commands.RestoreAsync(r.DocumentId,r.Input.Version,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<DeleteApiDocumentRequest,ApiDocumentWriteResponse>(async r=>{await commands.DeleteAsync(r.DocumentId,r.PlanId,default);return new(r.RequestId);},(id,e)=>new(id,e));
    }
}

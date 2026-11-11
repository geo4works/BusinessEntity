using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

internal sealed class RichApiImageHandler(RichApiImages images,IMessageBus bus,BusRequestScope scope,ILogger<RichApiImageHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<AttachApiRichImageRequest,ApiRichWriteResponse>(async r=>{await images.AttachAsync(r.DocumentId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<DownloadApiRichImageRequest,DownloadApiRichImageResponse>(async r=>new(r.RequestId,await images.DownloadAsync(r,default)),(id,e)=>new(id,null,e));
    }
}

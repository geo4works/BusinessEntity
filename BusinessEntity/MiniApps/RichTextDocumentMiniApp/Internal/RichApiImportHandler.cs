using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

internal sealed class RichApiImportHandler(RichApiImportSubmit submit,RichApiImportWorker worker,IMessageBus bus,BusRequestScope scope,ILogger<RichApiImportHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<SubmitApiRichImportRequest,CreateApiJobResponse>(async r=>new(r.RequestId,await submit.SubmitAsync(r.TargetId,r.Input,default)),(id,e)=>new(id,null,e));
        Subscribe<RunApiRichImportJobRequest,RunApiRichImportJobResponse>(async r=>{await worker.RunAsync(r.JobId,default);return new(r.RequestId);},(id,e)=>new(id,e));
    }
}

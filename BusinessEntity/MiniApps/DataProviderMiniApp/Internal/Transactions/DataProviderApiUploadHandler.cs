using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using ReactiveUI;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

internal sealed class DataProviderApiUploadHandler(DataProviderTransactionService writer,IMessageBus bus,ILogger<DataProviderApiUploadHandler> logger):BusEndpoint(bus,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<OpenApiFileLeaseRequest,OpenApiFileLeaseResponse>(async r=>new(r.RequestId,await writer.OpenFileLeaseAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<CreateApiUploadRequest,ApiUploadResponse>(async r=>new(r.RequestId,await writer.CreateUploadAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<ReadApiUploadRequest,ApiUploadResponse>(async r=>new(r.RequestId,await writer.ReadUploadAsync(r.UploadId,r.Context)),(id,e)=>new(id,null,e));
        Subscribe<CompleteApiUploadRequest,ApiUploadResponse>(async r=>new(r.RequestId,await writer.CompleteUploadAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<CancelApiUploadRequest,ApiUploadResponse>(async r=>new(r.RequestId,await writer.CancelUploadAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<OpenApiUploadPartRequest,OpenApiUploadPartResponse>(async r=>new(r.RequestId,await writer.OpenUploadPartAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<CompleteApiUploadPartRequest,ApiUploadResponse>(async r=>new(r.RequestId,await writer.CompleteUploadPartAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<ReadApiUploadLeaseRequest,ReadApiUploadLeaseResponse>(r=>writer.ReadUploadLeaseAsync(r),(id,e)=>new(id,null,null,e));
    }
}

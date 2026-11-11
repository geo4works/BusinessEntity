using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using ReactiveUI;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

internal sealed class DataProviderApiJobHandler(DataProviderTransactionService writer,IMessageBus bus,ILogger<DataProviderApiJobHandler> logger):BusEndpoint(bus,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<AbandonApiPreparedChunksRequest,AbandonApiPreparedChunksResponse>(r=>{writer.AbandonPrepared(r);return Task.FromResult(new AbandonApiPreparedChunksResponse(r.RequestId));},(id,e)=>new(id,e));
        Subscribe<OpenApiPreparedChunksRequest,OpenApiPreparedChunksResponse>(r=>writer.OpenPreparedChunksAsync(r),(id,e)=>new(id,null,null,e));
        Subscribe<SealApiPreparedChunksRequest,SealApiPreparedChunksResponse>(async r=>new(r.RequestId,await writer.SealPreparedChunksAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<CreateApiJobRequest,CreateApiJobResponse>(async r=>new(r.RequestId,await writer.CreateJobAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<ReadApiJobRequest,ReadApiJobResponse>(async r=>new(r.RequestId,await writer.ReadAuthorizedJobAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<ClaimApiJobRequest,ClaimApiJobResponse>(async r=>new(r.RequestId,await writer.ClaimJobAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<UpdateApiJobRequest,UpdateApiJobResponse>(async r=>new(r.RequestId,await writer.UpdateJobAsync(r)),(id,e)=>new(id,null,e));
        Subscribe<CancelApiJobRequest,UpdateApiJobResponse>(async r=>new(r.RequestId,await writer.CancelJobAsync(r)),(id,e)=>new(id,null,e));
    }
}

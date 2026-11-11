using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

internal sealed class DataProviderApiJobControlHandler(DataProviderTransactionService writer,BusRequestClient client,IMessageBus bus,BusRequestScope scope,ILogger<DataProviderApiJobControlHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()=>Subscribe<ReauthorizeApiJobControlRequest,UpdateApiJobResponse>(async r=>
    {
        var job=await writer.ReadJobAsync(r.JobId,r.Context)??throw new KeyNotFoundException();var proof=await client.SendAsync<CaptureApiJobAuthorizationRequest,CaptureApiJobAuthorizationResponse>(new(Guid.NewGuid(),job.Operation.CapabilityId));
        return new(r.RequestId,await writer.ReauthorizeJobAsync(new(Guid.NewGuid(),r.JobId,proof.Result??throw new UnauthorizedAccessException()){Context=r.Context}));
    },(id,e)=>new(id,null,e));
}

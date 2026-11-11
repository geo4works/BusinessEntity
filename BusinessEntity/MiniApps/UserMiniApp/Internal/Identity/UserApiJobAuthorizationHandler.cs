using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

internal sealed class UserApiJobAuthorizationHandler(UserApiJobAuthorization authorization,IMessageBus bus,BusRequestScope scope,ILogger<UserApiJobAuthorizationHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<CaptureApiJobAuthorizationRequest,CaptureApiJobAuthorizationResponse>(async r=>new(r.RequestId,await authorization.CaptureAsync(r.CapabilityId,default)),(id,e)=>new(id,null,e));
        Subscribe<RestoreApiJobAuthorizationRequest,RestoreApiJobAuthorizationResponse>(async r=>new(r.RequestId,await authorization.RestoreAsync(r.JobId,default)),(id,e)=>new(id,null,e));
    }
}

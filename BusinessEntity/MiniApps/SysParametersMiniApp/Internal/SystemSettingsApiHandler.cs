using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.SysParametersMiniApp.Internal;

internal sealed class SystemSettingsApiHandler(SystemSettingsApi settings,IMessageBus bus,BusRequestScope scope,ILogger<SystemSettingsApiHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<ReadSystemSettingsApiRequest,ReadSystemSettingsApiResponse>(async r=>new(r.RequestId,await settings.ReadAsync(default)),(id,e)=>new(id,null,e));
        Subscribe<SaveSystemSettingsApiRequest,SaveSystemSettingsApiResponse>(async r=>{await settings.SaveAsync(r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
    }
}

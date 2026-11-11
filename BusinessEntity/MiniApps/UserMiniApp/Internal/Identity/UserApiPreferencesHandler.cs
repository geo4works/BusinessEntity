using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

internal sealed class UserApiPreferencesHandler(UserApiPreferences preferences,IMessageBus bus,BusRequestScope scope,ILogger<UserApiPreferencesHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<ReadApiPrintingPreferencesRequest,ReadApiPrintingPreferencesResponse>(async r=>new(r.RequestId,await preferences.PrintingAsync(default)),(id,e)=>new(id,null,e));
        Subscribe<SaveApiPrintingPreferencesRequest,SaveApiPreferencesResponse>(async r=>{await preferences.SavePrintingAsync(r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<ReadApiDocumentPreferenceRequest,ReadApiDocumentPreferenceResponse>(async r=>new(r.RequestId,await preferences.DocumentAsync(r.DocumentId,default)),(id,e)=>new(id,null,e));
        Subscribe<SaveApiDocumentPreferenceRequest,SaveApiPreferencesResponse>(async r=>{await preferences.SaveDocumentAsync(r.DocumentId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
    }
}

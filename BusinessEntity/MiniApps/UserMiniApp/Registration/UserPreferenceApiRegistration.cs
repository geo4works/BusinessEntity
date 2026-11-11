using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Internal;

namespace BusinessEntity.MiniApps.UserMiniApp.Registration;

internal static class UserPreferenceApiRegistration
{
    public static void AddUserPreferenceApi(this IServiceCollection services)
    {
        services.AddScoped<UserApiPreferenceContext>();services.AddScoped<UserApiPreferences>();services.AddScoped<UserApiPreferencesHandler>();services.AddBusEndpoint<UserApiPreferencesHandler>(typeof(ReadApiPrintingPreferencesRequest),typeof(SaveApiPrintingPreferencesRequest),typeof(ReadApiDocumentPreferenceRequest),typeof(SaveApiDocumentPreferenceRequest));
        services.AddApiBinding<ApiEmptyInput,ReadApiPrintingPreferencesRequest,ReadApiPrintingPreferencesResponse,ApiPrintingPreferences>(Cap("settings.personal.printing.read","GET","/me/preferences/printing"),static(_,_)=>new(Guid.NewGuid()),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiPrintingPreferences,SaveApiPrintingPreferencesRequest,SaveApiPreferencesResponse,ApiMutationOutcome>(Cap("settings.personal.printing.update","PUT","/me/preferences/printing",true),static(i,_)=>new(Guid.NewGuid(),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiEmptyInput,ReadApiDocumentPreferenceRequest,ReadApiDocumentPreferenceResponse,ApiDocumentDisplayPreference>(Cap("settings.personal.display.read","GET","/me/preferences/rich-documents/{id:guid}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiDocumentDisplayPreference,SaveApiDocumentPreferenceRequest,SaveApiPreferencesResponse,ApiMutationOutcome>(Cap("settings.personal.display.update","PUT","/me/preferences/rich-documents/{id:guid}",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
    }
    private static ApiCapability Cap(string id,string method,string route,bool mutation=false)=>new("settings.personal",1,id,1,"UserMiniApp",id,method,route,id+".input.v1",id+".output.v1",mutation);
}

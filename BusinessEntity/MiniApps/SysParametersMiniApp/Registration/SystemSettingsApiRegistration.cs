using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts;
using BusinessEntity.MiniApps.SysParametersMiniApp.Internal;

namespace BusinessEntity.MiniApps.SysParametersMiniApp.Registration;

internal static class SystemSettingsApiRegistration
{
    public static void AddSystemSettingsApi(this IServiceCollection services)
    {
        services.AddScoped<SystemSettingsApi>();services.AddScoped<SystemSettingsApiHandler>();services.AddBusEndpoint<SystemSettingsApiHandler>(typeof(ReadSystemSettingsApiRequest),typeof(SaveSystemSettingsApiRequest));
        services.AddApiBinding<ApiEmptyInput,ReadSystemSettingsApiRequest,ReadSystemSettingsApiResponse,SystemSettingsValues>(Cap("settings.system.read","GET"),static(_,_)=>new(Guid.NewGuid()),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<SystemSettingsValues,SaveSystemSettingsApiRequest,SaveSystemSettingsApiResponse,ApiMutationOutcome>(Cap("settings.system.update","PUT",true),static(i,_)=>new(Guid.NewGuid(),i),static _=>throw new NotSupportedException());services.AddApiToolset("SysParametersMiniApp");
    }
    private static ApiCapability Cap(string id,string method,bool mutation=false)=>new("settings.system",1,id,1,"SysParametersMiniApp",id,method,"/settings/system",id+".input.v1",id+".output.v1",mutation);
}

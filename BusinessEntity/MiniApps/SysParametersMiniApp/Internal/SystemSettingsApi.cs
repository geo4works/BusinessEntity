using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Services;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;

namespace BusinessEntity.MiniApps.SysParametersMiniApp.Internal;

internal sealed class SystemSettingsApi(BusinessEntityHelper entities,IDataProviderConnector data,IUserConnector users,ApiDomainScope scope,ApiWriteContext write)
{
    private async Task RequireAsync(CancellationToken ct)
    {var session=await scope.CurrentAsync(ct);if(!session.Spaces.AllSpaces)throw new ApiFault(403,"api.space_denied","Системные настройки требуют допуска ко всем пространствам.");if((await users.GetCurrentUserAsync(ct))?.IsGlobalAdmin!=true)throw new UnauthorizedAccessException();}
    public async Task<SystemSettingsValues> ReadAsync(CancellationToken ct)
    {await RequireAsync(ct);var entity=await entities.GetRequiredSingletonEntityAsync<SysParameters>(SysParametersTpType.Id,"SysParameters",ct);return SystemSettingsRules.View(entity.Data);}
    public async Task SaveAsync(SystemSettingsValues input,CancellationToken ct)
    {
        await RequireAsync(ct);SystemSettingsRules.ValidateWire(input);var entity=await entities.GetRequiredSingletonEntityAsync<SysParameters>(SysParametersTpType.Id,"SysParameters",ct);SystemSettingsRules.Apply(entity.Data,input);
        await data.SaveAsync(write.Attach(new(write.OperationId,"SysParametersMiniApp",null){Payloads=[new(entity.Id,entity.Data.Version,entity.Data)]},entity.Id,checks:[new("GlobalAdmin",RequireAllSpaces:true)]),ct);
    }
}

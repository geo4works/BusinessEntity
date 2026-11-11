using BusinessEntity.Core.Classes;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using Entity=BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Infrastructure.Api;

// Общая проверка API пространства не выбирает browser context и не содержит правил конкретного типа.
public sealed class ApiDomainScope(IUserApiConnector sessions,IDataProviderConnector data,IUserConnector users,BusRequestScope scope)
{
    // Каждый предметный запрос читает актуальную security session.
    public Task<ApiSessionInfo> CurrentAsync(CancellationToken ct)=>scope.ApiSessionId is {} id?sessions.ValidateAsync(id,null,ct):throw new ApiFault(401,"api.session_required","Требуется API session.");
    // SpaceId явно задан клиентом и уже проверен владельцем как ресурс нужного типа.
    public async Task RequireSpaceAsync(Guid id,CancellationToken ct)
    {if(!(await CurrentAsync(ct)).Spaces.Allows(id))throw new ApiFault(403,"api.space_denied","Пространство не входит в API допуск.");}
    // Метаданные известных типов не заменяют отдельную content/structural policy владельца.
    public async Task<(Entity Entity,Guid SpaceId)> EntityAsync(Guid id,BusinessEntityTypeEnum? expectedType,CancellationToken ct)
    {
        var entity=await data.GetByIdAsync(id,ct)??throw new KeyNotFoundException();
        if(expectedType.HasValue&&entity.EntityType!=expectedType.Value)throw new KeyNotFoundException();
        var permissions=await users.GetCurrentUserPermissionsForEntityAsync(id,ct);await RequireSpaceAsync(permissions.SpaceId,ct);
        return(entity,permissions.SpaceId);
    }
}

using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

internal sealed class UserApiPreferenceContext(UserApiSessions sessions,UserAccessService access,IDataProviderConnector data,BusRequestScope scope)
{
    public async Task<Guid> UserAsync(CancellationToken ct)=>(await sessions.ValidateAsync(scope.ApiSessionId??throw new UnauthorizedAccessException(),null,ct)).UserId;
    public async Task<Guid> DocumentAsync(Guid id,CancellationToken ct)
    {
        var session=await sessions.ValidateAsync(scope.ApiSessionId??throw new UnauthorizedAccessException(),null,ct);var entity=await data.GetByIdAsync(id,ct)??throw new KeyNotFoundException();if(entity.EntityType!=RichTextDocumentType.Id)throw new KeyNotFoundException();var permissions=await access.GetCurrentUserPermissionsForEntityAsync(id,ct);if(!session.Spaces.Allows(permissions.SpaceId))throw new ApiFault(403,"api.space_denied","Пространство не входит в допуск.");if(!(await access.GetContentAccessForEntityIdAsync(id,ct)).CanRead)throw new UnauthorizedAccessException();return permissions.SpaceId;
    }
}

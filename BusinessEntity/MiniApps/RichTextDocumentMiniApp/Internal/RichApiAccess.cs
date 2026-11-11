using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using Entity=BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Выбор разрешённого снимка остаётся у владельца статьи, не в HTTP adapter.
internal sealed class RichApiAccess(ApiDomainScope scope,IUserConnector users,IDataProviderConnector data)
{
    public async Task<(Entity Entity,Guid Space,UserContentAccessDecision Access)> ContextAsync(Guid id,CancellationToken ct)
    {var(entity,space)=await scope.EntityAsync(id,RichTextDocumentType.Id,ct);return(entity,space,await users.GetEntityContentAccessAsync(id,ct));}
    public async Task<(Entity Entity,Guid Space,RichTextDocument Snapshot)> ReadAsync(Guid id,int? version,bool history,CancellationToken ct)
    {
        var(entity,space,access)=await ContextAsync(id,ct);if(history&&!access.CanViewDraft)throw new UnauthorizedAccessException();if(!access.CanRead)throw new UnauthorizedAccessException();
        if(version is <1)throw new ArgumentException("Version должен быть положительным.");
        if(!access.CanViewDraft){if(!access.CanViewPublished||entity.PublishedVersion<1||(version.HasValue&&version!=entity.PublishedVersion))throw new UnauthorizedAccessException();version=entity.PublishedVersion;}
        var snapshot=version.HasValue?await data.GetDataVersionAsync<RichTextDocument>(id,version.Value,ct):await data.GetDataAsync<RichTextDocument>(id,ct);return(entity,space,snapshot??throw new KeyNotFoundException());
    }
    public async Task<(Entity Parent,Guid Space)> ParentAsync(Guid spaceId,Guid? parentId,CancellationToken ct)
    {var(parent,space)=await scope.EntityAsync(parentId??spaceId,null,ct);if(space!=spaceId)throw new ApiFault(409,"api.context_conflict","Родитель находится в другом пространстве.");var permission=await users.GetCurrentUserPermissionsForEntityAsync(parent.Id,ct);if(!permission.CanAdminItems&&!permission.CanAdminSpace&&!permission.CanGlobalAdmin)throw new UnauthorizedAccessException();return(parent,space);}
    public async Task<Guid> DeleteAsync(Guid id,CancellationToken ct)
    {var(_,space,_)=await ContextAsync(id,ct);var permission=await users.GetCurrentUserPermissionsForEntityAsync(id,ct);if(!permission.CanAdminItems&&!permission.CanAdminSpace&&!permission.CanGlobalAdmin)throw new UnauthorizedAccessException();return space;}
}

using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using Entity=BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Internal;

// Чтение повторно использует existing tree visibility; права фильтруются до pagination.
internal sealed class SpaceApiReader(SpaceHelper spaces,ITreeConnector tree,ApiDomainScope scope,ApiPagination pagination,IUserConnector users)
{
    public async Task<ApiPage<ApiSpace>> ListAsync(ListApiSpacesRequest request,CancellationToken ct)
    {
        var session=await scope.CurrentAsync(ct);var visible=new List<ApiSpace>();
        foreach(var item in await spaces.GetSpacesAsync(ct))if(session.Spaces.Allows(item.Id)&&await tree.GetTreeForSpaceAsync(item.Id,ct)!=null)visible.Add(View(item));
        return pagination.Page(visible,x=>x.Id.ToString("N"),session,"spaces.list.v1",request.Cursor,request.Limit);
    }
    public async Task<ApiSpace> ReadAsync(Guid id,CancellationToken ct)
    {await scope.RequireSpaceAsync(id,ct);var item=await spaces.GetSpaceByIdAsync(id)??throw new KeyNotFoundException();if(await tree.GetTreeForSpaceAsync(id,ct)==null)throw new UnauthorizedAccessException();return View(item);}
    public async Task<ApiSpaceSettings> SettingsAsync(Guid id,CancellationToken ct)
    {await ReadAsync(id,ct);var permissions=await users.GetCurrentUserPermissionsForSpaceAsync(id,ct);if(!permissions.CanAdminSpace&&!permissions.CanGlobalAdmin)throw new UnauthorizedAccessException();var settings=await spaces.GetGenericSpacePropertiesAsync(id,ct);return new(settings.DoBackup,settings.BackupIntervalMinutes);}
    private static ApiSpace View(Entity entity)=>new(entity.Id,entity.Name,entity.IsPublic,entity.CreatedDate,entity.CreatedByUserId);
}

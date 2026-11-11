using BusinessEntity.Core.Types;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal;

// Это headless проекция той же visibility policy; пустые доступные папки нужны CRUD клиенту.
internal sealed class TreeApiReader(TreeMiniAppService tree,ApiDomainScope scope,ApiPagination pagination,EntityTypeRegistry types)
{
    public async Task<ApiPage<ApiTreeNode>> ChildrenAsync(ListApiTreeRequest request,CancellationToken ct)
    {
        await scope.RequireSpaceAsync(request.SpaceId,ct);var parent=request.ParentId??request.SpaceId;var (_,space)=await scope.EntityAsync(parent,null,ct);if(space!=request.SpaceId)throw new ApiFault(409,"api.context_conflict","Родитель находится в другом пространстве.");
        var snapshot=await tree.GetTreeForSpaceAsync(space,ct,true)??throw new UnauthorizedAccessException();var children=parent==space?snapshot.Children:Find(snapshot.Children,parent)?.Children??throw new UnauthorizedAccessException();
        return pagination.Page(children.Select(n=>View(n,space,parent)),x=>x.Id.ToString("N"),await scope.CurrentAsync(ct),"tree.children.v1:"+space+":"+parent,request.Cursor,request.Limit);
    }
    public async Task<ApiTreeNode> FolderAsync(Guid id,CancellationToken ct)
    {
        var (_,space)=await scope.EntityAsync(id,FolderType.Id,ct);var snapshot=await tree.GetTreeForSpaceAsync(space,ct,true)??throw new UnauthorizedAccessException();
        return FindView(snapshot.Children,id,space,space)??throw new UnauthorizedAccessException();
    }
    private ApiTreeNode View(TreeNodeSnapshot n,Guid space,Guid parent)=>new(n.Entity.Id,types.Get(n.Entity.EntityType).TypeKey,n.Entity.Name,space,parent,n.Children.Count>0);
    private static TreeNodeSnapshot? Find(IReadOnlyList<TreeNodeSnapshot> nodes,Guid id){foreach(var n in nodes){if(n.Entity.Id==id)return n;var result=Find(n.Children,id);if(result!=null)return result;}return null;}
    private ApiTreeNode? FindView(IReadOnlyList<TreeNodeSnapshot> nodes,Guid id,Guid space,Guid parent){foreach(var n in nodes){if(n.Entity.Id==id)return View(n,space,parent);var result=FindView(n.Children,id,space,n.Entity.Id);if(result!=null)return result;}return null;}
}

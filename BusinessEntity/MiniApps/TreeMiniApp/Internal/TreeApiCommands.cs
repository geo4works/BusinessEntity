using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Services;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal;

// UI/HTTP используют один Core graph prepare и тот же серверный Tree authorization.
internal sealed class TreeApiCommands(TreeMiniAppService tree,BusinessEntityHelper graph,IDataProviderConnector data,ApiDomainScope scope,ApiWriteContext write)
{
    public async Task CreateAsync(Guid spaceId,ApiFolderCreate input,CancellationToken ct)
    {
        var (parent,space)=await scope.EntityAsync(input.ParentId??spaceId,null,ct);if(space!=spaceId)throw new ApiFault(409,"api.context_conflict","Родитель находится в другом пространстве.");await tree.EnsureCanAdminItemsAsync(parent.Id,ct);
        var plan=await graph.PrepareChildAsync(parent,FolderType.Id,EntityNameValidator.NormalizeRequired(input.Name),write.Id("folder"),write.Id("contains"),ct:ct);
        await data.SaveAsync(write.Attach(plan.Batch with {OperationId=write.OperationId},plan.Entity.Id,space,[new("AdminItems",parent.Id,space)]),ct);
    }
    public async Task UpdateAsync(Guid id,ApiFolderUpdate input,CancellationToken ct)
    {
        var (_,space)=await scope.EntityAsync(id,FolderType.Id,ct);await tree.EnsureCanAdminItemsAsync(id,ct);var plan=await graph.PrepareRenameAsync(id,EntityNameValidator.NormalizeRequired(input.Name),ct)??throw new KeyNotFoundException();
        await data.SaveAsync(write.Attach(plan.Batch with{OperationId=write.OperationId},id,space,[new("AdminItems",id,space)]),ct);
    }
    public async Task MoveAsync(ApiTreeMove input,CancellationToken ct)
    {
        var ids=await ManageSetAsync(input.SpaceId,input.EntityIds,ct);var (_,targetSpace)=await scope.EntityAsync(input.TargetParentId,null,ct);if(targetSpace!=input.SpaceId)throw new ApiFault(409,"api.context_conflict","Перенос между пространствами не поддержан.");await tree.EnsureCanAdminItemsAsync(input.TargetParentId,ct);
        var batch=await graph.PrepareMoveAsync(ids,input.TargetParentId,"TreeMiniApp",write.OperationId,ct);var checks=ids.Append(input.TargetParentId).Distinct().Select(id=>new ApiPermissionCheck("AdminItems",id,input.SpaceId)).ToArray();
        await data.SaveAsync(write.Attach(batch,ids[0],input.SpaceId,checks),ct);
    }
    public async Task DeleteAsync(ApiTreeDelete input,CancellationToken ct)
    {
        var ids=await ManageSetAsync(input.SpaceId,input.EntityIds,ct);var batch=write.Attach(new(write.OperationId,"TreeMiniApp",null){DeleteEntities=ids},ids[0],input.SpaceId,ids.Select(id=>new ApiPermissionCheck("AdminItems",id,input.SpaceId)).ToArray());
        await data.SaveAsync(batch with {ApiOperation=batch.ApiOperation! with {DeletionPlanId=input.PlanId}},ct);
    }
    public async Task<Guid[]> ManageSetAsync(Guid spaceId,IReadOnlyList<Guid>? source,CancellationToken ct)
    {
        if(source==null||source.Count is <1 or >128)throw new ArgumentException("Нужны 1–128 объектов.");var ids=source.Distinct().ToArray();
        foreach(var id in ids){var(entity,space)=await scope.EntityAsync(id,null,ct);if(entity.EntityType==SpaceType.Id)throw new ArgumentException("Пространство удаляет/перемещает только его владелец.");if(space!=spaceId)throw new ApiFault(409,"api.context_conflict","Набор должен находиться в одном заданном пространстве.");await tree.EnsureCanAdminItemsAsync(id,ct);}return ids;
    }
}

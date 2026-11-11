using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Services;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using Entity=BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Internal;

// Domain scenario использует те же Normalize/prepare/settings rules, что SpaceHelper/UI.
internal sealed class SpaceApiCommands(SpaceHelper spaces,BusinessEntityHelper graph,IDataProviderConnector data,IUserConnector users,ApiDomainScope scope,ApiWriteContext write)
{
    public async Task CreateAsync(ApiSpaceCreate input,CancellationToken ct)
    {
        if((await users.GetCurrentUserAsync(ct))?.IsGlobalAdmin!=true)throw new UnauthorizedAccessException();var session=await scope.CurrentAsync(ct);if(!session.Spaces.AllSpaces)throw new ApiFault(403,"api.space_denied","Создание требует допуска ко всем пространствам.");
        ValidateInterval(input.BackupIntervalMinutes);var plan=graph.PrepareCreate(SpaceType.Id,SpaceHelper.NormalizeName(input.Name),write.Id("space"));plan.Entity.IsPublic=input.IsPublic;
        var settings=new GenericSpaceProperties{DoBackup=input.DoBackup,BackupIntervalMinutes=input.BackupIntervalMinutes};
        var batch=plan.Batch with {OperationId=write.OperationId,Properties=[SpaceHelper.CreateGenericSpaceProperty(plan.Entity.Id,settings,write.Id("settings"))]};
        await data.SaveAsync(write.Attach(batch,plan.Entity.Id,plan.Entity.Id,[new("GlobalAdmin",RequireAllSpaces:true)]),ct);
    }
    public async Task UpdateAsync(Guid id,ApiSpaceUpdate input,CancellationToken ct)
    {
        var entity=await ManageAsync(id,ct);if(input.Name==null&&input.IsPublic==null)throw new ArgumentException("Нет изменяемых полей.");
        var plan=await graph.PrepareRenameAsync(id,input.Name==null?entity.Name:SpaceHelper.NormalizeName(input.Name),ct)??throw new KeyNotFoundException();if(input.IsPublic.HasValue)plan.Entity.IsPublic=input.IsPublic.Value;
        await data.SaveAsync(write.Attach(plan.Batch with {OperationId=write.OperationId},id,id,[new("AdminSpace",id,id)]),ct);
    }
    public async Task SettingsAsync(Guid id,ApiSpaceSettings input,CancellationToken ct)
    {
        await ManageAsync(id,ct);ValidateInterval(input.BackupIntervalMinutes);var current=await spaces.GetGenericSpacePropertiesAsync(id,ct);current.DoBackup=input.DoBackup;current.BackupIntervalMinutes=input.BackupIntervalMinutes;
        var property=await spaces.PrepareGenericSpacePropertiesAsync(id,current,ct);
        await data.SaveAsync(write.Attach(new(write.OperationId,"SpacesMiniApp",null){Properties=[property]},id,id,[new("AdminSpace",id,id)]),ct);
    }
    public async Task DeleteAsync(Guid id,Guid planId,CancellationToken ct)
    {await ManageAsync(id,ct);if(planId==Guid.Empty)throw new ArgumentException("Обязателен planId.");var batch=write.Attach(new(write.OperationId,"SpacesMiniApp",null){DeleteEntities=[id]},id,id,[new("AdminSpace",id,id)]);await data.SaveAsync(batch with {ApiOperation=batch.ApiOperation! with {DeletionPlanId=planId}},ct);}
    // Plan handler отправляет этот проверенный ресурс и assertions DataProvider, не собственный SQL.
    public async Task<Entity> ManageAsync(Guid id,CancellationToken ct)
    {await scope.RequireSpaceAsync(id,ct);var entity=await spaces.GetSpaceByIdAsync(id)??throw new KeyNotFoundException();var permissions=await users.GetCurrentUserPermissionsForSpaceAsync(id,ct);if(!permissions.CanAdminSpace&&!permissions.CanGlobalAdmin)throw new UnauthorizedAccessException();return entity;}
    private static void ValidateInterval(int value){if(value<1)throw new ArgumentException("BackupIntervalMinutes должен быть положительным.");}
}

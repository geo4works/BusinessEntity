using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Services;
using BusinessEntity.Core.Types;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using Document=BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Internal;

// Создание и Save работают с тем же owner validation и полным payload, что UI.
internal sealed class DocumentApiCommands(BusinessEntityHelper graph,IDataProviderConnector data,IUserConnector users,ApiDomainScope scope,ApiWriteContext write,EntityTypeRegistry types)
{
    public async Task CreateAsync(Guid spaceId,ApiDocumentCreate input,CancellationToken ct)
    {
        var(parent,space)=await scope.EntityAsync(input.ParentId??spaceId,null,ct);if(space!=spaceId)throw new ApiFault(409,"api.context_conflict","Родитель находится в другом пространстве.");
        var permissions=await users.GetCurrentUserPermissionsForEntityAsync(parent.Id,ct);if(!permissions.CanAdminItems&&!permissions.CanAdminSpace&&!permissions.CanGlobalAdmin)throw new UnauthorizedAccessException();
        var document=DocumentEditRules.Freeze(new(){Id=write.Id("document"),Name=input.Title,Text=input.Content},types);
        var plan=await graph.PrepareChildAsync(parent,DocumentType.Id,document.Name,document.Id,write.Id("contains"),document.Text,ct);if(input.IsPublic.HasValue)plan.Entity.IsPublic=input.IsPublic.Value;
        var batch=plan.Batch with {OperationId=write.OperationId,Payloads=[new(document.Id,0,document)]};
        await data.SaveAsync(write.Attach(batch,document.Id,space,[new("AdminItems",parent.Id,space)]),ct);
    }
    public async Task UpdateAsync(Guid id,ApiDocumentUpdate input,CancellationToken ct)
    {
        var(entity,space)=await scope.EntityAsync(id,DocumentType.Id,ct);var access=await users.GetEntityContentAccessAsync(id,ct);if(!access.CanEditDraft)throw new UnauthorizedAccessException();if(input.BaseVersion<1)throw new ArgumentException("BaseVersion обязателен.");
        var baseline=await data.GetDataVersionAsync<Document>(id,input.BaseVersion,ct)??throw new KeyNotFoundException();baseline.Name=input.Title;baseline.Text=input.Content;var snapshot=DocumentEditRules.Freeze(baseline,types);
        var checks=new List<ApiPermissionCheck>{new("EditDraft",id,space)};
        var batch=new EntitySaveBatch(write.OperationId,"DocumentsMiniApp",null){Payloads=[new(id,input.BaseVersion,snapshot,VersionDescription:input.VersionDescription??string.Empty)]};
        if(input.IsPublic.HasValue){if(!access.CanChangeCommonFlag)throw new UnauthorizedAccessException();entity.IsPublic=input.IsPublic.Value;batch=batch with{Entities=[entity]};checks.Add(new("ChangeCommon",id,space));}
        await data.SaveAsync(write.Attach(batch,id,space,checks),ct);
    }
    public async Task RestoreAsync(Guid id,int version,CancellationToken ct)
    {var(_,space)=await scope.EntityAsync(id,DocumentType.Id,ct);var access=await users.GetEntityContentAccessAsync(id,ct);if(!access.CanEditDraft||!access.CanViewDraft)throw new UnauthorizedAccessException();if(version<1)throw new ArgumentException("Version должен быть положительным.");await data.SaveAsync(write.Attach(new(write.OperationId,"DocumentsMiniApp",null){Restores=[new(id,version)]},id,space,[new("EditDraft",id,space),new("History",id,space)]),ct);}
    public async Task DeleteAsync(Guid id,Guid? planId,CancellationToken ct)
    {var(_,space)=await scope.EntityAsync(id,DocumentType.Id,ct);var permission=await users.GetCurrentUserPermissionsForEntityAsync(id,ct);if(!permission.CanAdminItems&&!permission.CanAdminSpace&&!permission.CanGlobalAdmin)throw new UnauthorizedAccessException();var batch=write.Attach(new(write.OperationId,"DocumentsMiniApp",null){DeleteEntities=[id]},id,space,[new("Delete",id,space)]);await data.SaveAsync(batch with {ApiOperation=batch.ApiOperation! with {DeletionPlanId=planId}},ct);}
}

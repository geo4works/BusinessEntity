using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity=BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

// Plan проверяется самим SQL writer, поэтому изменение между preview и Delete не теряется.
public sealed class ApiDeletionPlanTests
{
    [PostgresFact]
    public async Task ChangedSubtreeRejectsTheOldPlanAndFreshPlanDeletesAtomically()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var root=Folder("root");var child=Folder("child");var user=Guid.NewGuid();
        await db.Writer.SaveAsync(new(Guid.NewGuid(),"TreeMiniApp",null){Entities=[root,child],Relations=[Edge(root.Id,child.Id)]},new(Guid.NewGuid(),user));
        var original=await PlanAsync(db,root.Id,user);Assert.Equal(2,original.EntityIds.Count);
        var later=Folder("later");await db.Writer.SaveAsync(new(Guid.NewGuid(),"TreeMiniApp",null){Entities=[later],Relations=[Edge(root.Id,later.Id)]},new(Guid.NewGuid(),user));
        var stale=Identity(ApiWriteMode.Canonical);var staleActor=Actor(stale,user);await db.Writer.BeginApiOperationAsync(stale,staleActor);
        var conflict=await Assert.ThrowsAsync<PublicOperationException>(()=>db.Writer.SaveAsync(Delete(stale,root.Id,original.PlanId),staleActor));Assert.Equal("graph.plan_conflict",conflict.Code);
        await using(var sql=new KmsBusinessEntityDbContext(db.Options))Assert.Equal(3,await sql.BusinessEntities.CountAsync());
        var fresh=await PlanAsync(db,root.Id,user);var operation=Identity(ApiWriteMode.Canonical);var actor=Actor(operation,user);await db.Writer.BeginApiOperationAsync(operation,actor);
        var result=await db.Writer.SaveAsync(Delete(operation,root.Id,fresh.PlanId),actor);Assert.Equal(3,result.DeletedEntities.Count);
        await using(var sql=new KmsBusinessEntityDbContext(db.Options))Assert.False(await sql.BusinessEntities.AnyAsync());
        Assert.Equal("Committed",(await db.CreateWriter().ReadApiOperationAsync(operation.OperationId,actor))!.State);
    }
    [PostgresFact]
    public async Task NonemptySubtreeRequiresPlanAndForeignClientCannotUseIt()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var root=Folder("root");var child=Folder("child");var user=Guid.NewGuid();
        await db.Writer.SaveAsync(new(Guid.NewGuid(),"TreeMiniApp",null){Entities=[root,child],Relations=[Edge(root.Id,child.Id)]},new(Guid.NewGuid(),user));
        var operation=Identity(ApiWriteMode.Canonical);var actor=Actor(operation,user);await db.Writer.BeginApiOperationAsync(operation,actor);
        await Assert.ThrowsAsync<PublicOperationException>(()=>db.Writer.SaveAsync(Delete(operation,root.Id,null),actor));
        var plan=await PlanAsync(db,root.Id,user);var foreignOperation=Identity(ApiWriteMode.Canonical);var foreign=Actor(foreignOperation,user) with {ApiClientId="different-client"};await db.Writer.BeginApiOperationAsync(foreignOperation,foreign);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>db.Writer.SaveAsync(Delete(foreignOperation,root.Id,plan.PlanId),foreign));
        await using var sql=new KmsBusinessEntityDbContext(db.Options);Assert.Equal(2,await sql.BusinessEntities.CountAsync());
    }
    [PostgresFact]
    public async Task TechnicalPlanSurvivesWriterRestartAndCannotBecomeAnUnregisteredCanonicalWrite()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var root=Folder("root");var user=Guid.NewGuid();await db.Writer.SaveAsync(new(Guid.NewGuid(),"TreeMiniApp",null){Entities=[root]},new(Guid.NewGuid(),user));
        var operation=Identity(ApiWriteMode.Technical);var actor=Actor(operation,user);await db.Writer.BeginApiOperationAsync(operation,actor);var request=new PrepareApiDeletionPlanRequest(Guid.NewGuid(),Guid.NewGuid(),[root.Id],root.Id,[new("AdminItems",root.Id,root.Id)]){Context=actor};
        var first=await db.Writer.PrepareDeletionPlanAsync(request);var restarted=db.CreateWriter();Assert.Equal(first.PlanId,(await restarted.PrepareDeletionPlanAsync(request)).PlanId);
        var receipt=await restarted.ReadApiOperationAsync(operation.OperationId,actor);Assert.Equal("Registered",receipt!.State);Assert.Equal(first.PlanId,receipt.ReferenceId);Assert.Null(receipt.Result);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>restarted.SaveAsync(Delete(operation,root.Id,first.PlanId),actor));
    }
    private static Entity Folder(string name)=>new(){Name=name,EntityType=global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id};
    private static BusinessEntityRelation Edge(Guid parent,Guid child)=>new(){ObjectAId=parent,ObjectBId=child,RelationType="Contains"};
    private static ApiOperationIdentity Identity(ApiWriteMode mode)=>new(Guid.NewGuid(),"TreeMiniApp",mode==ApiWriteMode.Technical?"tree.deletion-plan":"tree.delete",1,new string('A',64),mode);
    private static BusRequestContext Actor(ApiOperationIdentity identity,Guid user)=>new(Guid.NewGuid(),user,ExecutionKind:BusExecutionKind.Api,ApiSessionId:Guid.NewGuid(),ApiClientId:"client",ApiOperationId:identity.OperationId,ApiRequestHash:identity.RequestHash);
    private static EntitySaveBatch Delete(ApiOperationIdentity identity,Guid root,Guid? plan)=>new(identity.OperationId,"TreeMiniApp",null){DeleteEntities=[root],ApiOperation=new(identity.OperationId,identity.RequestHash,root,root,[new("AdminItems",root,root)],plan)};
    private static async Task<ApiDeletionPlan> PlanAsync(PostgresTestDatabase database,Guid root,Guid user)
    {var identity=Identity(ApiWriteMode.Technical);var actor=Actor(identity,user);await database.Writer.BeginApiOperationAsync(identity,actor);return await database.Writer.PrepareDeletionPlanAsync(new(Guid.NewGuid(),Guid.NewGuid(),[root],root,[new("AdminItems",root,root)]){Context=actor});}
}

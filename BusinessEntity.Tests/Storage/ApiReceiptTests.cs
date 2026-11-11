using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity=BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

// Реальные SQL faults доказывают, что публичный API результат и business outcome коммитятся вместе.
public sealed class ApiReceiptTests
{
    // Регистрация переживает откат как Pending и не выдаётся за созданный документ.
    [PostgresFact]
    public async Task BeforeCommitLeavesPendingReceiptAndNoBusinessOutcome()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var identity=Identity();var actor=Actor(identity);
        await db.Writer.BeginApiOperationAsync(identity,actor);var batch=Batch(identity);db.Faults.ThrowAt=StorageFaultPoint.BeforeCommit;
        await Assert.ThrowsAsync<IOException>(()=>db.Writer.SaveAsync(batch,actor));
        var receipt=await db.CreateWriter().ReadApiOperationAsync(identity.OperationId,actor);Assert.Equal("Pending",receipt!.State);Assert.Null(receipt.Result);
        await using var sql=new KmsBusinessEntityDbContext(db.Options);Assert.False(await sql.BusinessEntities.AnyAsync());Assert.False(await sql.OperationOutcomes.AnyAsync());
    }
    // Потерянный ответ после commit восстанавливает тот же generated ID/версию через новый writer/session.
    [PostgresFact]
    public async Task LostReplyAndRestartReturnCommittedIdsWithoutSecondVersion()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var identity=Identity();var actor=Actor(identity);var batch=Batch(identity);
        await db.Writer.BeginApiOperationAsync(identity,actor);db.Faults.ThrowAt=StorageFaultPoint.AfterCommit;
        await Assert.ThrowsAsync<IOException>(()=>db.Writer.SaveAsync(batch,actor));db.Faults.ThrowAt=null;
        var restarted=db.CreateWriter();var next=actor with {ScopeId=Guid.NewGuid(),ApiSessionId=Guid.NewGuid()};
        var receipt=await restarted.BeginApiOperationAsync(identity,next);Assert.Equal("Committed",receipt.State);Assert.Equal(batch.Entities.Single().Id,receipt.Result!.EntityId);Assert.Equal(1,receipt.Result.Version);
        await restarted.SaveAsync(batch,next);
        await using var sql=new KmsBusinessEntityDbContext(db.Options);Assert.Equal(1,await sql.BusinessEntities.CountAsync());Assert.Equal(1,await sql.BusinessEntityDataItems.CountAsync());Assert.Equal(1,await sql.OperationOutcomes.CountAsync());
    }
    // Повторы конкурируют по устойчивому ID, другое содержание/клиент не присваивает принятую операцию.
    [PostgresFact]
    public async Task ParallelRetriesAreSingleAndDifferentHashIsRejected()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var identity=Identity();var actor=Actor(identity);var batch=Batch(identity);
        await Task.WhenAll(Enumerable.Range(0,6).Select(_=>db.Writer.BeginApiOperationAsync(identity,actor)));
        await Task.WhenAll(Enumerable.Range(0,6).Select(_=>db.Writer.SaveAsync(batch,actor)));
        var error=await Assert.ThrowsAsync<PublicOperationException>(()=>db.Writer.BeginApiOperationAsync(identity with {RequestHash=new string('B',64)},actor));Assert.Equal("operation.id_conflict",error.Code);
        Assert.Null(await db.Writer.ReadApiOperationAsync(identity.OperationId,actor with {ApiClientId="other"}));
        await using var sql=new KmsBusinessEntityDbContext(db.Options);Assert.Equal(1,await sql.TechnicalStates.CountAsync());Assert.Equal(1,await sql.BusinessEntityDataItems.CountAsync());
    }
    // Обычный API sender не может обойти регистрацию, отправив голый EntitySaveBatch.
    [PostgresFact]
    public async Task UnregisteredApiWriteCannotBypassReceipt()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var identity=Identity();var actor=Actor(identity);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>db.Writer.SaveAsync(Batch(identity) with {ApiOperation=null},actor));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>db.Writer.SaveAsync(Batch(identity),actor));
        await using var sql=new KmsBusinessEntityDbContext(db.Options);Assert.False(await sql.BusinessEntities.AnyAsync());
    }
    // Синтетическая typed операция существует только в тестовой сборке.
    private static ApiOperationIdentity Identity()=>new(Guid.NewGuid(),"DocumentsMiniApp","test.create",1,new string('A',64));
    // Доверенный actor не содержит UI window или системного bypass.
    private static BusRequestContext Actor(ApiOperationIdentity identity)=>new(Guid.NewGuid(),Guid.NewGuid(),ExecutionKind:BusExecutionKind.Api,ApiSessionId:Guid.NewGuid(),ApiClientId:"fixture-client",ApiOperationId:identity.OperationId,ApiRequestHash:identity.RequestHash);
    // Полный снимок создаётся с устойчивым generated ID, не выбираемым новым при retry.
    private static EntitySaveBatch Batch(ApiOperationIdentity identity)
    {
        var id=ApiOperationExecutor.StableId(identity.OperationId+":document");var entity=new Entity {Id=id,Name="API fixture",EntityType=global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id};
        return new(identity.OperationId,identity.OwnerMiniAppId,null){Entities=[entity],Payloads=[new(id,0,new Document{Name=entity.Name,Text="single snapshot"})],ApiOperation=new(identity.OperationId,identity.RequestHash,id)};
    }
}

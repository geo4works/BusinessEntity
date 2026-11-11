using System.Text.Json;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Технические receipts физически и транзакционно принадлежат единственному DataProvider writer.
internal sealed partial class DataProviderTransactionService
{
    private const string ApiReceiptKind="ApiOperationV1";
    // Регистрация не создаёт business commit; прежний hash с тем же ID проверяется до повторного исполнения.
    public async Task<ApiOperationReceipt> BeginApiOperationAsync(ApiOperationIdentity operation,BusRequestContext actor,CancellationToken ct=default)
    {
        RequireApiActor(actor);
        if(operation.OperationId==Guid.Empty||operation.Version<1||string.IsNullOrWhiteSpace(operation.OwnerMiniAppId)||string.IsNullOrWhiteSpace(operation.CapabilityId)
            ||!System.Text.RegularExpressions.Regex.IsMatch(operation.RequestHash,"^[A-Fa-f0-9]{64}$"))throw new ArgumentException("Некорректная регистрация операции.");
        using var admission=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);var existing=await db.TechnicalStates.FindAsync([operation.OperationId],ct);
            if(existing!=null)
            {
                VerifyReceiptOwner(existing,actor);var receipt=DeserializeReceipt(existing);
                if(receipt.Identity!=operation)throw new PublicOperationException("operation.id_conflict","Idempotency-Key уже использован с другим запросом.");
                if(receipt.ExpiresAt<=DateTime.UtcNow)throw new PublicOperationException("operation.retention_conflict","Срок гарантированного повтора операции истёк.");
                return receipt;
            }
            var now=DateTime.UtcNow;var created=new ApiOperationReceipt(operation,"Pending",null,null,now+_outcomeRetention);
            db.TechnicalStates.Add(new(){Id=operation.OperationId,OwnerMiniAppId=operation.OwnerMiniAppId,Kind=ApiReceiptKind,AuthorUserId=actor.UserId,
                ClientId=actor.ApiClientId!,Json=JsonSerializer.Serialize(created,StorageJsonOptions.Default),ModifiedAt=now,ExpiresAt=created.ExpiresAt});
            await db.SaveChangesAsync(ct);return created;
        }
        finally{_writer.Release();}
    }
    // Новый session того же автора/client может читать результат после рестарта или потерянного ответа.
    public async Task<ApiOperationReceipt?> ReadApiOperationAsync(Guid id,BusRequestContext actor,CancellationToken ct=default)
    {
        RequireApiActor(actor);await using var db=new KmsBusinessEntityDbContext(_options);
        var row=await db.TechnicalStates.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.Kind==ApiReceiptKind&&x.AuthorUserId==actor.UserId&&x.ClientId==actor.ApiClientId,ct);
        return row==null||row.ExpiresAt<=DateTime.UtcNow?null:DeserializeReceipt(row);
    }
    // Проверка выполняется в той же SQL-транзакции, что canonical changes и OperationOutcome.
    private static async Task<TechnicalStateRow?> ValidateApiCommitAsync(KmsBusinessEntityDbContext db,EntitySaveBatch batch,BusRequestContext actor,CancellationToken ct)
    {
        if(batch.ApiOperation==null)
        {if(actor.ExecutionKind==BusExecutionKind.Api)throw new UnauthorizedAccessException("API write требует принятой операции.");return null;}
        RequireApiActor(actor);var commit=batch.ApiOperation;
        if(commit.OperationId!=batch.OperationId||actor.ApiOperationId!=batch.OperationId||actor.ApiRequestHash!=commit.RequestHash)throw new UnauthorizedAccessException();
        var row=await db.TechnicalStates.FindAsync([batch.OperationId],ct)??throw new KeyNotFoundException("Операция не зарегистрирована.");
        VerifyReceiptOwner(row,actor);var receipt=DeserializeReceipt(row);
        if(receipt.Identity.WriteMode==ApiWriteMode.Technical||(receipt.Identity.WriteMode==ApiWriteMode.Deferred&&receipt.ReferenceId==null))throw new UnauthorizedAccessException("Операция не допускает canonical commit.");
        if(receipt.Identity.RequestHash!=commit.RequestHash||receipt.Identity.OwnerMiniAppId!=batch.MiniAppId)
            throw new PublicOperationException("operation.id_conflict","Пакет не соответствует принятой операции.");
        if(receipt.ExpiresAt<=DateTime.UtcNow)throw new PublicOperationException("operation.retention_conflict","Срок гарантированного повтора истёк.");
        return row;
    }
    // Public mutation response возникает из реально сохранённых IDs/версий и фиксируется до commit.
    private static async Task CompleteApiReceiptAsync(KmsBusinessEntityDbContext db,TechnicalStateRow? row,ApiOperationCommit? commit,SaveBatchResult result,CancellationToken ct)
    {
        if(row==null)return;
        var receipt=DeserializeReceipt(row);var primary=commit!.EntityId;
        var version=result.Versions.SingleOrDefault(x=>x.EntityId==primary);
        int? number=version?.Version;
        if(number==null&&primary.HasValue&&!result.DeletedEntities.Contains(primary.Value))
        {
            if(commit.ResponseVersion.HasValue)
            {if(!await db.BusinessEntityDataItems.AnyAsync(x=>x.BusinessEntityId==primary.Value&&x.Version==commit.ResponseVersion.Value,ct))throw new KeyNotFoundException();number=commit.ResponseVersion;}
            else number=await db.BusinessEntityDataItems.Where(x=>x.BusinessEntityId==primary.Value).Select(x=>(int?)x.Version).MaxAsync(ct);
        }
        var outcome=new ApiMutationOutcome(result.OperationId,primary,number,version?.CreatedVersion??false,result.ChangedEntities,result.DeletedEntities);
        row.Json=JsonSerializer.Serialize(receipt with {State="Committed",Result=outcome,SpaceId=commit.SpaceId},StorageJsonOptions.Default);
        row.ModifiedAt=result.CommittedAt;row.Revision++;await CompleteDeferredJobAsync(db,receipt,outcome,ct);
    }
    // Некорректный receipt не подменяется новым Pending.
    private static ApiOperationReceipt DeserializeReceipt(TechnicalStateRow row)=>JsonSerializer.Deserialize<ApiOperationReceipt>(row.Json,StorageJsonOptions.Default)??throw new InvalidDataException();
    // Канонический API author никогда не IsSystem и всегда связан с проверенным OAuth client/session.
    private static void RequireApiActor(BusRequestContext actor)
    {if(actor.IsSystem||actor.ExecutionKind!=BusExecutionKind.Api||!actor.UserId.HasValue||actor.ApiSessionId==null||string.IsNullOrEmpty(actor.ApiClientId))throw new UnauthorizedAccessException();}
    // Чужой GUID не раскрывает ни payload, ни сам факт регистрации чужой операции.
    private static void VerifyReceiptOwner(TechnicalStateRow row,BusRequestContext actor)
    {if(row.Kind!=ApiReceiptKind||row.AuthorUserId!=actor.UserId||row.ClientId!=actor.ApiClientId)throw new UnauthorizedAccessException();}
}

using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Preview/final validation принадлежат тому же writer, что canonical удаление.
internal sealed partial class DataProviderTransactionService
{
    private const string DeletionPlanKind="ApiDeletionPlanV1";
    // Technical plan и его receipt-reference фиксируются одной SQL транзакцией, без версии Space.
    public async Task<ApiDeletionPlan> PrepareDeletionPlanAsync(PrepareApiDeletionPlanRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);if(request.PlanId==Guid.Empty||request.RootIds.Count is <1 or >128)throw new ArgumentException("Нужны 1–128 корневых объектов.");
        using var admitted=_committedGraph.EnterWrite(actor);await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);
            var operation=await TechnicalReceiptAsync(db,actor,ApiWriteMode.Technical,ct);var receipt=DeserializeReceipt(operation);
            await _committedGraph.AuthorizeApiAsync(receipt.Identity,new(receipt.Identity.OperationId,receipt.Identity.RequestHash,null,request.SpaceId,request.Checks),actor,ct);
            var existing=await db.TechnicalStates.FindAsync([request.PlanId],ct);
            if(existing!=null){VerifyTechnicalOwner(existing,actor,DeletionPlanKind,receipt.Identity.OwnerMiniAppId);return DeserializePlan(existing).Public;}
            var roots=request.RootIds.Distinct().OrderBy(x=>x.ToString("N"),StringComparer.Ordinal).ToArray();var snapshot=await DeletionSnapshotAsync(db,roots,ct);
            var now=DateTime.UtcNow;var result=new ApiDeletionPlan(request.PlanId,roots,snapshot.Ids,snapshot.HasDependencies,now.AddMinutes(5));
            db.TechnicalStates.Add(new(){Id=result.PlanId,OwnerMiniAppId=receipt.Identity.OwnerMiniAppId,Kind=DeletionPlanKind,AuthorUserId=actor.UserId,ClientId=actor.ApiClientId!,Json=JsonSerializer.Serialize(new StoredDeletionPlan(result,snapshot.Hash,null),StorageJsonOptions.Default),ModifiedAt=now,ExpiresAt=receipt.ExpiresAt});
            operation.Json=JsonSerializer.Serialize(receipt with {State="Registered",SpaceId=request.SpaceId,ReferenceId=result.PlanId},StorageJsonOptions.Default);operation.Revision++;operation.ModifiedAt=now;
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return result;
        }
        finally{_writer.Release();}
    }
    // Writer повторяет проверку состава/зависимостей после всех предшествующих commits, до удаления.
    private static async Task ValidateApiDeletionAsync(KmsBusinessEntityDbContext db,PreparedBatch batch,BusRequestContext actor,CancellationToken ct)
    {
        var roots=batch.Supplement.DeleteEntities.Distinct().ToArray();if(roots.Length==0)return;
        var snapshot=await DeletionSnapshotAsync(db,roots,ct,batch.Supplement.DeleteRelations.ToHashSet());var planId=batch.ApiOperation!.DeletionPlanId;
        if(planId is {} id)
        {
            var row=await db.TechnicalStates.FindAsync([id],ct)??throw new PublicOperationException("graph.plan_conflict","План удаления отсутствует.");
            VerifyTechnicalOwner(row,actor,DeletionPlanKind,null);var plan=DeserializePlan(row);
            if(plan.Public.ExpiresAt<=DateTime.UtcNow||plan.Fingerprint!=snapshot.Hash||!plan.Public.RootIds.Order().SequenceEqual(roots.Order())||(plan.ConsumedBy.HasValue&&plan.ConsumedBy!=actor.ApiOperationId))
                throw new PublicOperationException("graph.plan_conflict","План истёк или состав/зависимости изменились. Подготовьте новый план.");
            row.Json=JsonSerializer.Serialize(plan with {ConsumedBy=actor.ApiOperationId},StorageJsonOptions.Default);row.Revision++;
        }
        else if(roots.Length>1||snapshot.Ids.Length>roots.Length)throw new PublicOperationException("graph.plan_conflict","Для удаления набора/поддерева нужен явный актуальный план.");
        if(snapshot.HasDependencies)throw new PublicOperationException("storage.reference_conflict","Удаляемые объекты используются сохранёнными внешними зависимостями.");
    }
    // Fingerprint включает структуру, latest metadata и сохранённые версии, но не загружает тела книг.
    private static async Task<DeleteSnapshot> DeletionSnapshotAsync(KmsBusinessEntityDbContext db,IReadOnlyList<Guid> roots,CancellationToken ct,IReadOnlySet<Guid>? removedRelations=null)
    {
        if(await db.BusinessEntities.CountAsync(x=>roots.Contains(x.Id),ct)!=roots.Count)throw new KeyNotFoundException();
        var relations=await db.BusinessEntityRelations.AsNoTracking().ToListAsync(ct);var children=relations.Where(x=>x.RelationType=="Contains").ToLookup(x=>x.ObjectAId,x=>x.ObjectBId);
        var ids=roots.ToHashSet();var queue=new Queue<Guid>(roots);while(queue.TryDequeue(out var parent))foreach(var child in children[parent])if(ids.Add(child))queue.Enqueue(child);
        if(ids.Count>10000)throw new PublicOperationException("graph.plan_conflict","План превышает предел 10000 объектов; разделите удаление.");
        var ordered=ids.OrderBy(x=>x.ToString("N"),StringComparer.Ordinal).ToArray();
        var entities=await db.BusinessEntities.AsNoTracking().Where(x=>ids.Contains(x.Id)).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.EntityType,x.Name,x.LastModifiedDate,x.PublishedVersion}).ToArrayAsync(ct);
        var versions=await db.BusinessEntityDataItems.AsNoTracking().Where(x=>ids.Contains(x.BusinessEntityId)).OrderBy(x=>x.BusinessEntityId).ThenBy(x=>x.Version).Select(x=>new{x.BusinessEntityId,x.Version,x.ContentHash}).ToArrayAsync(ct);
        var boundary=relations.Where(x=>ids.Contains(x.ObjectAId)||ids.Contains(x.ObjectBId)).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ObjectAId,x.ObjectBId,x.RelationType,x.RelationParams}).ToArray();
        var files=await db.VersionFileReferences.AsNoTracking().Where(x=>ids.Contains(x.FileEntityId)&&!ids.Contains(x.SourceEntityId)).OrderBy(x=>x.SourceEntityId).ThenBy(x=>x.Version).ThenBy(x=>x.FileEntityId).ThenBy(x=>x.FileId).ThenBy(x=>x.Variant).Select(x=>new{x.SourceEntityId,x.Version,x.FileEntityId,x.FileId,x.Variant}).ToArrayAsync(ct);
        var dependencies=files.Length>0||relations.Any(x=>x.RelationType!="Contains"&&!ids.Contains(x.ObjectAId)&&ids.Contains(x.ObjectBId)&&removedRelations?.Contains(x.Id)!=true);
        return new(ordered,Hash(JsonSerializer.Serialize(new{Roots=roots.Order(),Entities=entities,Versions=versions,Relations=boundary,Files=files},StorageJsonOptions.Default)),dependencies);
    }
    // Known technical command не может коммитить canonical receipt или чужую запись.
    private static async Task<TechnicalStateRow> TechnicalReceiptAsync(KmsBusinessEntityDbContext db,BusRequestContext actor,ApiWriteMode mode,CancellationToken ct)
    {
        if(actor.ApiOperationId is not {} id)throw new UnauthorizedAccessException();var row=await db.TechnicalStates.FindAsync([id],ct)??throw new KeyNotFoundException();VerifyReceiptOwner(row,actor);var receipt=DeserializeReceipt(row);
        if(receipt.Identity.WriteMode!=mode||receipt.Identity.RequestHash!=actor.ApiRequestHash||receipt.ExpiresAt<=DateTime.UtcNow)throw new UnauthorizedAccessException();return row;
    }
    // JSON не принимается от HTTP клиента как произвольная internal command.
    private static StoredDeletionPlan DeserializePlan(TechnicalStateRow row)=>JsonSerializer.Deserialize<StoredDeletionPlan>(row.Json,StorageJsonOptions.Default)??throw new InvalidDataException();
    private static void VerifyTechnicalOwner(TechnicalStateRow row,BusRequestContext actor,string kind,string? owner)
    {if(row.Kind!=kind||row.AuthorUserId!=actor.UserId||row.ClientId!=actor.ApiClientId||(owner!=null&&row.OwnerMiniAppId!=owner))throw new UnauthorizedAccessException();}
    private sealed record StoredDeletionPlan(ApiDeletionPlan Public,string Fingerprint,Guid? ConsumedBy);
    private sealed record DeleteSnapshot(Guid[] Ids,string Hash,bool HasDependencies);
}

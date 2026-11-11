using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

internal sealed partial class DataProviderTransactionService
{
    // Удаление target атомарно закрывает его неиспользованные inputs, а не отравляет quota на сутки.
    private async Task InvalidateUploadTargetsAsync(KmsBusinessEntityDbContext db,IReadOnlyList<Guid> deleted,CancellationToken ct)
    {
        if(deleted.Count==0)return;var ids=deleted.Select(x=>x.ToString()).ToArray();
        var rows=await db.TechnicalStates.FromSqlInterpolated($"SELECT * FROM \"DataProviderTechnicalStates\" WHERE \"Kind\"={UploadKind} AND (\"Json\"::jsonb->'public'->>'targetId')=ANY({ids})").ToListAsync(ct);
        foreach(var row in rows)await InvalidateOneUploadAsync(db,row,ct);
    }
    private static async Task InvalidateOneUploadAsync(KmsBusinessEntityDbContext db,TechnicalStateRow row,CancellationToken ct)
    {
        var upload=ReadUpload(row);if(upload.ConsumedBy!=null||upload.Public.State=="Cancelled")return;SaveUpload(row,upload with{ReservedBy=null,Public=upload.Public with{State="Cancelled"}});
        if(upload.ReservedBy is not {} operationId)return;var operation=await db.TechnicalStates.FindAsync([operationId],ct);if(operation?.Kind!=ApiReceiptKind)return;var receipt=DeserializeReceipt(operation);if(receipt.State=="Committed"||receipt.ReferenceId is not {} jobId)return;
        var jobRow=await db.TechnicalStates.FindAsync([jobId],ct);if(jobRow?.Kind!=JobKind)return;var job=ReadJob(jobRow);if(job.Public.State is "Succeeded" or "Failed" or "Cancelled")return;
        job=job with{Public=job.Public with{State="Failed",ErrorCode="record.not_found",UpdatedAt=DateTime.UtcNow}};SaveJob(jobRow,job);RegisterJobReceipt(operation,receipt,jobId,job.Public.SpaceId,"Failed");await ReleaseJobUploadsAsync(db,job,ct);
    }
    // Обычная maintenance также подбирает orphan inputs после уже завершённого удаления target.
    private async Task InvalidateOrphanUploadsAsync(CancellationToken ct)
    {
        await _writer.WaitAsync(ct);
        try
        {
            await using var db=new KmsBusinessEntityDbContext(_options);await using var tx=await db.Database.BeginTransactionAsync(ct);
            var rows=await db.TechnicalStates.FromSqlInterpolated($"SELECT s.* FROM \"DataProviderTechnicalStates\" s LEFT JOIN \"BusinessEntities\" e ON (s.\"Json\"::jsonb->'public'->>'targetId')=e.\"Id\"::text WHERE s.\"Kind\"={UploadKind} AND e.\"Id\" IS NULL AND s.\"Json\"::jsonb->'consumedBy'='null'::jsonb AND s.\"Json\"::jsonb->'public'->>'state'<>'Cancelled'").ToListAsync(ct);
            foreach(var row in rows)await InvalidateOneUploadAsync(db,row,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        finally{_writer.Release();}
    }
}

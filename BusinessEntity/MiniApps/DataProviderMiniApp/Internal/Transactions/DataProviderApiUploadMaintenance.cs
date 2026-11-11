using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

internal sealed partial class DataProviderTransactionService
{
    // Автоматически убирается лишь собственный временный input; canonical storage/тома не затрагиваются.
    internal async Task CleanupUploadsAsync(CancellationToken ct)
    {
        await InvalidateOrphanUploadsAsync(ct);
        await using var db=new KmsBusinessEntityDbContext(_options);var now=DateTime.UtcNow;
        await foreach(var row in db.TechnicalStates.AsNoTracking().Where(x=>x.Kind==UploadKind).AsAsyncEnumerable().WithCancellation(ct))
        {
            var upload=ReadUpload(row);if(upload.Public.ExpiresAt<=now||upload.Public.State=="Cancelled"||(upload.ConsumedBy.HasValue&&row.ModifiedAt<now.AddHours(-1)))
                try{_files.ApiStaging.Cancel(row.Id);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        await foreach(var row in db.TechnicalStates.AsNoTracking().Where(x=>x.Kind==PreparedChunksKind).AsAsyncEnumerable().WithCancellation(ct))
        {var prepared=ReadPrepared(row);if(prepared.ExpiresAt<=now||prepared.ConsumedBy.HasValue&&row.ModifiedAt<now.AddHours(-1))try{_files.ApiStaging.CancelPrepared(row.Id);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}

internal sealed class ApiUploadMaintenance(DataProviderTransactionService storage,ILogger<ApiUploadMaintenance> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await storage.CleanupUploadsAsync(stoppingToken);}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Upload staging cleanup недоступна; повтор при следующем проходе.");}
            try{await Task.Delay(TimeSpan.FromMinutes(10),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}

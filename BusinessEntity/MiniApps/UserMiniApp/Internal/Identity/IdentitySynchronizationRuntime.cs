using System.Text.Json;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.Infrastructure.Modularity;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Восстанавливает единственный committed выбор после потерянного результата активации, без fallback.
internal sealed class IdentitySynchronizationRuntime(IdentityStateStore store,IdentityRuntime runtime,
    IdentityAdmissionGate admission,ILogger<IdentitySynchronizationRuntime> logger):IModuleRuntime
{
    private CancellationTokenSource? _stop;
    private Task? _work;
    // Периодическая сверка не удерживает HTTP/circuit или открытый DbContext.
    public Task StartAsync(CancellationToken ct)
    {_stop=CancellationTokenSource.CreateLinkedTokenSource(ct);_work=RunAsync(_stop.Token);return Task.CompletedTask;}
    // Остановка дожидается своего цикла и освобождает только собственные ресурсы.
    public async Task StopAsync(CancellationToken ct)
    {if(_stop==null)return;_stop.Cancel();if(_work!=null)try{await _work.WaitAsync(ct);}catch(OperationCanceledException){} _stop.Dispose();_stop=null;}
    // При Unknown сначала завершается writer, затем состояние читается свежим SQL запросом.
    private async Task RunAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while(await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await store.ExclusiveAsync(async () =>
                    {
                        if(runtime.IsSuspended)await admission.WaitForDrainedAsync(ct);
                        var state=await store.ReadAsync(db=>db.IdentityInstallation.SingleAsync(ct));
                        var row=await store.ReadAsync(db=>db.IdentityProfileRevisions.SingleAsync(x=>x.ProviderId==state.ActiveProviderId&&x.Revision==state.ActiveRevision,ct));
                        var profile=JsonSerializer.Deserialize<IdentityProviderProfile>(row.Json,IdentityStateStore.Json)??throw new InvalidDataException();
                        await runtime.AddAsync(profile);
                        if(runtime.IsSuspended)runtime.ResumeAuthoritative(profile.Id,profile.Revision,state.Epoch);
                        else runtime.Activate(profile.Id,profile.Revision,state.Epoch);
                        return true;
                    },ct);
                }
                catch(Exception ex)when(ex is not OperationCanceledException){logger.LogWarning("Не удалось сверить identity configuration: {ErrorType}",ex.GetType().Name);}
            }
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){}
    }
}

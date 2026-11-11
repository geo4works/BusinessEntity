using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.Infrastructure.Api;

public interface IApiJobRuntimeBinding
{
    ApiJobDefinition Definition{get;}
    Task RunAsync(BusRequestClient bus,Guid jobId,CancellationToken ct);
}
public static class ApiJobRuntimeRegistration
{
    public static void AddApiJobRuntime<TRequest,TResponse>(this IServiceCollection services,ApiJobDefinition definition,Func<Guid,TRequest> request)where TRequest:BusRequest where TResponse:BusResponse
    {services.AddSingleton(definition);services.AddSingleton<IApiJobRuntimeBinding>(new Binding<TRequest,TResponse>(definition,request));}
    private sealed class Binding<TRequest,TResponse>(ApiJobDefinition definition,Func<Guid,TRequest> request):IApiJobRuntimeBinding where TRequest:BusRequest where TResponse:BusResponse
    {
        public ApiJobDefinition Definition=>definition;
        public async Task RunAsync(BusRequestClient bus,Guid jobId,CancellationToken ct)=>await bus.SendAsync<TRequest,TResponse>(request(jobId),ct,TimeSpan.FromHours(2));
    }
}
// Один runtime обслуживает только заранее объявленные typed owner bindings на той же шине.
public sealed class ApiJobRuntime(IServiceScopeFactory scopes,ApiHeavyWorkGate heavy,IEnumerable<IApiJobRuntimeBinding> registered,ILogger<ApiJobRuntime> logger):BackgroundService
{
    private readonly Guid _runtimeId=Guid.NewGuid();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bindings=registered.ToDictionary(x=>x.Definition.Key,StringComparer.Ordinal);
        while(!stoppingToken.IsCancellationRequested)
        {
            StoredApiJob? job=null;
            try
            {
                using var heavyLease=await heavy.EnterAsync(stoppingToken);await using var work=scopes.CreateAsyncScope();work.ServiceProvider.GetRequiredService<BusRequestScope>().MarkSystem();var bus=work.ServiceProvider.GetRequiredService<BusRequestClient>();
                job=(await bus.SendAsync<ClaimApiJobRequest,ClaimApiJobResponse>(new(Guid.NewGuid(),_runtimeId,bindings.Keys.ToArray()),stoppingToken)).Result;
                if(job?.Public.State=="Running")
                {
                    await bus.SendAsync<RestoreApiJobAuthorizationRequest,RestoreApiJobAuthorizationResponse>(new(Guid.NewGuid(),job.Public.Id),stoppingToken);
                    await bindings[job.Public.Owner+":"+job.Public.Kind].RunAsync(bus,job.Public.Id,CancellationToken.None);
                }
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception error)
            {
                if(job!=null)await RecordFailureAsync(job,error,stoppingToken);else logger.LogWarning("Очередь API jobs временно недоступна.");
            }
            try{await Task.Delay(TimeSpan.FromMilliseconds(500),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
        }
    }
    // Сначала свежий writer сверяет canonical receipt. Committed всегда побеждает ошибку/отмену.
    private async Task RecordFailureAsync(StoredApiJob job,Exception error,CancellationToken ct)
    {
        try
        {
            await using var work=scopes.CreateAsyncScope();work.ServiceProvider.GetRequiredService<BusRequestScope>().MarkSystem();var bus=work.ServiceProvider.GetRequiredService<BusRequestClient>();var code=BusError.FromException(error).Code;
            var state=code=="access.denied"||code.StartsWith("api.session_")||code is "api.identity_invalid" or "api.grant_denied" or "api.capability_denied" or "api.space_denied"?"NeedsReauthorization":code is "bus.timeout" or "operation.unknown" or "operation.failed" or "storage.unavailable"?"Running":"Failed";
            await bus.SendAsync<UpdateApiJobRequest,UpdateApiJobResponse>(new(Guid.NewGuid(),job.Public.Id,state,ErrorCode:code),ct);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){}
        catch(Exception){logger.LogWarning("Исход API job {JobId} не подтверждён; требуется reconciliation.",job.Public.Id);}
    }
}

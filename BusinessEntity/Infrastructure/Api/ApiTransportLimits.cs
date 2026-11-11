namespace BusinessEntity.Infrastructure.Api;

// Лимиты установки не содержат бизнес-прав и не зависят от текущего браузерного пространства.
public sealed class ApiTransportLimits
{
    public int RequestsPerMinute {get;}
    public int MaxConcurrentRequests {get;}
    public long MaxJsonBytes {get;}
    // Неверная конфигурация останавливает запуск, не снимает ограничение молча.
    public ApiTransportLimits(IConfiguration configuration)
    {RequestsPerMinute=configuration.GetValue("Api:RequestsPerMinute",120);MaxConcurrentRequests=configuration.GetValue("Api:MaxConcurrentRequests",16);MaxJsonBytes=configuration.GetValue("Api:MaxJsonBytes",2L*1024*1024);
        if(RequestsPerMinute<1||MaxConcurrentRequests<1||MaxJsonBytes<1)throw new ArgumentOutOfRangeException("Api transport limits");}
}

// Приём HTTP ограничен отдельно от SQL admission/drain и очереди тяжёлых jobs.
public sealed class ApiConcurrencyGate(ApiTransportLimits limits)
{
    private readonly SemaphoreSlim _slots=new(limits.MaxConcurrentRequests,limits.MaxConcurrentRequests);
    // Ненакопительная очередь отвечает 429; принятый SQL writer имеет собственную независимую lifetime.
    public IDisposable Enter()
    {if(!_slots.Wait(0))throw new ApiFault(429,"api.busy","Слишком много параллельных запросов.");return new Lease(_slots);}
    // Освобождает только собственный HTTP слот один раз.
    private sealed class Lease(SemaphoreSlim slots):IDisposable
    {private int _released;public void Dispose(){if(Interlocked.Exchange(ref _released,1)==0)slots.Release();}}
}

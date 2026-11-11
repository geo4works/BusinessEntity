namespace BusinessEntity.Infrastructure.Api;

// Общая техническая data-plane не передаёт bytes/Stream/path по шине; lease выдаёт DataProvider после ACL.
public interface IApiStagingDataPlane
{
    Task CopyPartAsync(Guid leaseId,Stream input,CancellationToken ct);
    Stream OpenRead(Guid leaseId);
    Stream OpenPreparedWrite(Guid leaseId);
}
// Только зарегистрированный binary endpoint расширяет JSON body limit до фиксированной части.
public sealed record ApiBinaryRequestLimit(long MaxBytes);
// Один тяжёлый parser/assemble/video процесс установки, без неограниченной очереди в памяти.
public sealed class ApiHeavyWorkGate
{
    private readonly SemaphoreSlim _gate=new(1,1);
    public async Task<IDisposable> EnterAsync(CancellationToken ct){await _gate.WaitAsync(ct);return new Lease(_gate);}
    private sealed class Lease(SemaphoreSlim gate):IDisposable{private int _released;public void Dispose(){if(Interlocked.Exchange(ref _released,1)==0)gate.Release();}}
}

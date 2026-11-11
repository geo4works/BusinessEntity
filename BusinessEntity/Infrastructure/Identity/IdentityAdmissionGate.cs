namespace BusinessEntity.Infrastructure.Identity;

// Переключение доверия ждёт принятые записи; новые записи не стартуют в промежутке активации.
public sealed class IdentityAdmissionGate
{
    private readonly object _gate = new();
    private bool _switching;
    private Guid? _permit;
    private int _writers;
    private TaskCompletionSource _drained = Completed();
    // Приём записи проверяет ожидаемую эпоху под общей блокировкой admission.
    public IDisposable Enter(long expectedEpoch, Func<long> currentEpoch, Guid? permit = null)
    {
        lock (_gate)
        {
            if ((_switching && (permit == null || permit != _permit)) || expectedEpoch != currentEpoch()) throw new UnauthorizedAccessException("Контекст авторизации устарел.");
            if (_writers++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new Lease(ReleaseWriter);
        }
    }
    // Возвращает lease паузы после drain; таймаут снимает паузу без активации.
    public async Task<IdentityActivationLease> PauseAsync(TimeSpan timeout, CancellationToken ct)
    {
        Task waiting;
        lock (_gate)
        {
            if (_switching) throw new InvalidOperationException("Переключение уже выполняется.");
            _switching = true; _permit = Guid.NewGuid(); waiting = _drained.Task;
        }
        try { await waiting.WaitAsync(timeout, ct); return new IdentityActivationLease(_permit!.Value, () => { lock (_gate) { _switching = false; _permit = null; } }); }
        catch { lock (_gate) { _switching = false; _permit = null; } throw; }
    }
    // После неизвестного исхода reconciliation сначала дожидается завершения реальных writers.
    public Task WaitForDrainedAsync(CancellationToken ct)
    {lock(_gate)return _drained.Task.WaitAsync(ct);}
    // Подтверждённый commit/отказ завершает единственное участие writer в drain.
    private void ReleaseWriter()
    { lock (_gate) if (--_writers == 0) _drained.TrySetResult(); }
    // Начальное отсутствие писателей уже удовлетворяет drain.
    private static TaskCompletionSource Completed()
    { var value = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); value.SetResult(); return value; }
    // Повторное освобождение не меняет счётчик писателей.
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

// Неэкспортируемое браузеру разрешение одной активации сохраняется только в памяти процесса.
public sealed class IdentityActivationLease(Guid id, Action release) : IDisposable
{
    public Guid Id { get; } = id;
    private Action? _release = release;
    // Завершение активации или отказ снимает паузу ровно один раз.
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

// Применяет active epoch ко всем каноническим записям UI/API, не только к HTTP middleware.
public sealed class IdentityTransactionAdmission(IIdentityProviderRegistry registry, IdentityAdmissionGate gate) : BusinessEntity.Infrastructure.Bus.ITransactionAdmission
{
    // Системный runtime помечается явно сервером; клиент API не может передать этот признак.
    public IDisposable Enter(BusinessEntity.Infrastructure.Bus.BusRequestContext actor)
        => gate.Enter(actor.IsSystem ? registry.Current.Epoch : actor.AuthEpoch, () => registry.Current.Epoch, actor.ActivationPermit);
}

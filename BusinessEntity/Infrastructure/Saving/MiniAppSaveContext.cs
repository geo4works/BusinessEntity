using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.Infrastructure.Saving;

// Общая единица dirty/Save, принадлежащая окну и предметному рабочему экземпляру.
public sealed class MiniAppSaveContext
{
    private readonly ISaveBatchTransport _transport;
    private readonly BusRequestScope _scope;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private IMiniAppSaveParticipant? _participant;
    private EntitySaveBatch? _pending;
    private SaveBatchResult? _committed;
    private bool _prepared;
    private long _sentRevision, _revision, _acknowledged;
    public Guid EditSessionId { get; } = Guid.NewGuid();
    public Guid LogicalWindowId { get; }
    public Guid? UserId { get; }
    public string MiniAppId { get; }
    public Guid? ContextId { get; }
    public bool IsDirty => Interlocked.Read(ref _revision) != Interlocked.Read(ref _acknowledged);
    public long Revision => Interlocked.Read(ref _revision);
    public MiniAppSaveStatus Status { get; private set; }
    public string? Error { get; private set; }
    public Guid? OperationId => _pending?.OperationId;
    public event Action? Changed;
    // Владелец и окно фиксируются менеджером, а не берутся из браузерной команды.
    public MiniAppSaveContext(string miniAppId, Guid? contextId, BusRequestScope scope, ISaveBatchTransport transport)
    { MiniAppId = miniAppId; ContextId = contextId; _scope = scope; _transport = transport; LogicalWindowId = scope.LogicalWindowId; UserId = scope.UserId; }
    // Один предметный участник обслуживает все компоненты рабочего экземпляра.
    public void RegisterParticipant(IMiniAppSaveParticipant participant)
    {
        if (_participant != null && !ReferenceEquals(_participant, participant))
            throw new InvalidOperationException("У контекста сохранения уже есть владелец черновика.");
        _participant = participant;
    }
    // Рабочий экземпляр переживает пересоздание UI и принадлежит самому save context.
    public TParticipant GetOrCreateParticipant<TParticipant>(Func<TParticipant> create) where TParticipant : class, IMiniAppSaveParticipant
    {
        if (_participant is TParticipant participant) return participant;
        var result = create(); RegisterParticipant(result); return result;
    }
    // UI отмечает новую ревизию, не меняя подтверждение предыдущей операции.
    public void MarkChanged() { Interlocked.Increment(ref _revision); Changed?.Invoke(); }
    // Не запускает два параллельных применения; повтор Unknown использует прежний frozen package.
    public async Task<MiniAppSaveAttempt> SaveAsync(Guid moduleId, CancellationToken ct = default)
    {
        await _operation.WaitAsync(ct);
        try
        {
            if (!IsDirty && _pending == null) return new(true, Status);
            if (_participant == null) return Fail("Владелец сохранения не зарегистрирован.");
            using var address = _scope.Enter(moduleId, EditSessionId, ContextId);
            if (_pending == null)
            {
                try
                {
                    _sentRevision = _revision; _prepared = false;
                    _pending = _participant.Capture(Guid.NewGuid(), EditSessionId, _sentRevision);
                    if (_pending.EditSessionId != EditSessionId || _pending.MiniAppId != MiniAppId)
                        throw new InvalidOperationException("Пакет не принадлежит контексту сохранения.");
                }
                catch (Exception exception) { _pending = null; return Fail(exception.Message); }
            }
            Status = MiniAppSaveStatus.Saving; Error = null; Changed?.Invoke();
            if (!_prepared)
            {
                try
                {
                    // Снимок уже зафиксирован Capture: конвертация HTML не читает живой черновик.
                    var capturedOperation = _pending.OperationId;
                    _pending = await _participant.PrepareAsync(_pending, ct);
                    if (_pending.OperationId != capturedOperation || _pending.EditSessionId != EditSessionId || _pending.MiniAppId != MiniAppId)
                        throw new InvalidOperationException("Подготовлен чужой пакет.");
                    _prepared = true;
                }
                catch (Exception exception) { _pending = null; return Fail(exception.Message); }
            }
            try
            {
                if (_committed == null)
                    _committed = await _transport.SaveAsync(_pending, ct);
            }
            catch (Exception exception)
            {
                // Даже отказ ответа после SQL commit не доказывает отката; проверяется durable outcome.
                try { _committed = (await _transport.GetOutcomeAsync(_pending.OperationId, ct))?.Result; }
                catch (Exception outcomeError)
                { Error = BusError.FromException(outcomeError).Message; }
                if (_committed == null)
                {
                    var definite = exception is BusRequestFailedException failure &&
                        failure.Error.Code is "validation.failed" or "access.denied" or "payload.invalid" or "payload.unsupported" or "record.not_found";
                    if (definite) { _pending = null; return Fail(BusError.FromException(exception).Message); }
                    Status = MiniAppSaveStatus.Unknown;
                    Error = "Исход сохранения пока не установлен. Повторите проверку: будет использован тот же пакет.";
                    Changed?.Invoke(); return new(false, Status, Error);
                }
            }
            try
            {
                await _participant.AcknowledgeAsync(_pending, _committed!, _sentRevision, ct);
                _acknowledged = _sentRevision; _pending = null; _committed = null;
                Status = MiniAppSaveStatus.Saved; Error = null; Changed?.Invoke(); return new(true, Status);
            }
            catch (Exception exception)
            {
                // SQL результат известен; повтор завершит локальное подтверждение без второй записи.
                return Fail("Данные сохранены, но черновик не подтверждён: " + exception.Message);
            }
        }
        finally { _operation.Release(); }
    }
    // Discard не может скрыть ещё выполняющуюся или неустановленную запись.
    public async Task DiscardAsync(CancellationToken ct = default)
    {
        await _operation.WaitAsync(ct);
        try
        {
            if (_pending != null) throw new InvalidOperationException("Сначала установите исход сохранения.");
            if (_participant == null) throw new InvalidOperationException("Владелец черновика не зарегистрирован.");
            var discardedRevision = _revision;
            await _participant.DiscardAsync(ct); _acknowledged = discardedRevision;
            Status = MiniAppSaveStatus.Idle; Error = null; Changed?.Invoke();
        }
        finally { _operation.Release(); }
    }
    // Отказ оставляет dirty и данные участника без изменений.
    private MiniAppSaveAttempt Fail(string message)
    { Status = MiniAppSaveStatus.Failed; Error = message; Changed?.Invoke(); return new(false, Status, message); }
}

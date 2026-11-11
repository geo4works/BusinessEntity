namespace BusinessEntity.Infrastructure.Bus;

// Изолирует адресацию HTTP/circuit scope на общем транспорте.
public sealed class BusRequestScope
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid? UserId { get; private set; }
    public bool IsSystem { get; private set; }
    public bool HasApplicationAdminAuthority { get; private set; }
    public BusExecutionKind ExecutionKind { get; private set; }
    public Guid LogicalWindowId => ExecutionKind == BusExecutionKind.Ui ? Id : Guid.Empty;
    public long AuthEpoch { get; private set; }
    public Guid? ApiSessionId { get; private set; }
    public string? ApiClientId { get; private set; }
    public Guid? ApiOperationId {get;private set;}
    public string? ApiRequestHash {get;private set;}
    public DateTime? ApiAccessExpiresAt{get;private set;}
    private readonly AsyncLocal<Guid?> _activationPermit = new();
    private readonly AsyncLocal<SourceAddress?> _source = new();
    public Guid? ModuleId => _source.Value?.ModuleId;
    public Guid? EditSessionId => _source.Value?.EditSessionId;
    public Guid? ContextId => _source.Value?.ContextId;

    // Ограничивает источник одной async-операцией, не перезаписывая адрес параллельного компонента.
    public IDisposable Enter(Guid? moduleId, Guid? editSessionId, Guid? contextId)
    {
        var previous = _source.Value; _source.Value = new(moduleId, editSessionId, contextId);
        return new SourceLease(() => _source.Value = previous);
    }
    // Неизменяемый адрес текущей async-ветки.
    private sealed record SourceAddress(Guid? ModuleId, Guid? EditSessionId, Guid? ContextId);
    // Восстанавливает вложенный источник после команды.
    private sealed class SourceLease : IDisposable
    {
        private Action? _release;
        // Сохраняет прежний адрес до завершения команды.
        public SourceLease(Action release) => _release = release;
        // Повторное освобождение безопасно.
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    // Создаёт неизменяемый адрес запроса с установленным сервером автором.
    public BusRequestContext Snapshot() => new(Id, UserId, LogicalWindowId, ModuleId, EditSessionId, ContextId, IsSystem, HasApplicationAdminAuthority, AuthEpoch, ExecutionKind, ApiSessionId, ApiClientId, _activationPermit.Value,ApiOperationId,ApiRequestHash,ApiAccessExpiresAt);

    // Запоминает материализованного пользователя только в текущем scope.
    public void SetUser(Guid? userId)
    { if (UserId != userId) HasApplicationAdminAuthority = false; UserId = userId; }
    // Только серверный владелец identity подтверждает полномочие; данные браузера сюда не копируются.
    public void SetApplicationAdminAuthority(bool value) => HasApplicationAdminAuthority = UserId.HasValue && value;

    // Помечает явный стартовый/системный контекст без пользователя браузера.
    public void MarkSystem() { IsSystem = true; ExecutionKind = BusExecutionKind.Background; }
    // Эпоха берётся только из проверенного сервером principal.
    public void SetAuthenticationEpoch(long value) => AuthEpoch = value;
    // API-запрос получает свой scope; продолжительная сессия не подменяет идентичность request.
    public void SetApiContext(Guid sessionId, string clientId) { ExecutionKind = BusExecutionKind.Api; ApiSessionId = sessionId; ApiClientId = clientId; }
    public void SetApiAccessExpiry(DateTime? expiry)=>ApiAccessExpiresAt=expiry;
    // Только User вызывает после проверки сохранённого authorization context через собственную SQL policy.
    internal void BeginBackgroundApi(){IsSystem=false;HasApplicationAdminAuthority=false;ExecutionKind=BusExecutionKind.Api;}
    // HTTP API не изображает окно даже до открытия своей security session.
    public void MarkApiRequest()=>ExecutionKind=BusExecutionKind.Api;
    // Только transport executor привязывает canonical write к принятой typed API операции.
    public void SetApiOperation(Guid id,string hash){ApiOperationId=id;ApiRequestHash=hash;}
    // Единственный коммит активации использует capability drain lease, которую браузер не получает.
    public IDisposable EnterActivation(Guid permit)
    { var old = _activationPermit.Value; _activationPermit.Value = permit; return new SourceLease(() => _activationPermit.Value = old); }

    // Отбрасывает запрос чужого окна или HTTP scope до выполнения работы.
    public bool Owns(BusRequest request) => request.Context.ScopeId == Id && request.Context.LogicalWindowId == LogicalWindowId;
}

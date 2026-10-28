namespace BusinessEntity.Infrastructure.Bus;

// Изолирует адресацию HTTP/circuit scope на общем транспорте.
public sealed class BusRequestScope
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid? UserId { get; private set; }
    public bool IsSystem { get; private set; }
    public bool HasApplicationAdminAuthority { get; private set; }
    public Guid LogicalWindowId => Id;
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
    public BusRequestContext Snapshot() => new(Id, UserId, LogicalWindowId, ModuleId, EditSessionId, ContextId, IsSystem, HasApplicationAdminAuthority);

    // Запоминает материализованного пользователя только в текущем scope.
    public void SetUser(Guid? userId)
    { if (UserId != userId) HasApplicationAdminAuthority = false; UserId = userId; }
    // Только серверный владелец identity подтверждает полномочие; данные браузера сюда не копируются.
    public void SetApplicationAdminAuthority(bool value) => HasApplicationAdminAuthority = UserId.HasValue && value;

    // Помечает явный стартовый/системный контекст без пользователя браузера.
    public void MarkSystem() => IsSystem = true;

    // Отбрасывает запрос чужого окна или HTTP scope до выполнения работы.
    public bool Owns(BusRequest request) => request.Context.ScopeId == Id && request.Context.LogicalWindowId == LogicalWindowId;
}

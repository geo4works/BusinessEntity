namespace BusinessEntity.Infrastructure.Bus;

// Изолирует адресацию HTTP/circuit scope на общем транспорте.
public sealed class BusRequestScope
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid? UserId { get; private set; }
    public bool IsSystem { get; private set; }
    public Guid LogicalWindowId => Id;
    public Guid? ModuleId { get; private set; }
    public Guid? EditSessionId { get; private set; }
    public Guid? ContextId { get; private set; }

    // Привязывает серверный источник команды к модулю и его контексту сохранения.
    public void SetSource(Guid? moduleId, Guid? editSessionId, Guid? contextId)
    { ModuleId = moduleId; EditSessionId = editSessionId; ContextId = contextId; }

    // Создаёт неизменяемый адрес запроса с установленным сервером автором.
    public BusRequestContext Snapshot() => new(Id, UserId, LogicalWindowId, ModuleId, EditSessionId, ContextId, IsSystem);

    // Запоминает материализованного пользователя только в текущем scope.
    public void SetUser(Guid? userId) => UserId = userId;

    // Помечает явный стартовый/системный контекст без пользователя браузера.
    public void MarkSystem() => IsSystem = true;

    // Отбрасывает запрос чужого окна или HTTP scope до выполнения работы.
    public bool Owns(BusRequest request) => request.Context.ScopeId == Id && request.Context.LogicalWindowId == LogicalWindowId;
}

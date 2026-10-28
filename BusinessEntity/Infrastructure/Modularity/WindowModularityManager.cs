using BusinessEntity.Infrastructure.Selection;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.WebLogger.Services;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Modularity;

// Управляет UI-модулями и рабочими экземплярами одного server circuit, без внутренних вкладок.
public sealed class WindowModularityManager : IAsyncDisposable
{
    private readonly ModuleCatalog _catalog;
    private readonly BusRequestScope _scope;
    private readonly ISaveBatchTransport _transport;
    private readonly IModuleAccessPolicy _access;
    private readonly IMessageBus _bus;
    private readonly IWebLoggerService? _logger;
    private readonly List<MiniAppModule> _modules = [];
    private readonly List<MiniAppSaveContext> _contexts = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private readonly HashSet<Guid> _inactive = [];
    private long _selectionSourceRevision;
    public SelectionSourceAddress? CurrentSelectionSource { get; private set; }
    // Менеджер хранит только адрес master; предметные снимки передаются исключительно по шине.
    public SelectionSourceAddress ActivateSelectionSource(Guid moduleId)
    {
        GetLive(moduleId);
        if (CurrentSelectionSource?.OriginModuleId == moduleId) return CurrentSelectionSource;
        CurrentSelectionSource = new(moduleId, ++_selectionSourceRevision);
        _bus.SendMessage(new SelectionSourceChanged(LogicalWindowId, CurrentSelectionSource));
        return CurrentSelectionSource;
    }
    // Старый master не может очистить адрес уже выбранного нового источника.
    public void ClearSelectionSource(Guid moduleId)
    {
        if (CurrentSelectionSource?.OriginModuleId != moduleId) return;
        CurrentSelectionSource = null; ++_selectionSourceRevision;
        _bus.SendMessage(new SelectionSourceChanged(LogicalWindowId, null));
    }
    public event Action? Changed;
    public Guid LogicalWindowId => _scope.LogicalWindowId;
    public IReadOnlyList<MiniAppModule> Modules => _modules.ToArray();
    public IReadOnlyList<MiniAppSaveContext> DirtyContexts => _contexts.Where(x => x.IsDirty).ToArray();
    // Шесть зависимостей: каталог, адрес, сохранение, доступ, события и внешний lifecycle-журнал.
    public WindowModularityManager(ModuleCatalog catalog, BusRequestScope scope, ISaveBatchTransport transport,
        IModuleAccessPolicy access, IMessageBus bus, IWebLoggerService? logger = null)
    { _catalog = catalog; _scope = scope; _transport = transport; _access = access; _bus = bus; _logger = logger; }
    // Только менеджер создаёт UI ModuleId; повтор определяется политикой вида модуля.
    public async Task<MiniAppModule> OpenAsync(string key, Guid? contextId = null, Guid? editSessionId = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var definition = _catalog.Get(key);
            if (definition.Placement == ModulePlacement.Headless) throw new InvalidOperationException("Фоновые модули создаёт каталог.");
            if (!await _access.CanOpenAsync(definition, contextId, ct)) throw new UnauthorizedAccessException();
            if (definition.InstancePolicy == ModuleInstancePolicy.PerContext && contextId == null)
                throw new ArgumentException("Для модуля PerContext обязателен ContextId.");
            var existing = definition.InstancePolicy == ModuleInstancePolicy.MultiInstance ? null : _modules.FirstOrDefault(x =>
                x.Definition.Key == key && (definition.InstancePolicy == ModuleInstancePolicy.WindowSingleton || x.ContextId == contextId));
            if (existing != null)
            { if (_inactive.Remove(existing.ModuleId)) Publish(existing, ModuleLifecycle.Activated); return existing; }
            MiniAppSaveContext? save = null;
            if (editSessionId.HasValue)
            {
                save = _contexts.SingleOrDefault(x => x.EditSessionId == editSessionId)
                    ?? throw new KeyNotFoundException("Контекст сохранения другого окна или закрыт.");
                if (save.MiniAppId != definition.MiniAppId || save.ContextId != contextId)
                    throw new InvalidOperationException("Компонент не принадлежит рабочему экземпляру.");
            }
            else if (definition.HasSaveContext)
            {
                save = definition.InstancePolicy == ModuleInstancePolicy.MultiInstance ? null :
                    _contexts.FirstOrDefault(x => x.MiniAppId == definition.MiniAppId && x.ContextId == contextId);
                if (save == null)
                {
                    save = new(definition.MiniAppId, contextId, _scope, _transport);
                    save.Changed += OnChanged; _contexts.Add(save);
                }
            }
            var module = new MiniAppModule(Guid.NewGuid(), definition, LogicalWindowId, contextId, save);
            _modules.Add(module); Publish(module, ModuleLifecycle.Opened); Publish(module, ModuleLifecycle.Activated); OnChanged(); return module;
        }
        finally { _gate.Release(); }
    }
    // Проверяет текущий адрес перед исполнением поздней команды UI.
    public MiniAppModule GetLive(Guid moduleId) => !_disposed ? _modules.SingleOrDefault(x => x.ModuleId == moduleId && !_inactive.Contains(x.ModuleId))
        ?? throw new KeyNotFoundException("Модуль уже закрыт.") : throw new ObjectDisposedException(nameof(WindowModularityManager));
    // Приёмник оболочки проверяет право до создания скрытого UI-модуля.
    public Task<bool> CanOpenDefinitionAsync(ModuleDefinition definition, Guid? contextId = null)
        => _access.CanOpenAsync(definition, contextId, default);
    // Право конкретной команды проверяется сервером заново при выполнении.
    public Task<bool> CanExecuteAsync(Guid moduleId, string? permission, Guid? permissionContextId = null)
    {
        var module = GetLive(moduleId);
        return _access.CanOpenAsync(module.Definition with { RequiredPermission = permission }, permissionContextId ?? module.ContextId, default);
    }
    // Запускает команду в async-local адресе конкретного живого экземпляра.
    public async Task ExecuteAsync(Guid moduleId, Func<MiniAppModule, Task> command)
    {
        var module = GetLive(moduleId);
        if (!await _access.CanOpenAsync(module.Definition, module.ContextId, default)) throw new UnauthorizedAccessException();
        using var address = _scope.Enter(module.ModuleId, module.EditSessionId, module.ContextId);
        await command(module);
    }
    // Снятие UI сохраняет последний dirty runtime, но его прежний адрес не принимает команды.
    public async Task ReleaseViewAsync(Guid moduleId)
    {
        var module = _modules.SingleOrDefault(x => x.ModuleId == moduleId);
        if (module == null) return;
        if (module.IsDirty && !_modules.Any(x => x.ModuleId != moduleId && x.SaveContext == module.SaveContext))
        { if (_inactive.Add(moduleId)) { ClearSelectionSource(moduleId); Publish(module, ModuleLifecycle.Deactivated); } return; }
        await CloseAsync(moduleId);
    }
    // Закрытие вспомогательного компонента не удаляет общий черновик; последний dirty-владелец требует guard.
    public async Task CloseAsync(Guid moduleId, bool windowDisposal = false)
    {
        await _gate.WaitAsync();
        try
        {
            var module = _modules.SingleOrDefault(x => x.ModuleId == moduleId); if (module == null) return;
            var last = module.SaveContext != null && !_modules.Any(x => x.ModuleId != moduleId && x.SaveContext == module.SaveContext);
            if (last && module.IsDirty && !windowDisposal) throw new InvalidOperationException("Несохранённый рабочий экземпляр требует Save/Discard/Cancel.");
            ClearSelectionSource(moduleId);
            if (!_inactive.Remove(moduleId)) Publish(module, ModuleLifecycle.Deactivated);
            _modules.Remove(module); Publish(module, ModuleLifecycle.Closed);
            if (last && module.SaveContext != null)
            { module.SaveContext.Changed -= OnChanged; _contexts.Remove(module.SaveContext); }
            OnChanged();
        }
        finally { _gate.Release(); }
    }
    // Адрес жизненного цикла и веб-журнал относятся к тому же логическому окну.
    private void Publish(MiniAppModule module, ModuleLifecycle lifecycle)
    {
        _bus.SendMessage(new ModuleLifecycleChanged(module.ModuleId, module.Definition.MiniAppId,
            module.Definition.ModuleDefinitionId, module.LogicalWindowId, module.ContextId, module.EditSessionId, lifecycle));
        if (_logger != null) _ = _logger.Information($"[modules] {lifecycle} module={module.ModuleId} definition={module.Definition.Key} window={LogicalWindowId}");
    }
    // Сообщает хостам и guard об изменении их собственного окна.
    private void OnChanged() => Changed?.Invoke();
    // Circuit удерживается при reconnect; финальный disposal закрывает все адреса и освобождает подписчиков.
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var module in _modules.ToArray()) await CloseAsync(module.ModuleId, windowDisposal: true);
        Changed = null;
    }
}

using System.Reactive.Linq;
using BusinessEntity.Infrastructure.Bus;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Modularity;

// Собирает декларации по общей шине и владеет scope фоновых модулей.
public sealed class ModuleCatalog : IHostedService, IAsyncDisposable
{
    private readonly IMessageBus _bus;
    private readonly IServiceScopeFactory _scopes;
    private readonly MiniAppHostService[] _hosts;
    private readonly Dictionary<string, ModuleRuntimeRegistration> _runtimes;
    private readonly ILogger<ModuleCatalog> _logger;
    private readonly Dictionary<string, ModuleDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly List<(AsyncServiceScope Scope, IModuleRuntime Runtime, MiniAppModule Module)> _running = [];
    private readonly HashSet<string> _received = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _collected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private IDisposable? _subscription;
    private bool _started;
    private bool _stopping;
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly BusinessEntity.WebLogger.Services.IWebLoggerService? _webLogger;
    public event Action? DefinitionsChanged;
    // Runtime factories задаёт Registration владельца; каталог не знает конкретных миниаппов.
    public ModuleCatalog(IMessageBus bus, IServiceScopeFactory scopes, IEnumerable<MiniAppHostService> hosts,
        IEnumerable<ModuleRuntimeRegistration> runtimes, ILogger<ModuleCatalog> logger, BusinessEntity.WebLogger.Services.IWebLoggerService? webLogger = null)
    { _bus = bus; _scopes = scopes; _hosts = hosts.ToArray(); _runtimes = runtimes.ToDictionary(x => x.DefinitionKey); _logger = logger; _webLogger = webLogger; }
    // Возвращает отдельный упорядоченный список, включая поздние объявления.
    public IReadOnlyList<ModuleDefinition> Definitions
    { get { lock (_gate) return _definitions.Values.OrderBy(x => x.Order).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray(); } }
    // Неизвестное определение приводит к явному отказу открытия.
    public ModuleDefinition Get(string key)
    { lock (_gate) return _definitions.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"Модуль '{key}' не объявлен."); }
    // Проверяет владельца, дубликаты и допустимое сочетание placement/instance policy.
    public void Accept(HereAreModuleDefinitions response)
    {
        lock (_gate)
        {
            foreach (var definition in response.Definitions)
            {
                if (definition.MiniAppId != response.MiniAppId || string.IsNullOrWhiteSpace(definition.ModuleDefinitionId))
                    throw new InvalidOperationException("Неверный владелец декларации модуля.");
                if ((definition.InstancePolicy == ModuleInstancePolicy.AppSingleton) != (definition.Placement == ModulePlacement.Headless)
                    || (definition.Placement == ModulePlacement.Headless && definition.HasSaveContext))
                    throw new InvalidOperationException("AppSingleton допускается только для фонового модуля без пользовательского черновика.");
                if (definition.Placement != ModulePlacement.Headless && string.IsNullOrWhiteSpace(definition.ComponentKey))
                    throw new InvalidOperationException("Для UI-модуля обязателен ComponentKey.");
                if (definition.Placement == ModulePlacement.ShellModuleHostArea && string.IsNullOrWhiteSpace(definition.HostArea))
                    throw new InvalidOperationException("Для модуля оболочки обязательно именованное место.");
                if (_definitions.TryGetValue(definition.Key, out var previous) && previous != definition)
                    throw new InvalidOperationException($"Повторное определение модуля '{definition.Key}'.");
                _definitions[definition.Key] = definition;
            }
            _received.Add(response.MiniAppId);
            if (_hosts.All(x => _received.Contains(x.MiniAppId))) _collected.TrySetResult();
        }
        DefinitionsChanged?.Invoke();
        if (_started && !_stopping) _ = StartLateRuntimesAsync();
    }
    // Подписка ответа живёт постоянно; поздний ответ обновляет каталог и места оболочки.
    public async Task CollectAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_subscription == null)
            {
                if (_hosts.Select(x => x.MiniAppId).Distinct().Count() != _hosts.Length)
                    throw new InvalidOperationException("Один миниапп зарегистрировал несколько host-service.");
                _subscription = _bus.Listen<HereAreModuleDefinitions>().Subscribe(Accept);
                foreach (var host in _hosts) host.Start();
                if (_hosts.Length == 0) _collected.TrySetResult();
                _bus.SendMessage(new GiveMeModuleDefinitions(Guid.NewGuid()));
            }
        }
        await _collected.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
    }
    // Startup последователен: storage, identity bootstrap, defaults, seed и фоновые задачи.
    public async Task StartAsync(CancellationToken ct)
    {
        if (_started) return;
        _stopping = false;
        await CollectAsync(ct);
        await StartPendingRuntimesAsync(ct);
        _started = true;
    }
    // Поздние headless-декларации также создаются каталогом, один раз и последовательно.
    private async Task StartLateRuntimesAsync()
    {
        try { await StartPendingRuntimesAsync(CancellationToken.None); }
        catch (Exception exception) { _logger.LogError(exception, "Поздний runtime не запущен."); if (_webLogger != null) await _webLogger.Error(exception); }
    }
    // Одна сериализация защищает AppSingleton от повторных HereAreModuleDefinitions.
    private async Task StartPendingRuntimesAsync(CancellationToken ct)
    {
        await _runtimeGate.WaitAsync(ct);
        try
        {
            if (_stopping) return;
            foreach (var definition in Definitions.Where(x => x.Placement == ModulePlacement.Headless && x.OpenOnStartup))
                if (!_running.Any(x => x.Module.Definition.Key == definition.Key)) await StartRuntimeAsync(definition, ct);
        }
        finally { _runtimeGate.Release(); }
    }
    // Только каталог создаёт AppSingleton runtime и удерживает его scope до Stop.
    private async Task StartRuntimeAsync(ModuleDefinition definition, CancellationToken ct)
    {
        if (!_runtimes.TryGetValue(definition.Key, out var registration))
            throw new InvalidOperationException($"Не задан runtime фонового модуля '{definition.Key}'.");
        var scope = _scopes.CreateAsyncScope();
        try
        {
            var source = scope.ServiceProvider.GetRequiredService<BusRequestScope>(); source.MarkSystem();
            var module = new MiniAppModule(Guid.NewGuid(), definition, Guid.Empty, null, null);
            using var address = source.Enter(module.ModuleId, null, null);
            var runtime = registration.Create(scope.ServiceProvider);
            await runtime.StartAsync(ct);
            _running.Add((scope, runtime, module)); Publish(module, ModuleLifecycle.Opened); Publish(module, ModuleLifecycle.Activated);
        }
        catch { await scope.DisposeAsync(); throw; }
    }
    // Фоновые модули останавливаются в обратном порядке зависимостей.
    public async Task StopAsync(CancellationToken ct)
    {
        _stopping = true;
        await _runtimeGate.WaitAsync(ct);
        var errors = new List<Exception>();
        try
        {
            for (var i = _running.Count - 1; i >= 0; i--)
            {
                var entry = _running[i]; Publish(entry.Module, ModuleLifecycle.Deactivated);
                try { await entry.Runtime.StopAsync(ct); }
                catch (Exception exception) { errors.Add(exception); }
                finally { Publish(entry.Module, ModuleLifecycle.Closed); await entry.Scope.DisposeAsync(); }
            }
            _running.Clear(); _started = false;
        }
        finally { _runtimeGate.Release(); }
        if (errors.Count > 0) throw new AggregateException("Ошибка завершения модулей.", errors);
    }

    // Общие lifecycle events не передают экземпляр runtime другим владельцам.
    private void Publish(MiniAppModule module, ModuleLifecycle lifecycle)
    {
        _logger.LogInformation("Module {ModuleId} {Definition} {Lifecycle}", module.ModuleId, module.Definition.Key, lifecycle);
        if (_webLogger != null) _ = _webLogger.Information($"[modules] {lifecycle} module={module.ModuleId} definition={module.Definition.Key}");
        _bus.SendMessage(new ModuleLifecycleChanged(module.ModuleId, module.Definition.MiniAppId,
            module.Definition.ModuleDefinitionId, module.LogicalWindowId, module.ContextId, module.EditSessionId, lifecycle));
    }
    // Закрывает подписку и оставшиеся runtime при незавершённом startup.
    public async ValueTask DisposeAsync()
    { _subscription?.Dispose(); _subscription = null; await StopAsync(CancellationToken.None); }
}

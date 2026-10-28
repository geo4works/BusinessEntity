using System.Reactive.Disposables;
using System.Reactive.Linq;
using BusinessEntity.Infrastructure.Modularity;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Selection;

// Каждый helper имеет собственный снимок; общего изменяемого selection service нет.
public sealed class SelectionObserver : IDisposable
{
    private readonly IMessageBus _bus;
    private readonly WindowModularityManager _manager;
    private readonly Guid _helperModuleId;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly object _gate = new();
    private Guid _requestId;
    private bool _disposed;
    private ObjectInContextSelected? _snapshot;
    public ObjectInContextSelected? Snapshot { get { lock (_gate) return _snapshot; } }
    public event Action? Changed;
    // Подписывается до запроса последнего значения и проверяет адрес helper.
    public SelectionObserver(IMessageBus bus, WindowModularityManager manager, Guid helperModuleId)
    {
        _bus = bus; _manager = manager; _helperModuleId = manager.GetLive(helperModuleId).ModuleId;
        _subscriptions.Add(bus.Listen<ObjectInContextSelected>().Subscribe(Accept));
        _subscriptions.Add(bus.Listen<HereIsCurrentSelection>().Subscribe(reply =>
        { if (reply.RequestId == _requestId) Accept(reply.Snapshot); }));
        _subscriptions.Add(bus.Listen<SelectionSourceChanged>().Subscribe(SourceChanged));
        _subscriptions.Add(bus.Listen<ModuleLifecycleChanged>().Subscribe(LifecycleChanged));
        RequestCurrent();
    }
    // Окно, активный источник, его поколение и liveness проверяются раньше содержимого.
    private void Accept(ObjectInContextSelected message)
    {
        lock (_gate)
        {
            if (_disposed || message.LogicalWindowId != _manager.LogicalWindowId ||
                _manager.CurrentSelectionSource != new SelectionSourceAddress(message.OriginModuleId, message.SourceRevision)) return;
            try { _manager.GetLive(_helperModuleId); _manager.GetLive(message.OriginModuleId); }
            catch (Exception error) when (error is KeyNotFoundException or ObjectDisposedException) { return; }
            if (message.SelectionRevision <= 0 || message.Objects.IsDefault) return;
            if (_snapshot is { } previous && previous.OriginModuleId == message.OriginModuleId &&
                previous.SourceRevision == message.SourceRevision && previous.SelectionRevision >= message.SelectionRevision) return;
            _snapshot = message;
        }
        Changed?.Invoke();
    }
    // Новый источник немедленно убирает прежнюю информацию, затем запрашивается его актуальное значение.
    private void SourceChanged(SelectionSourceChanged message)
    {
        if (message.LogicalWindowId != _manager.LogicalWindowId || message.Source != _manager.CurrentSelectionSource) return;
        lock (_gate) { if (_disposed) return; _snapshot = null; }
        Changed?.Invoke(); RequestCurrent();
    }
    // Закрытый helper больше не принимает события даже до disposal Razor-компонента.
    private void LifecycleChanged(ModuleLifecycleChanged message)
    {
        if (message.LogicalWindowId == _manager.LogicalWindowId && message.ModuleId == _helperModuleId &&
            message.Lifecycle is ModuleLifecycle.Closed or ModuleLifecycle.Deactivated) Dispose();
    }
    // Запрос содержит адрес живого helper, ответ коррелирован с его последним запросом.
    private void RequestCurrent()
    {
        if (_disposed) return;
        _requestId = Guid.NewGuid(); _bus.SendMessage(new GiveMeCurrentSelection(_requestId, _manager.LogicalWindowId, _helperModuleId));
    }
    // Освобождает все подписки и локальные значения завершённого helper.
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _snapshot = null; Changed = null; }
        _subscriptions.Dispose();
    }
}

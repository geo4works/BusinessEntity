using System.Collections.Immutable;
using System.Reactive.Linq;
using BusinessEntity.Infrastructure.Modularity;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Selection;

// Один master хранит свой последний снимок и отвечает поздним helpers по шине.
public sealed class SelectionPublisher : IDisposable
{
    private readonly IMessageBus _bus;
    private readonly WindowModularityManager _manager;
    private readonly Guid _moduleId;
    private readonly IDisposable _requests;
    private ObjectInContextSelected? _latest;
    private long _revision;
    private bool _disposed;
    // Источник создаётся только для живого модуля своего окна.
    public SelectionPublisher(IMessageBus bus, WindowModularityManager manager, Guid moduleId)
    {
        _bus = bus; _manager = manager; _moduleId = manager.GetLive(moduleId).ModuleId;
        _requests = bus.Listen<GiveMeCurrentSelection>().Subscribe(Reply);
    }
    // Вызывается владельцем после изменения локального выделения и постановки его отрисовки.
    public void Publish(IEnumerable<SelectedObjectSnapshot> objects)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var values = objects.ToImmutableArray();
        var source = _manager.ActivateSelectionSource(_moduleId);
        _latest = new(_manager.LogicalWindowId, _moduleId, Interlocked.Increment(ref _revision), source.SourceRevision, values);
        _bus.SendMessage(_latest);
    }
    // Адрес и жизненный цикл проверяются до чтения собственного снимка.
    private void Reply(GiveMeCurrentSelection request)
    {
        if (_disposed || request.LogicalWindowId != _manager.LogicalWindowId ||
            _manager.CurrentSelectionSource?.OriginModuleId != _moduleId) return;
        try { _manager.GetLive(request.RequesterModuleId); _manager.GetLive(_moduleId); }
        catch (Exception error) when (error is KeyNotFoundException or ObjectDisposedException) { return; }
        if (_latest is { } snapshot) _bus.SendMessage(new HereIsCurrentSelection(request.RequestId, snapshot));
    }
    // Завершение UI снимает подписку и инвалидирует только его собственный адрес master.
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _requests.Dispose();
        _manager.ClearSelectionSource(_moduleId); _latest = null;
    }
}

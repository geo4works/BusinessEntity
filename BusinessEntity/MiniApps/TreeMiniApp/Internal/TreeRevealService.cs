using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Modularity;
namespace BusinessEntity.MiniApps.TreeMiniApp.Internal;

// Связывает запрос показа узла только с собственным Tree UI, включая позднее создание дерева.
internal sealed class TreeRevealService(WindowModularityManager manager)
{
    private Func<Guid?, Task>? _apply;
    private Guid? _sourceId;
    public Guid? RequestedEntityId { get; private set; }
    // Сохраняет только намерение навигации, а не общий контекст выделенных объектов.
    public async Task RevealAsync(Guid? entityId, BusRequestContext address)
    {
        var source = manager.GetLive(address.ModuleId ?? throw new ArgumentException("Нужен адрес модуля страницы."));
        if (source.LogicalWindowId != address.LogicalWindowId || source.ContextId != address.ContextId ||
            source.EditSessionId != address.EditSessionId) throw new ArgumentException("Адрес страницы устарел.");
        _sourceId = source.ModuleId; RequestedEntityId = entityId;
        if (_apply != null) await _apply(entityId);
    }
    // Пересоздание UI не применяет позднее намерение уже закрытой страницы.
    public Guid? GetLiveRequestedEntityId()
    {
        if (!_sourceId.HasValue) return null;
        try { manager.GetLive(_sourceId.Value); return RequestedEntityId; }
        catch (Exception error) when (error is KeyNotFoundException or ObjectDisposedException) { return null; }
    }
    // Регистрирует собственный renderer; старый компонент не снимает обработчик нового.
    public IDisposable Attach(Func<Guid?, Task> apply)
    { _apply = apply; return new Binding(() => { if (_apply == apply) _apply = null; }); }
    // Снимает callback своего UI без предметных сообщений наружу.
    private sealed class Binding(Action release) : IDisposable { public void Dispose() => release(); }
}

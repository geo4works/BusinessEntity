using BusinessEntity.Infrastructure.Bus;
using Microsoft.AspNetCore.Components;

namespace BusinessEntity.Infrastructure.Modularity;

// Адресует события UI его живому модулю, не меняя источник параллельного компонента.
public abstract class ModuleComponentBase : ComponentBase, IHandleEvent
{
    [CascadingParameter] public MiniAppModule? Module { get; set; }
    [Inject] public WindowModularityManager ModuleManager { get; set; } = default!;
    [Inject] public BusRequestScope RequestScope { get; set; } = default!;
    // Повторяет renderer lifecycle ComponentBase внутри адресной async-операции.
    async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? arg)
    {
        using var edit = arg is ChangeEventArgs ? ModuleManager.TrackEditEvent() : null;
        if (arg is not ChangeEventArgs) await ModuleManager.WaitForPendingEditsAsync();
        Task task = Module == null ? callback.InvokeAsync(arg) : ModuleManager.ExecuteAsync(Module.ModuleId, _ => callback.InvokeAsync(arg));
        StateHasChanged();
        if (task.IsCompletedSuccessfully) return;
        try { await task; }
        catch (OperationCanceledException) when (task.IsCanceled) { return; }
        StateHasChanged();
    }
    // JS callbacks владельца могут явно открыть тот же адрес, не используя браузерные идентификаторы.
    protected IDisposable EnterModuleAddress() => RequestScope.Enter(Module?.ModuleId, Module?.EditSessionId, Module?.ContextId);
}

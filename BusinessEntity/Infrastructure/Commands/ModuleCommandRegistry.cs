using System.Collections.Concurrent;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Modularity;
using Microsoft.AspNetCore.Components;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Commands;

// Scoped runtime bindings отдельно от деклараций: обработчик предоставляет владелец живого UI.
public sealed class ModuleCommandRegistry
{
    private readonly ConcurrentDictionary<(Guid, string, string, Guid), Binding> _bindings = new();
    private readonly WindowModularityManager _manager;
    private readonly IMessageBus _bus;
    // Реестр принадлежит одному circuit и проверяет существование адреса у менеджера.
    public ModuleCommandRegistry(WindowModularityManager manager, IMessageBus bus) { _manager = manager; _bus = bus; }
    // Непредметный holder не передаётся по шине.
    private sealed record Binding(Guid LeaseId, ButtonDefinition Definition, ButtonState State, Func<Task> Execute, Guid? PermissionContextId);
    // Владелец обновляет состояние команды и свой callback до отрисовки.
    public IDisposable Bind(MiniAppModule module, ButtonDefinition definition, ButtonState state, Func<Task> execute, Guid bindingId, Guid? permissionContextId = null)
    {
        var key = (module.ModuleId, definition.MiniAppId, definition.CommandId, bindingId); var lease = bindingId;
        if ((definition.ExecutionMiniAppId ?? definition.MiniAppId) != module.Definition.MiniAppId)
            throw new InvalidOperationException("Обработчик команды принадлежит другому миниаппу.");
        _manager.GetLive(module.ModuleId);
        _bindings[key] = new(lease, definition, state, execute, permissionContextId);
        _bus.SendMessage(new ButtonStateSnapshot(module.LogicalWindowId, module.ModuleId, module.EditSessionId,
            module.ContextId, module.Definition.MiniAppId, bindingId, new System.Collections.ObjectModel.ReadOnlyDictionary<string, ButtonState>(new Dictionary<string, ButtonState> { [definition.CommandId] = state })));
        return new BindingLease(() => { if (_bindings.TryGetValue(key, out var binding) && binding.LeaseId == lease) _bindings.TryRemove(key, out _); });
    }
    // Неактивные, снятые, скрытые или запрещённые команды не исполняются даже при подделке browser event.
    public async Task InvokeAsync(BusRequestContext address, string miniAppId, string commandId, Guid bindingId)
    {
        var module = _manager.GetLive(address.ModuleId ?? throw new ArgumentException("Не задан ModuleId команды."));
        if (address.LogicalWindowId != module.LogicalWindowId || address.EditSessionId != module.EditSessionId || address.ContextId != module.ContextId)
            throw new UnauthorizedAccessException();
        if (!_bindings.TryGetValue((module.ModuleId, miniAppId, commandId, bindingId), out var binding)) throw new KeyNotFoundException("Обработчик команды снят.");
        if (binding.LeaseId != bindingId || !binding.State.Visible || !binding.State.Enabled) throw new UnauthorizedAccessException();
        if (!await _manager.CanExecuteAsync(module.ModuleId, binding.Definition.RequiredPermission, binding.PermissionContextId)) throw new UnauthorizedAccessException();
        if (!_bindings.TryGetValue((module.ModuleId, miniAppId, commandId, bindingId), out var current) || current.LeaseId != bindingId || !current.State.Visible || !current.State.Enabled)
            throw new KeyNotFoundException("Контекст команды изменился.");
        await _manager.ExecuteAsync(module.ModuleId, _ => current.Execute());
    }
    // Старый UI не может снять более новую регистрацию той же команды.
    private sealed class BindingLease : IDisposable
    {
        private Action? _release;
        // Удерживает только условное снятие своей записи.
        public BindingLease(Action release) => _release = release;
        // Идемпотентно снимает обработчик при disposal UI.
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

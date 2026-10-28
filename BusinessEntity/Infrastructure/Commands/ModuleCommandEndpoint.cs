using BusinessEntity.Infrastructure.Bus;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Commands;

// Единственный scoped получатель адресных UI-команд своего окна.
public sealed class ModuleCommandEndpoint : ScopedBusEndpoint
{
    private readonly ModuleCommandRegistry _commands;
    private readonly BusRequestScope _scope;
    // Общий transport фильтрует окно прежде, чем обращаться к runtime binding.
    public ModuleCommandEndpoint(ModuleCommandRegistry commands, IMessageBus bus, BusRequestScope scope, ILogger<ModuleCommandEndpoint> logger) : base(bus, scope, logger) { _commands = commands; _scope = scope; }
    // Выполнение получает явный ответ; неизвестный адрес не исчезает молча.
    protected override void SubscribeRequests()
    {
        Subscribe<ButtonCommandInvoked, ButtonCommandResult>(async request =>
        {
            if (request.Context.UserId != _scope.UserId || request.Context.IsSystem != _scope.IsSystem) throw new UnauthorizedAccessException();
            await _commands.InvokeAsync(request.Context, request.DeclarationOwner, request.CommandId, request.BindingId); return new(request.RequestId); }, (id, error) => new(id, error));
    }
}

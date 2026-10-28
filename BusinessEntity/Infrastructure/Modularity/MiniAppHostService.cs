using BusinessEntity.Infrastructure.Commands;
using System.Reactive.Linq;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Modularity;

// Singleton одного владельца: только декларации, без чтения данных и команд.
public sealed class MiniAppHostService : IDisposable
{
    private readonly IMessageBus _bus;
    private readonly ModuleDefinition[] _definitions;
    private IDisposable? _subscription;
    private readonly ButtonDefinition[] _buttons;
    private IDisposable? _buttonsSubscription;
    public string MiniAppId { get; }
    // Копирует декларации владельца при создании его единственного host-service.
    public MiniAppHostService(IMessageBus bus, string miniAppId, IEnumerable<ModuleDefinition> definitions, IEnumerable<ButtonDefinition>? buttons = null)
    { _bus = bus; MiniAppId = miniAppId; _definitions = definitions.ToArray(); _buttons = buttons?.Select(button => button with { EntityTypes = button.EntityTypes == null ? null : Array.AsReadOnly(button.EntityTypes.ToArray()) }).ToArray() ?? []; }
    // Подписка устанавливается до запроса каталога и существует до завершения приложения.
    public void Start()
    {
        _buttonsSubscription ??= _bus.Listen<GiveMeButtons>().Subscribe(request =>
            _bus.SendMessage(new HereAreButtons(request.RequestId, MiniAppId, request.HostId,
                _buttons.Where(x => x.HostId == request.HostId).ToArray())));
        _subscription ??= _bus.Listen<GiveMeModuleDefinitions>().Subscribe(request =>
            _bus.SendMessage(new HereAreModuleDefinitions(request.RequestId, MiniAppId, Array.AsReadOnly(_definitions))));
    }
    // Освобождает единственную подписку владельца.
    public void Dispose() { _subscription?.Dispose(); _subscription = null; _buttonsSubscription?.Dispose(); _buttonsSubscription = null; }
}
// Локальная фабрика runtime отделена от передаваемых по шине деклараций.
public sealed record ModuleRuntimeRegistration(string DefinitionKey, Func<IServiceProvider, IModuleRuntime> Create);
// Порядок создания схемы и User storage передаётся между стартовыми runtime без знания EF.
public sealed class ApplicationStartupState
{
    public bool FreshEntityStorage { get; set; }
}

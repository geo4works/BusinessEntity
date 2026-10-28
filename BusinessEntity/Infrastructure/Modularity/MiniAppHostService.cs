using System.Reactive.Linq;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Modularity;

// Singleton одного владельца: только декларации, без чтения данных и команд.
public sealed class MiniAppHostService : IDisposable
{
    private readonly IMessageBus _bus;
    private readonly ModuleDefinition[] _definitions;
    private IDisposable? _subscription;
    public string MiniAppId { get; }
    // Копирует декларации владельца при создании его единственного host-service.
    public MiniAppHostService(IMessageBus bus, string miniAppId, IEnumerable<ModuleDefinition> definitions)
    { _bus = bus; MiniAppId = miniAppId; _definitions = definitions.ToArray(); }
    // Подписка устанавливается до запроса каталога и существует до завершения приложения.
    public void Start()
    {
        _subscription ??= _bus.Listen<GiveMeModuleDefinitions>().Subscribe(request =>
            _bus.SendMessage(new HereAreModuleDefinitions(request.RequestId, MiniAppId, Array.AsReadOnly(_definitions))));
    }
    // Освобождает единственную подписку владельца.
    public void Dispose() { _subscription?.Dispose(); _subscription = null; }
}
// Локальная фабрика runtime отделена от передаваемых по шине деклараций.
public sealed record ModuleRuntimeRegistration(string DefinitionKey, Func<IServiceProvider, IModuleRuntime> Create);
// Порядок создания схемы и User storage передаётся между стартовыми runtime без знания EF.
public sealed class ApplicationStartupState
{
    public bool FreshEntityStorage { get; set; }
}

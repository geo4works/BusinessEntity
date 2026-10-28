using System.Reactive.Linq;
using BusinessEntity.Infrastructure.Modularity;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Commands;

// Получает декларации приёмников по общей шине; текущие права проверяет window manager.
public sealed class ButtonCatalog : IDisposable
{
    private readonly IMessageBus _bus;
    private readonly MiniAppHostService[] _hosts;
    private readonly Dictionary<string, ButtonDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    // Подписка создаётся до первого GiveMeButtons и принимает поздние ответы.
    public ButtonCatalog(IMessageBus bus, IEnumerable<MiniAppHostService> hosts)
    {
        _bus = bus; _hosts = hosts.ToArray();
        _subscription = bus.Listen<HereAreButtons>().Subscribe(response =>
        {
            lock (_gate)
                foreach (var definition in response.Buttons)
                {
                    if (definition.MiniAppId != response.MiniAppId || definition.HostId != response.HostId)
                        throw new InvalidOperationException("Неверный владелец декларации команды.");
                    var key = definition.MiniAppId + ":" + definition.CommandId;
                    if (_definitions.TryGetValue(key, out var previous) && previous != definition)
                        throw new InvalidOperationException("Повторная декларация команды: " + key);
                    _definitions[key] = definition;
                }
        });
    }
    // Собирает снимок конкретного места, не передавая runtime обработчики по шине.
    public IReadOnlyList<ButtonDefinition> GetForHost(string hostId)
    {
        foreach (var host in _hosts) host.Start();
        _bus.SendMessage(new GiveMeButtons(Guid.NewGuid(), hostId));
        lock (_gate) return _definitions.Values.Where(x => x.HostId == hostId).OrderBy(x => x.Order).ThenBy(x => x.CommandId, StringComparer.Ordinal).ToArray();
    }
    // Обязательная команда без декларации не подменяется произвольной кнопкой.
    public ButtonDefinition Get(string miniAppId, string commandId, string hostId)
        => GetForHost(hostId).SingleOrDefault(x => x.MiniAppId == miniAppId && x.CommandId == commandId)
            ?? throw new KeyNotFoundException($"Команда '{miniAppId}:{commandId}' не объявлена в {hostId}.");
    // Singleton не удерживает окно после завершения приложения.
    public void Dispose() => _subscription.Dispose();
}

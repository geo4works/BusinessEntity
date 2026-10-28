using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Internal;
using BusinessEntity.Infrastructure.Bus;
using ReactiveUI;

namespace BusinessEntity.MiniApps.UserMessagesMiniApp.Facade;

// Фасад mini-app пользовательских сообщений.
internal sealed class UserMessagesMiniApp : IUserMessagesMiniApp
{
    private readonly UserMessagesMiniAppState _state;
    private readonly BusRequestScope _scope;
    private readonly IMessageBus _bus;
    private readonly UserMessagesMiniAppMessageHandler _messageHandler;

    // Создает фасад и активирует bus-подписки mini-app.
    public UserMessagesMiniApp(
        UserMessagesMiniAppState state, BusRequestScope scope, IMessageBus bus,
        UserMessagesMiniAppMessageHandler messageHandler)
    {
        _state = state; _scope = scope; _bus = bus;
        _messageHandler = messageHandler;
        _messageHandler.EnsureSubscribed();
    }

    // Пробрасывает событие изменения сообщений из state.
    public event Action? MessagesChanged
    {
        add => _state.Changed += value;
        remove => _state.Changed -= value;
    }

    // Возвращает стек пользовательских сообщений указанного пользователя.
    public IReadOnlyList<UserMessageRecord> GetMessages(Guid userId)
    {
        return _state.GetMessages(userId);
    }

    // Даёт внешнему коду явную точку для startup-инициализации mini-app.
    public void EnsureInitialized()
    {
        _messageHandler.EnsureSubscribed();
    }

    // Добавляет сообщение напрямую в state mini-app.
    public void Post(PostUserMessage message)
    {
        _bus.SendMessage(message);
    }

    // Очищает стек сообщений указанного пользователя.
    public void Clear(Guid userId)
    {
        _bus.SendMessage(new ClearUserMessages(userId, _scope.LogicalWindowId));
    }
}

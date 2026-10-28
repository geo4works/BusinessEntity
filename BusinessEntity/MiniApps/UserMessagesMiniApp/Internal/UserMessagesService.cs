using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Internal;
using BusinessEntity.Infrastructure.Bus;
using ReactiveUI;

namespace BusinessEntity.MiniApps.UserMessagesMiniApp.Internal;

// Фасад mini-app пользовательских сообщений.
internal sealed class UserMessagesService
{
    private readonly UserMessagesMiniAppState _state;
    private readonly BusRequestScope _scope;
    private readonly UserMessagesMiniAppMessageHandler _messageHandler;

    // Создает фасад и активирует bus-подписки mini-app.
    public UserMessagesService(
        UserMessagesMiniAppState state, BusRequestScope scope,
        UserMessagesMiniAppMessageHandler messageHandler)
    {
        _state = state; _scope = scope;
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

    // Собственный UI добавляет flash-сообщение напрямую, без самозапроса через шину.
    public void Post(PostUserMessage message) => _state.Add(message);

    // Очищает стек сообщений указанного пользователя.
    public void Clear(Guid userId)
    {
        _state.Clear(new ClearUserMessages(userId, _scope.LogicalWindowId));
    }
}

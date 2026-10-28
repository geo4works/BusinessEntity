using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMessagesMiniApp.Internal;

// Хранит ограниченный стек пользовательских сообщений для правой панели.
internal sealed class UserMessagesMiniAppState
{
    private const int MaxMessages = 50;
    private readonly Dictionary<Guid, List<UserMessageRecord>> _messagesByUser = new();
    private readonly object _syncRoot = new();
    private readonly HashSet<Guid> _delivered = new();

    // Сообщает подписчикам, что стек пользовательских сообщений изменился.
    public event Action? Changed;

    // Возвращает снимок стека сообщений пользователя, чтобы UI безопасно перечислял записи.
    public IReadOnlyList<UserMessageRecord> GetMessages(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<UserMessageRecord>();
        }

        lock (_syncRoot)
        {
            if (_messagesByUser.TryGetValue(userId, out var messages))
            {
                return messages.ToList();
            }

            return Array.Empty<UserMessageRecord>();
        }
    }

    // Добавляет новое сообщение наверх стека и удаляет записи сверх лимита.
    public void Add(PostUserMessage message)
    {
        if (message.UserId == Guid.Empty || string.IsNullOrWhiteSpace(message.Text))
        {
            return;
        }

        var record = new UserMessageRecord(
            message.CorrelationId,
            message.UserId,
            message.Text.Trim(),
            message.Level,
            NormalizeTitle(message),
            DateTime.Now);

        lock (_syncRoot)
        {
            if (!_delivered.Add(message.CorrelationId)) return;
            if (!_messagesByUser.TryGetValue(message.UserId, out var messages))
            {
                messages = new List<UserMessageRecord>();
                _messagesByUser[message.UserId] = messages;
            }

            messages.Insert(0, record);

            if (messages.Count > MaxMessages)
            {
                foreach (var expired in messages.Skip(MaxMessages)) _delivered.Remove(expired.Id);
                messages.RemoveRange(MaxMessages, messages.Count - MaxMessages);
            }
        }

        Changed?.Invoke();
    }

    // Удаляет все сообщения конкретного пользователя.
    public void Clear(ClearUserMessages message)
    {
        if (message.UserId == Guid.Empty)
        {
            return;
        }

        var changed = false;
        lock (_syncRoot)
        {
            if (_messagesByUser.Remove(message.UserId, out var removed))
            {
                foreach (var record in removed) _delivered.Remove(record.Id);
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    // Подбирает заголовок сообщения по уровню, если отправитель не передал свой.
    private static string NormalizeTitle(PostUserMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Title))
        {
            return message.Title.Trim();
        }

        return message.Level switch
        {
            UserMessageLevel.Success => "Успешно",
            UserMessageLevel.Warning => "Внимание",
            UserMessageLevel.Error => "Ошибка",
            _ => "Информация"
        };
    }
}

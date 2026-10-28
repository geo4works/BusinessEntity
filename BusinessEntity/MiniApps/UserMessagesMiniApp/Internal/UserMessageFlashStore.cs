using System.Collections.Concurrent;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMessagesMiniApp.Internal;

// Короткоживущая передача результата login через одноразовый URL-ключ.
internal sealed class UserMessageFlashStore : IUserMessageFlashStore
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    // Запоминает только текст уведомления без principal, cookie или пароля.
    public Guid Create(Guid userId, string text, string title)
    {
        foreach (var entry in _entries.Where(e => e.Value.ExpiresAt < DateTime.UtcNow))
            _entries.TryRemove(entry.Key, out _);
        var key = Guid.NewGuid();
        _entries[key] = new Entry(userId, text, title, DateTime.UtcNow.AddMinutes(2));
        return key;
    }
    // Не позволяет другому пользователю потребить уведомление и доставляет его один раз.
    public PostUserMessage? Take(Guid key, Guid userId, Guid logicalWindowId)
    {
        if (!_entries.TryGetValue(key, out var entry) || entry.UserId != userId) return null;
        if (!_entries.TryRemove(key, out entry) || entry.ExpiresAt < DateTime.UtcNow) return null;
        return new PostUserMessage(userId, logicalWindowId, key, entry.Text, UserMessageLevel.Success, entry.Title);
    }
    // Хранит только необходимую информацию до завершения redirect.
    private sealed record Entry(Guid UserId, string Text, string Title, DateTime ExpiresAt);
}

using System.Text.Json;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Подготовка своей формы собирает локальные записи вместо отдельных commit репозиториев.
internal sealed class UserMutationCapture : IDisposable
{
    private static readonly AsyncLocal<UserMutationCapture?> Slot = new();
    public static UserMutationCapture? Current => Slot.Value;
    public List<Action> PostCommit { get; } = new();
    public List<UserTableMutation> Changes { get; } = new();
    private readonly UserMutationCapture? _previous;
    // Область принадлежит одной async-операции подготовки и не разделяется окнами.
    public UserMutationCapture() { _previous = Slot.Value; Slot.Value = this; }
    // Фиксирует независимый DTO; пароль в User storage DTO не содержится.
    public void Upsert<T>(T data) where T : class
    {
        var id = Id(data); Changes.RemoveAll(x => x.TypeKey == typeof(T).Name && x.Id == id);
        Changes.Add(new(typeof(T).Name, id, JsonSerializer.Serialize(data, GraphArchiveJson.CreateOptions())));
    }
    // Фиксирует удаление служебной строки того же владельца.
    public void Delete<T>(Guid id) where T : class
    { Changes.RemoveAll(x => x.TypeKey == typeof(T).Name && x.Id == id); Changes.Add(new(typeof(T).Name, id, null)); }
    // Чтение во время подготовки видит уже подготовленные изменения своей операции.
    public IReadOnlyList<T> Overlay<T>(IEnumerable<T> stored) where T : class
    {
        var items = stored.ToDictionary(Id);
        foreach (var change in Changes.Where(x => x.TypeKey == typeof(T).Name))
            if (change.Data == null) items.Remove(change.Id);
            else items[change.Id] = JsonSerializer.Deserialize<T>(change.Data, GraphArchiveJson.CreateOptions())!;
        return items.Values.ToArray();
    }
    // Все служебные DTO имеют технический Guid Id, без зависимости общего Core от них.
    private static Guid Id<T>(T data) where T : class => (Guid)(typeof(T).GetProperty("Id")?.GetValue(data) ?? throw new InvalidOperationException("Нет Id служебной записи."));
    // Восстанавливает предыдущую async-local область даже после отказа подготовки.
    public void Dispose() => Slot.Value = _previous;
}
// Техническая запись принадлежит исключительно storage User, не является сущностью бизнес-графа.
internal sealed record UserTableMutation(string TypeKey, Guid Id, string? Data);

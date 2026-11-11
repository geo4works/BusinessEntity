namespace BusinessEntity.Infrastructure.Settings;
// Декларация раздела связывает URL и публичный ключ модуля без передачи компонента по шине.
public sealed record SettingsPageDefinition(string Route, string Title, string DefinitionKey, int Order, string Permission, Guid? ContextId = null, bool AcceptSubroutes = false, bool ApplyOnCenter = false, IReadOnlyList<string>? Subroutes = null)
{
    // Известный вложенный раздел сохраняет draft; неподдерживаемый URL не выбирается наугад.
    public bool Matches(string path) => path.Equals(Route, StringComparison.OrdinalIgnoreCase) || (AcceptSubroutes && path.StartsWith(Route + "/", StringComparison.OrdinalIgnoreCase) && (Subroutes == null || Subroutes.Contains(path[(Route.Length + 1)..], StringComparer.OrdinalIgnoreCase)));
}
// Не-SQL участник настроек сохраняет собственный draft и baseline.
public interface ISettingsDraft
{
    bool IsDirty
    {
        get;
    }
    // Подтверждает snapshot владельца, не скрывая частичный отказ хранилища.
    Task ApplyAsync(CancellationToken ct = default);
    // Отбрасывает только изменения после последнего подтверждённого Apply.
    Task CancelAsync(CancellationToken ct = default);
}
// Scoped реестр участников принадлежит одному окну, а не пользователю в целом.
public sealed class SettingsDraftSession(IEnumerable<SettingsPageDefinition> definitions)
{
    private readonly List<ISettingsDraft> Drafts = [];
    public event Action? Changed;
    public bool IsDirty => Drafts.Any(x => x.IsDirty);
    // Каждый владелец регистрирует один объект draft на circuit.
    public void Register(ISettingsDraft draft)
    {
        if (!Drafts.Contains(draft))
            Drafts.Add(draft);
    }
    // Снятый UI освобождает свою регистрацию, не удерживая компонент до конца circuit.
    public void Unregister(ISettingsDraft draft) => Drafts.Remove(draft);
    // Уведомляет общий navigation guard о вводе и завершении Apply.
    public void Notify() => Changed?.Invoke();
    // Последовательное применение не выдаётся за общую SQL-транзакцию.
    public async Task ApplyAsync(CancellationToken ct = default)
    {
        foreach (var draft in Drafts.Where(x => x.IsDirty).ToArray())
            await draft.ApplyAsync(ct);
        Notify();
    }
    // Cancel отменяет только ещё не применённое состояние.
    public async Task CancelAsync(CancellationToken ct = default)
    {
        foreach (var draft in Drafts.Where(x => x.IsDirty).ToArray())
            await draft.CancelAsync(ct);
        Notify();
    }
    // Только маршруты того же origin могут удерживать смонтированные drafts при навигации.
    public bool CanKeepDraftsForNavigation(Uri current, Uri target) => current.Scheme == target.Scheme && current.Authority == target.Authority && ContainsRoute(current.AbsolutePath) && ContainsRoute(target.AbsolutePath);
    // Переходы между известными разделами сохраняют смонтированные формы и их drafts.
    public bool ContainsRoute(string path) => definitions.Any(d => d.Matches(path)) || path == "/administration";
}

using System.Text.Json;
using BusinessEntity.Infrastructure.Settings;
using BusinessEntity.MiniApps.ThemeManagerMiniApp.Contracts;
using Microsoft.JSInterop;
namespace BusinessEntity.MiniApps.ThemeManagerMiniApp.Internal;
// Каталог, preview и baseline изолированы circuit; singleton пользовательской темы отсутствует.
internal sealed class ThemeSession(IJSRuntime js, SettingsDraftSession drafts) : ISettingsDraft
{
    private ThemeCatalog Catalog = ThemeCatalog.Empty(); private string UserKey = ""; private bool Ready; private BeTheme Baseline = ThemeSchema.BuiltIn();
    public BeTheme Draft { get; private set; } = ThemeSchema.BuiltIn();
    private readonly SemaphoreSlim Initialization = new(1, 1);
    public event Action? StateChanged;
    private readonly SemaphoreSlim Mutations = new(1,1);
    private long Revision;
    // Параллельная запись не строит второй snapshot из устаревшего каталога.
    private IDisposable BeginMutation()
    {
        if(!Mutations.Wait(0)) throw new InvalidOperationException("Дождитесь завершения операции с темой.");
        return new MutationLease(Mutations);
    }
    // Lease освобождает writer даже при отказе JS или строгого validator.
    private sealed class MutationLease(SemaphoreSlim gate):IDisposable
    {
        private int Released;
        // Повторное освобождение не увеличивает доступную ёмкость writer-а.
        public void Dispose(){if(Interlocked.Exchange(ref Released,1)==0)gate.Release();}
    }
    public bool ReadOnly => Draft.Id == ThemeSchema.BuiltInId;
    public bool IsDirty
    {
        get; private set;
    }
    public string? Error
    {
        get; private set;
    }
    public IReadOnlyList<BeTheme> Themes => new[] { ThemeSchema.BuiltIn() }.Concat(Catalog.Themes).ToArray();
    public string?[] Palette => Catalog.Palette.ToArray();
    // Browser доступен после первого интерактивного render; повреждённый каталог не перезаписывается.
    public async Task InitializeAsync(Guid user)
    {
        await Initialization.WaitAsync();
        try
        {
            await InitializeCoreAsync(user);
        }
        finally { Initialization.Release(); }
    }
    // Сериализация bootstrap и редактора исключает двойную загрузку одного каталога.
    private async Task InitializeCoreAsync(Guid user)
    {
        if (Ready)
            return;
        UserKey = user.ToString("D");
        drafts.Register(this);
        var json = await js.InvokeAsync<string?>("beTheme.load", UserKey);
        try
        {
            if (json != null)
            {
                var c = JsonSerializer.Deserialize<ThemeCatalog>(json, ThemeSchema.Options) ?? throw new InvalidDataException("Пустой каталог.");
                ThemeSchema.ValidateCatalog(c);
                Catalog = c;
            }
            Baseline = ThemeSchema.Copy(Themes.Single(x => x.Id == Catalog.ActiveThemeId));
            Draft = ThemeSchema.Copy(Baseline);
            await js.InvokeVoidAsync("beTheme.apply", Baseline);
            var early = await js.InvokeAsync<string?>("beTheme.consumeError");
            if (early != null)
                Error = early;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException) { Error = "Локальная тема не загружена: " + e.Message; Catalog = ThemeCatalog.Empty(); Baseline = ThemeSchema.BuiltIn(); Draft = ThemeSchema.Copy(Baseline); await js.InvokeVoidAsync("beTheme.apply", Baseline); }
        Ready = true;
    }
    // Изменение отмечается общим guard до preview или сохранения.
    public void Changed()
    {
        Revision++;
        IsDirty = true;
        drafts.Notify(); StateChanged?.Invoke();
    }
    // Выбор темы меняет draft, но не подтверждённую активную тему.
    public void Select(Guid id)
    {
        Draft = ThemeSchema.Copy(Themes.Single(x => x.Id == id));
        Changed();
    }
    // Новая identity также назначается каждому стилю таблицы.
    public void Duplicate()
    {
        var t = ThemeSchema.Copy(Draft);
        t.Id = Guid.NewGuid();
        t.Name = ThemeSchema.FreeName(t.Name, Themes.Select(x => x.Name));
        foreach (var style in t.Tables)
        {
            var old = style.Id;
            style.Id = Guid.NewGuid();
            if (t.ActiveTableStyleId == old)
                t.ActiveTableStyleId = style.Id;
        }
        Draft = t;
        Changed();
    }
    // Новый редактируемый draft строится от встроенных значений.
    public void New()
    {
        Draft = ThemeSchema.BuiltIn();
        Duplicate();
    }
    // Preview касается только DOM текущего окна.
    public async Task PreviewAsync()
    {
        using var mutation = BeginMutation();
        ThemeSchema.Validate(Draft);
        await js.InvokeVoidAsync("beTheme.apply", Draft);
        Changed();
    }
    // Сначала atomic browser write, затем изменение подтверждённого in-memory каталога.
    private async Task Write(ThemeCatalog candidate, CancellationToken ct = default)
    {
        ThemeSchema.ValidateCatalog(candidate);
        await js.InvokeVoidAsync("beTheme.save", ct, UserKey, JsonSerializer.Serialize(candidate, ThemeSchema.Options));
        Catalog = candidate;
        StateChanged?.Invoke();
    }
    // Явное сохранение темы не делает её автоматически активной.
    public async Task SaveAsync()
    {
        using var mutation = BeginMutation();
        if (ReadOnly)
            throw new InvalidOperationException("Встроенная тема доступна только для чтения.");
        ThemeSchema.Validate(Draft);
        var c = ThemeSchema.Copy(Catalog);
        if (Themes.Any(x => x.Id != Draft.Id && x.Name.Equals(Draft.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Имя темы занято.");
        c.Themes.RemoveAll(x => x.Id == Draft.Id);
        c.Themes.Add(ThemeSchema.Copy(Draft));
        await Write(c);
    }
    // Удаление активной локальной темы явно возвращает встроенную.
    public async Task DeleteAsync()
    {
        using var mutation = BeginMutation();
        if (ReadOnly)
            throw new InvalidOperationException("Встроенную тему нельзя удалить.");
        var c = ThemeSchema.Copy(Catalog);
        c.Themes.RemoveAll(x => x.Id == Draft.Id);
        if (c.ActiveThemeId == Draft.Id)
            c.ActiveThemeId = ThemeSchema.BuiltInId;
        await Write(c);
        Draft = ThemeSchema.Copy(Themes.Single(x => x.Id == c.ActiveThemeId));
        Baseline = ThemeSchema.Copy(Draft);
        IsDirty = false;
        await js.InvokeVoidAsync("beTheme.apply", Baseline);
        drafts.Notify(); StateChanged?.Invoke();
    }
    // Import использует тот же валидатор и создаёт новые IDs до явного сохранения.
    public void Import(string json)
    {
        Draft = ThemeSchema.Import(json);
        Duplicate();
    }
    // Export отдаёт только валидный переносимый snapshot.
    public async Task ExportAsync()
    {
        ThemeSchema.Validate(Draft);
        await js.InvokeVoidAsync("beTheme.download", Draft.Name + ".betheme", JsonSerializer.Serialize(new ThemeExchange("BusinessEntityTheme", 1, Draft), ThemeSchema.Options));
    }
    // Палитра фиксируется отдельно от preview и сохраняется единственным snapshot.
    public async Task SetPaletteAsync(int slot, string color)
    {
        using var mutation = BeginMutation();
        ThemeSchema.Color(color);
        if (slot is < 0 or > 15)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var c = ThemeSchema.Copy(Catalog);
        c.Palette[slot] = color;
        await Write(c);
    }
    // Apply сохраняет новый snapshot и активный ID; ошибка storage оставляет старую baseline.
    public async Task ApplyAsync(CancellationToken ct = default)
    {
        using var mutation = BeginMutation();
        var sent = ThemeSchema.Copy(Draft);
        var revision = Revision;
        ThemeSchema.Validate(sent);
        var c = ThemeSchema.Copy(Catalog);
        if (sent.Id != ThemeSchema.BuiltInId)
        {
            c.Themes.RemoveAll(x => x.Id == sent.Id);
            c.Themes.Add(sent);
        }
        c.ActiveThemeId = sent.Id;
        await Write(c, ct);
        Baseline = ThemeSchema.Copy(sent);
        await js.InvokeVoidAsync("beTheme.apply", ct, Baseline);
        IsDirty = Revision != revision;
        drafts.Notify(); StateChanged?.Invoke();
    }
    // Отмена не удаляет явно сохранённые темы, но восстанавливает последний Apply в текущем окне.
    public async Task CancelAsync(CancellationToken ct = default)
    {
        using var mutation = BeginMutation();
        Draft = ThemeSchema.Copy(Baseline);
        await js.InvokeVoidAsync("beTheme.apply", ct, Baseline);
        IsDirty = false;
        drafts.Notify(); StateChanged?.Invoke();
    }
}

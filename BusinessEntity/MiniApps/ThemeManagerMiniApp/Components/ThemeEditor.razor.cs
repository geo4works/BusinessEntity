using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using BusinessEntity.MiniApps.ThemeManagerMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;
namespace BusinessEntity.MiniApps.ThemeManagerMiniApp.Components;

// Обрабатывает жизненный цикл и события компонента своего окна.
public partial class ThemeEditor
{
    private string PreviewId => "be-preview-" + Module!.ModuleId.ToString("N");
    private bool Ready, Busy; private string Section = "Общий"; private string? Notice; private UserMessageLevel Level = UserMessageLevel.Error;
    private readonly string[] Sections = ["Общий", "Скроллбары", "Таблицы", "Сообщения"];
    private static readonly Dictionary<string, string> TableColors = new() { { "Header", "Шапка" }, { "Background", "Фон" }, { "Alternate", "Чередование" }, { "Selection", "Выделение" }, { "Text", "Текст" }, { "Border", "Граница" } };
    private BeTableStyle Table => Session.Draft.Tables.Single(x => x.Id == Session.Draft.ActiveTableStyleId);
    private string PreviewStyle => string.Join(";", Session.Draft.Colors.Select(x => $"--be-{x.Key}:{x.Value}")) + ";--be-message-accent-width:" + Session.Draft.Numbers["message-accent-width"] + "px";
    private string TablePreviewStyle => $"--be-table-header:{Table.Header};--be-table-background:{Table.Background};--be-table-alternate:{Table.Alternate};--be-table-selection:{Table.Selection};--be-table-text:{Table.Text};--be-table-border:{Table.Border}";
    // ThemeSession shared с bootstrap того же circuit, UI ждёт интерактивного JS.
    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (first)
        {
            try
            {
                var user = await Users.EnsureCurrentUserAsync();
                if (user == null)
                    throw new UnauthorizedAccessException();
                await Session.InitializeAsync(user.Id);
                Ready = true;
            }
            catch (Exception e) { Notice = e.Message; }
            StateHasChanged();
        }
        if (Ready && Section == "Сообщения")
        {
            try
            {
                ThemeSchema.Validate(Session.Draft);
                await JS.InvokeVoidAsync("beTheme.preview", PreviewId, Session.Draft);
            }
            catch (InvalidDataException) { }
            catch (JSDisconnectedException) { }
        }
    }
    // Изменение цвета отмечает draft без применения CSS.
    private void SetColor(string key, string color)
    {
        Session.Draft.Colors[key] = color;
        Session.Changed();
    }
    // Ошибки обычных команд имеют одно место публикации.
    private async Task Run(Func<Task> action, bool success = false)
    {
        Busy = true;
        Notice = null;
        try
        {
            await action();
            if (success)
            {
                Notice = "Тема сохранена.";
                Level = UserMessageLevel.Success;
            }
        }
        catch (Exception e) { Notice = e.Message; Level = UserMessageLevel.Error; }
        finally { Busy = false; }
    }
    // Ограничивает вход до чтения payload, затем общий strict validator выполняет импорт.
    private async Task Import(InputFileChangeEventArgs args) => await Run(async () => { using var stream = args.File.OpenReadStream(262144); using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), false); Session.Import(await reader.ReadToEndAsync()); });
    // Палитра записывается атомарно, ошибка не превращается в тихий успех.
    private Task StorePalette(PaletteChange change) => Run(() => Session.SetPaletteAsync(change.Slot, change.Color));
    // Свойства стиля адресуются фиксированным allowlist, имя свойства из браузера не исполняется.
    private string GetTableColor(string key) => (string)typeof(BeTableStyle).GetProperty(key)!.GetValue(Table)!;
    // UI пишет только известный цвет и отмечает draft темы.
    private void SetTableColor(string key, string value)
    {
        ThemeSchema.Color(value);
        typeof(BeTableStyle).GetProperty(key)!.SetValue(Table, value);
        Session.Changed();
    }
    // Копия сохраняет оформление и получает независимые identity и имя.
    private void NewStyle()
    {
        var style = ThemeSchema.Copy(Table);
        style.Id = Guid.NewGuid();
        style.Name = ThemeSchema.FreeName(style.Name, Session.Draft.Tables.Select(x => x.Name));
        Session.Draft.Tables.Add(style);
        Session.Draft.ActiveTableStyleId = style.Id;
        Session.Changed();
    }
    // Последний стиль удалять нельзя, активный указатель всегда соответствует каталогу.
    private void DeleteStyle()
    {
        if (Session.Draft.Tables.Count <= 1)
            return;
        Session.Draft.Tables.Remove(Table);
        Session.Draft.ActiveTableStyleId = Session.Draft.Tables[0].Id;
        Session.Changed();
    }

    private bool Disposed;
    // Общий Apply обновляет каталог даже когда событие выполняется в другом компоненте центра.
    protected override void OnInitialized()=>Session.StateChanged+=Refresh;
    // Событие не удерживает закрытый renderer и не изменяет чужое окно.
    private void Refresh(){if(!Disposed)_=InvokeAsync(()=>{if(!Disposed)StateHasChanged();});}
    // Освобождает подписку редактора на scoped состояние темы.
    public void Dispose(){Disposed=true;Session.StateChanged-=Refresh;}
}

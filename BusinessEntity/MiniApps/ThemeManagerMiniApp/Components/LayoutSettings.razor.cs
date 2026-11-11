using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
namespace BusinessEntity.MiniApps.ThemeManagerMiniApp.Components;

// Обрабатывает жизненный цикл и события компонента своего окна.
public partial class LayoutSettings
{
    private bool Disposed;
    private UserPropertyDto? Stored; private string? Error;
    public bool IsDirty
    {
        get; private set;
    }
    // Читает только собственную настройку и регистрирует draft в текущем окне.
    protected override async Task OnInitializedAsync()
    {
        Drafts.Register(this);
        var rows = await Users.GetCurrentUserPropertiesAsync((int)UserPropertyTypeEnum.ShellLayoutId);
        Stored = rows.SingleOrDefault(x => x.Metadata == "Ui.ShellLayout");
        if (Stored != null && Stored.Data != "standard")
            Error = "Неизвестная сохранённая компоновка. Используется стандартная; исходное значение сохранено.";
    }
    // Выбор отмечает только draft, не пишет в БД до Apply.
    private void Select()
    {
        IsDirty = true;
        Drafts.Notify();
    }
    // Connector вызывает штатного владельца пользовательских свойств, UI не получает DbContext.
    public async Task ApplyAsync(CancellationToken ct = default)
    {
        Stored = await Users.UpsertCurrentUserPropertyAsync(Stored?.Id, (int)UserPropertyTypeEnum.ShellLayoutId, "standard", "Ui.ShellLayout", ct) ?? throw new InvalidOperationException("Компоновка не сохранена.");
        IsDirty = false;
        Drafts.Notify();
        if(!Disposed) await InvokeAsync(()=>{if(!Disposed) StateHasChanged();});
    }
    // Снимает ссылку на компонент при выходе из центра настроек.
    public void Dispose() { Disposed=true; Drafts.Unregister(this); }
    // Отмена не меняет уже записанные свойства пользователя.
    public Task CancelAsync(CancellationToken ct = default)
    {
        IsDirty = false;
        Drafts.Notify();
        return Task.CompletedTask;
    }

}

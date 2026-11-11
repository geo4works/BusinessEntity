using Microsoft.AspNetCore.Components.Routing;
namespace BusinessEntity.MiniApps.AdministrationMiniApp.Components;

// Обрабатывает жизненный цикл и события компонента своего окна.
public partial class AdministrationNavigation
{
    private List<BusinessEntity.Infrastructure.Settings.SettingsPageDefinition> Pages = [];
    private readonly HashSet<string> Visited = [];
    private BusinessEntity.Infrastructure.Settings.SettingsPageDefinition? Active;
    private readonly Dictionary<string, IDictionary<string, object>?> PageParameters = [];
    private bool Busy, Disposed; private string? Notice;
    private BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts.UserMessageLevel NoticeLevel;
    // Создаёт только разрешённые разделы; содержимое остаётся у владельцев модулей.
    protected override async Task OnInitializedAsync()
    {
        foreach (var p in Definitions.OrderBy(x => x.Order))
            if (await ModuleManager.CanOpenDefinitionAsync(Catalog.Get(p.DefinitionKey), p.ContextId))
                Pages.Add(p);
        Navigation.LocationChanged += Location;
        Select();
    }
    // Поздняя навигация меняет только выбранный раздел, сохраняя уже созданные формы.
    private void Location(object? sender, LocationChangedEventArgs e)
    {
        if (!Disposed)
            _ = InvokeAsync(() => { Select(); StateHasChanged(); });
    }
    // У неизвестного маршрута нет неявной подмены похожим разделом.
    private void Select()
    {
        var path = new Uri(Navigation.Uri).AbsolutePath;
        Active = Pages.FirstOrDefault(p => p.Matches(path));
        if (path == "/administration")
            Active = Pages.FirstOrDefault();
        if (Active != null)
        {
            Visited.Add(Active.Route);
            PageParameters[Active.Route] = BuildParameters(Active);
        }
    }
    // Адресные параметры сохраняют существующие deep links Users и пробного identity-плана.
    private IDictionary<string, object>? BuildParameters(BusinessEntity.Infrastructure.Settings.SettingsPageDefinition p)
    {
        if (p.AcceptSubroutes)
        {
            var path = new Uri(Navigation.Uri).AbsolutePath;
            return new Dictionary<string, object> { { "Section", Active == p && path.Length > p.Route.Length ? path[(p.Route.Length + 1)..] : "general" } };
        }
        if (p.Route.EndsWith("identity-providers"))
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(Navigation.Uri).Query);
            if (query.TryGetValue("planId", out var id) && Guid.TryParse(id, out var plan))
                return new Dictionary<string, object> { { "InitialPlanId", plan } };
        }
        return null;
    }
    // Неактивная страница получает прежние параметры, а не данные текущего URL.
    private IDictionary<string, object>? Parameters(global::BusinessEntity.Infrastructure.Settings.SettingsPageDefinition p) => PageParameters.GetValueOrDefault(p.Route);
    // Общий Apply вызывает только dirty save contexts и участников настроек, а не административные команды.
    private async Task Apply(bool close)
    {
        Busy = true;
        Notice = null;
        try
        {
            var result = await ApplySettings();
            NoticeLevel = result.CanLeave ? BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts.UserMessageLevel.Success : BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts.UserMessageLevel.Error;
            Notice = result.CanLeave ? "Настройки применены." : result.Error;
            if (close && result.CanLeave)
                Navigation.NavigateTo("/");
        }
        finally { Busy = false; }
    }
    // Только декларативно включённые настройки участвуют в общей записи; credentials/grants/identity требуют явных команд владельцев.
    private async Task<global::BusinessEntity.Infrastructure.Saving.NavigationSaveResolution> ApplySettings()
    {
        try
        {
            await ModuleManager.WaitForPendingEditsAsync();
            await Drafts.ApplyAsync();
            var allowed = Pages.Where(x => x.ApplyOnCenter).Select(x => x.DefinitionKey).ToHashSet();
            foreach (var context in ModuleManager.DirtyContexts)
            {
                var owner = ModuleManager.Modules.FirstOrDefault(x => x.SaveContext == context && allowed.Contains(x.Definition.Key));
                if (owner == null)
                    continue;
                global::BusinessEntity.Infrastructure.Saving.MiniAppSaveAttempt? result = null;
                await ModuleManager.ExecuteAsync(owner.ModuleId, async _ => result = await context.SaveAsync(owner.ModuleId));
                if (result?.Success != true)
                    return new(false, result?.Error ?? "Сохранение не завершено.");
            }
            return ModuleManager.DirtyContexts.Count == 0 && !Drafts.IsDirty ? new(true) : new(false, "Настройки применены. Сохраните или отмените незавершённые правки в соответствующем разделе.");
        }
        catch (Exception e) { return new(false, global::BusinessEntity.Infrastructure.Bus.BusError.FromException(e).Message); }
    }
    // Отмена сохраняет уже применённые значения и не допускает потери Unknown-операции.
    private async Task Cancel()
    {
        Busy = true;
        try
        {
            var result = await Guard.ResolveAsync(BusinessEntity.Infrastructure.Saving.NavigationSaveDecision.Discard);
            if (result.CanLeave)
                Navigation.NavigateTo("/");
            else
            {
                Notice = result.Error;
                NoticeLevel = BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts.UserMessageLevel.Error;
            }
        }
        finally { Busy = false; }
    }
    // Освобождает подписку центра при выходе из административного layout.
    public void Dispose()
    {
        Disposed = true;
        Navigation.LocationChanged -= Location;
    }

}

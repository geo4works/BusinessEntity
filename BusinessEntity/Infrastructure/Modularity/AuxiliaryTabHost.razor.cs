using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
namespace BusinessEntity.Infrastructure.Modularity;

// Обрабатывает жизненный цикл и события компонента своего окна.
public partial class AuxiliaryTabHost
{
    [Parameter, EditorRequired] public string Area { get; set; } = "";
    private IReadOnlyList<ModuleDefinition> Tabs = []; private string? Selected, Error; private bool Disposed;
    private readonly HashSet<string> Closed = [];
    // Каталог уведомляет о поздних декларациях, scoped manager проверяет доступ своего окна.
    protected override void OnInitialized() => Catalog.DefinitionsChanged += Refresh;
    // Каждая область получает только разрешённые модули; неизвестный ключ не подменяется другим.
    protected override Task OnParametersSetAsync() => Load();
    // Обновление переводится в owning renderer и не исполняется после disposal.
    private void Refresh()
    {
        if (!Disposed)
            _ = InvokeAsync(async () => { if (!Disposed) { await Load(); StateHasChanged(); } });
    }
    // Порядок деклараций устойчив; выбранная вспомогательная вкладка не активирует центральный master.
    private async Task Load()
    {
        try
        {
            var tabs = new List<ModuleDefinition>();
            foreach (var d in Catalog.Definitions.Where(x => x.Placement == ModulePlacement.AuxiliaryTab && x.HostArea == Area && x.OpenOnStartup))
                if (await Manager.CanOpenDefinitionAsync(d))
                    tabs.Add(d);
            Tabs = tabs;
            if (!Tabs.Any(x => x.Key == Selected))
                Selected = Tabs.FirstOrDefault()?.Key;
        }
        catch (Exception e) { Error = e.Message; }
    }
    // Клавиатурная навигация остаётся внутри списка вкладок текущего host-а.
    private async Task Key(ModuleDefinition tab, string key)
    {
        var i = Tabs.ToList().IndexOf(tab);
        var n = key switch
        {
            "ArrowRight" => Math.Min(Tabs.Count - 1, i + 1),
            "ArrowLeft" => Math.Max(0, i - 1),
            "Home" => 0,
            "End" => Tabs.Count - 1,
            _ => i
        };
        if (n >= 0 && Tabs.Count > 0)
        {
            Selected = Tabs[n].Key;
            await JS.InvokeVoidAsync("beUi.focus", TabId(Tabs[n]));
        }
    }
    // Закрывается только разрешённая декларацией чистая вспомогательная вкладка.
    private async Task Close(ModuleDefinition tab)
    {
        try
        {
            var module = Manager.Modules.SingleOrDefault(x => x.Definition.Key == tab.Key);
            if (module != null)
                await Manager.CloseAsync(module.ModuleId);
            Closed.Add(tab.Key);
            if (Selected == tab.Key)
                Selected = Tabs.FirstOrDefault(x => !Closed.Contains(x.Key))?.Key;
        }
        catch (Exception e) { Error = e.Message; }
    }
    // DOM identifiers включают окно: одинаковые декларации разных окон не смешиваются.
    private string TabId(ModuleDefinition t) => $"aux-{Manager.LogicalWindowId:N}-{t.ModuleDefinitionId}";
    // Панель связана с доступной ARIA-вкладкой того же экземпляра.
    private string PanelId(ModuleDefinition t) => TabId(t) + "-panel";
    // Освобождает единственную подписку host-а.
    public void Dispose()
    {
        Disposed = true;
        Catalog.DefinitionsChanged -= Refresh;
    }

}

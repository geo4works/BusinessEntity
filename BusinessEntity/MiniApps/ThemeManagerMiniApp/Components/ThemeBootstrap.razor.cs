
namespace BusinessEntity.MiniApps.ThemeManagerMiniApp.Components;

// Обрабатывает жизненный цикл и события компонента своего окна.
public partial class ThemeBootstrap
{
    private string? Error;
    // Ошибки раннего применения попадают в существующий адресный журнал.
    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (!first)
            return;
        try
        {
            var user = await Users.EnsureCurrentUserAsync();
            if (user?.Id is { } id)
                await Themes.InitializeAsync(id);
            Error = Themes.Error;
        }
        catch (Exception e) { Error = "Не удалось загрузить темы: " + e.Message; }
        if (Error != null)
            StateHasChanged();
    }

}

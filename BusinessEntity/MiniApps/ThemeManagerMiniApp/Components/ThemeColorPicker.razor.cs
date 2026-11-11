using Microsoft.AspNetCore.Components;
using BusinessEntity.MiniApps.ThemeManagerMiniApp.Contracts;
namespace BusinessEntity.MiniApps.ThemeManagerMiniApp.Components;

// Обрабатывает жизненный цикл и события компонента своего окна.
public partial class ThemeColorPicker
{
    [Parameter] public string Value { get; set; } = "#FFFFFF";
    [Parameter]
    public EventCallback<string> ValueChanged
    {
        get; set;
    }
    [Parameter]
    public bool ReadOnly
    {
        get; set;
    }
    [Parameter] public string?[] Palette { get; set; } = new string?[16];
    [Parameter]
    public EventCallback<PaletteChange> PaletteChanged
    {
        get; set;
    }
    private string Draft = "#FFFFFF", Previous = ""; private int Selected; private string? Error;
    // Внешнее переключение темы обновляет picker, незавершённый локальный выбор не применяет тему.
    protected override void OnParametersSet()
    {
        if (Previous != Value)
        {
            Previous = Value;
            Draft = Value;
        }
    }
    // Слот выбирается отдельно от подтверждения цвета свойства.
    private void Select(int slot) => Selected = slot;
    // Выбранный цвет переносится в черновик picker, но ещё не в тему.
    private void Use(int slot)
    {
        Selected = slot;
        if (Palette[slot] != null)
            Draft = Palette[slot]!;
    }
    // Ошибка HEX остаётся рядом с вводом и не меняет значение свойства.
    private async Task Choose()
    {
        try
        {
            ThemeSchema.Color(Draft);
            Error = null;
            await ValueChanged.InvokeAsync(Draft);
        }
        catch (InvalidDataException e) { Error = e.Message; }
    }
    // Палитра записывается отдельной явной командой в atomic catalog.
    private async Task Store()
    {
        try
        {
            ThemeSchema.Color(Draft);
            await PaletteChanged.InvokeAsync(new(Selected, Draft));
            Error = null;
        }
        catch (Exception e) { Error = e.Message; }
    }

}

using Microsoft.AspNetCore.Components;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;
namespace BusinessEntity.Infrastructure.UI;

// Обрабатывает жизненный цикл и события компонента своего окна.
public partial class OperationNotice
{
    [Parameter]
    public string? Text
    {
        get; set;
    }
    [Parameter] public UserMessageLevel Level { get; set; } = UserMessageLevel.Info;
    [Parameter]
    public string? Title
    {
        get; set;
    }
    private string? Previous;
    // UI-уведомление публикуется один раз для нового статуса, без дублирующего alert.
    protected override void OnParametersSet()
    {
        if (Text == Previous)
            return;
        Previous = Text;
        if (!string.IsNullOrWhiteSpace(Text) && Scope.UserId is { } user)
            Bus.SendMessage(new PostUserMessage(user, Scope.LogicalWindowId, Guid.NewGuid(), Text, Level, Title));
    }

}

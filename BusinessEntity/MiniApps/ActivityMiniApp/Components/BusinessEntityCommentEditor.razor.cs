using BusinessEntity.MiniApps.ActivityMiniApp.Contracts;
using BusinessEntity.MiniApps.ActivityMiniApp.Internal;
using Microsoft.AspNetCore.Components;

namespace BusinessEntity.MiniApps.ActivityMiniApp.Components;

/// <summary>
/// Поле ввода комментария или ответа с действием отправки.
/// </summary>
public partial class BusinessEntityCommentEditor : ComponentBase
{
    [Parameter] public Guid BusinessEntityId { get; set; }
    [Parameter] public Guid? ParentId { get; set; }
    [Parameter] public bool IsEnabled { get; set; } = true;
    [Parameter] public string Placeholder { get; set; } = "Написать комментарий";
    [Parameter] public int Rows { get; set; } = 2;
    [Parameter] public EventCallback OnSubmitted { get; set; }

    [Inject] private ActivityCommentService CommentsService { get; set; } = default!;

    private string Text { get; set; } = string.Empty;
    private string? Error { get; set; }
    private bool IsSubmitting { get; set; }
    private bool CanSubmit => IsEnabled && !IsSubmitting && !string.IsNullOrWhiteSpace(Text);

    // Создает комментарий через ActivityMiniApp и очищает поле после успешной отправки.
    private async Task SubmitAsync()
    {
        if (!CanSubmit)
        {
            return;
        }

        IsSubmitting = true;
        Error = null;

        try
        {
            await CommentsService.CreateCommentAsync(new BusinessEntityCommentCreateRequest
            {
                BusinessEntityId = BusinessEntityId,
                ParentId = ParentId,
                Text = Text
            });
            Text = string.Empty;
            await OnSubmitted.InvokeAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}

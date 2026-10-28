using BusinessEntity.MiniApps.ActivityMiniApp.Contracts;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using Microsoft.AspNetCore.Components;

namespace BusinessEntity.MiniApps.ActivityMiniApp.Components;

/// <summary>
/// Секция комментариев для одного BusinessEntity.
/// </summary>
public partial class BusinessEntityCommentsSection : ComponentBase
{
    [Parameter] public Guid BusinessEntityId { get; set; }
    [Parameter] public bool CanWrite { get; set; } = true;
    [Parameter] public bool UseOwnScroll { get; set; }
    [Parameter] public int ScrollMaxHeightPx { get; set; } = 280;
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public EventCallback<int> OnCountChanged { get; set; }

    [Inject] public IActivityConnector ActivityConnector { get; set; } = default!;
    [Inject] public IUserConnector UserConnector { get; set; } = default!;

    private IReadOnlyList<BusinessEntityCommentRecord> Comments { get; set; } = Array.Empty<BusinessEntityCommentRecord>();
    private bool IsLoading { get; set; }
    private string? Error { get; set; }
    private Guid LoadedBusinessEntityId { get; set; }
    private bool HasLoaded { get; set; }
    private bool CanWriteEffective { get; set; }
    private string SectionClass => string.Join(
        " ",
        new[]
        {
            "be-comments-section",
            UseOwnScroll ? "be-comments-section--own-scroll" : null,
            CssClass
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
    private string? ListStyle => UseOwnScroll
        ? $"max-height: {Math.Max(120, ScrollMaxHeightPx)}px;"
        : null;

    // Перечитывает комментарии при первом рендере и при смене BusinessEntityId.
    protected override async Task OnParametersSetAsync()
    {
        try
        {
            CanWriteEffective = await ResolveCanWriteEffectiveAsync();
            if (LoadedBusinessEntityId == BusinessEntityId && HasLoaded) return;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
    }

    // Загружает актуальную пачку комментариев из ActivityMiniApp.
    private async Task ReloadAsync()
    {
        if (BusinessEntityId == Guid.Empty)
        {
            Comments = Array.Empty<BusinessEntityCommentRecord>();
            LoadedBusinessEntityId = Guid.Empty;
            HasLoaded = true;
            await NotifyCountChangedAsync();
            return;
        }

        IsLoading = true;
        Error = null;

        try
        {
            Comments = await ActivityConnector.GetCommentsAsync(BusinessEntityId);
            LoadedBusinessEntityId = BusinessEntityId;
            HasLoaded = true;
            await NotifyCountChangedAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private Task NotifyCountChangedAsync()
    {
        return OnCountChanged.HasDelegate
            ? OnCountChanged.InvokeAsync(Comments.Count)
            : Task.CompletedTask;
    }

    // Проверяет, можно ли текущему пользователю показывать активный editor комментария.
    private async Task<bool> ResolveCanWriteEffectiveAsync()
    {
        if (!CanWrite)
        {
            return false;
        }

        var user = await UserConnector.GetCurrentUserAsync();
        return user?.IsAuthenticated == true;
    }
}

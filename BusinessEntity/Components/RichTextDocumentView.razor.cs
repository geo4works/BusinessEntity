using BusinessEntity.Core.RichText;
using BusinessEntity.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BusinessEntity.Components
{
    public partial class RichTextDocumentView : ComponentBase
    {
        [Parameter] public string EntityName { get; set; } = string.Empty;
        [Parameter] public Guid BusinessEntityId { get; set; }
        [Parameter] public RichTextDocumentChunkWindow? InitialChunkWindow { get; set; }
        [Parameter] public bool IsInitialContentLoading { get; set; }
        [Parameter] public bool IsOutlineLoading { get; set; }
        [Parameter] public bool IsBusy { get; set; }
        [Parameter] public bool IsRebuildingTableOfContents { get; set; }
        [Parameter] public string? StatusMessage { get; set; }
        [Parameter] public int VersionsRefreshToken { get; set; }
        [Parameter] public int ViewedVersion { get; set; } = 1;
        [Parameter] public int LatestVersion { get; set; } = 1;
        [Parameter] public int PublishedVersion { get; set; }
        [Parameter] public bool CanEditViewedVersion { get; set; } = true;
        [Parameter] public IReadOnlyList<RichTextDocumentOutlineNode>? OutlineNodes { get; set; }
        [Parameter] public EventCallback<InputFileChangeEventArgs> OnImportSelected { get; set; }
        [Parameter] public EventCallback OnRebuildTableOfContents { get; set; }
        [Parameter] public EventCallback<RichTextDocumentEditorSaveRequest> OnEditorSaved { get; set; }
        [Parameter] public EventCallback<int> OnVersionSelected { get; set; }
        [Parameter] public EventCallback OnPublishRequested { get; set; }
        [Parameter] public EventCallback OnDeleteRequested { get; set; }
        [Parameter] public EventCallback<bool> OnPublicChanged { get; set; }
        [Parameter] public bool CanPublish { get; set; }
        [Parameter] public bool CanDelete { get; set; }
        [Parameter] public bool CanChangePublicFlag { get; set; }
        [Parameter] public bool IsPublic { get; set; }
        [Parameter] public bool CanRestoreVersion { get; set; }
        [Parameter] public bool CanBrowseVersions { get; set; } = true;
        [Parameter] public bool StartInEditMode { get; set; }

        private RichTextDocumentEditView? EditView { get; set; }
        public bool IsEditingNow => IsEditMode;
        private bool IsEditMode { get; set; }
        private bool IsSaving { get; set; }
        private string? SaveError { get; set; }
        private string EditableEntityName { get; set; } = string.Empty;
        private string? TitleValidationMessage { get; set; }
        private RichTextDocumentViewportPosition? EditInitialPosition { get; set; }
        private RichTextDocumentViewportPosition? ReadInitialPosition { get; set; }
        private Guid? StartInEditModeAppliedFor { get; set; }
        private string ReadViewInstanceKey => $"{BusinessEntityId:D}:{ViewedVersion:D}";

        protected override void OnParametersSet()
        {
            if (!StartInEditMode)
            {
                StartInEditModeAppliedFor = null;
            }
            else if (CanEditViewedVersion && StartInEditModeAppliedFor != BusinessEntityId)
            {
                EditInitialPosition = null;
                ReadInitialPosition = null;
                IsEditMode = true;
                EditableEntityName = RichTextDocumentHelper.FilterRichTextDocumentTitle(EntityName);
                TitleValidationMessage = null;
                StartInEditModeAppliedFor = BusinessEntityId;
            }

            if (!CanEditViewedVersion && IsEditMode)
            {
                IsEditMode = false;
                EditView = null;
                EditInitialPosition = null;
            }

            if (!IsEditMode)
            {
                EditableEntityName = RichTextDocumentHelper.FilterRichTextDocumentTitle(EntityName);
                TitleValidationMessage = null;
            }
        }

        private Task HandleEditRequestedAsync(RichTextDocumentViewportPosition? visiblePosition)
        {
            if (!CanEditViewedVersion)
            {
                return Task.CompletedTask;
            }

            EditInitialPosition = visiblePosition;
            ReadInitialPosition = null;
            IsEditMode = true;
            EditableEntityName = RichTextDocumentHelper.FilterRichTextDocumentTitle(EntityName);
            TitleValidationMessage = null;
            return Task.CompletedTask;
        }

        private Task HandleTitleChangedAsync(string? value)
        {
            EditableEntityName = RichTextDocumentHelper.FilterRichTextDocumentTitle(value);
            TitleValidationMessage = string.IsNullOrWhiteSpace(EditableEntityName)
                ? "Название не может быть пустым."
                : null;
            return Task.CompletedTask;
        }

        private async Task HandleSaveAsync()
        {
            await SaveEditorChangesAsync();
        }

        private async Task HandlePublishAsync()
        {
            if (!CanPublish)
            {
                return;
            }

            if (IsEditMode && !await SaveEditorChangesAsync())
            {
                return;
            }

            await OnPublishRequested.InvokeAsync();
        }

        private Task HandleDeleteAsync()
        {
            return CanDelete
                ? OnDeleteRequested.InvokeAsync()
                : Task.CompletedTask;
        }

        private Task HandlePublicChangedAsync(bool value)
        {
            return CanChangePublicFlag
                ? OnPublicChanged.InvokeAsync(value)
                : Task.CompletedTask;
        }

        private async Task HandleReadModeAsync(RichTextDocumentViewportPosition? visiblePosition)
        {
            if (await SaveEditorChangesAsync(refreshReadWindow: true, viewportPosition: visiblePosition))
            {
                ReadInitialPosition = visiblePosition;
                IsEditMode = false;
                EditView = null;
                EditInitialPosition = null;
                TitleValidationMessage = null;
            }
        }

        private async Task<bool> SaveEditorChangesAsync(
            bool refreshReadWindow = false,
            RichTextDocumentViewportPosition? viewportPosition = null)
        {
            if (IsSaving || EditView == null)
            {
                return false;
            }

            try
            {
                IsSaving = true;
                SaveError = null;
                TitleValidationMessage = null;

                try
                {
                    EditableEntityName = RichTextDocumentHelper.NormalizeRichTextDocumentTitle(EditableEntityName);
                }
                catch (ArgumentException ex)
                {
                    TitleValidationMessage = ex.Message;
                    return false;
                }

                var versionDescription = EditView.VersionDescription;
                var saved = await EditView.SaveAsync(EditableEntityName);
                await OnEditorSaved.InvokeAsync(new RichTextDocumentEditorSaveRequest
                {
                    SavedVersion = saved.Version,
                    TitleChanged = saved.TitleChanged,
                    SavedChunkCount = saved.SavedChunkCount,
                    Title = EditableEntityName,
                    VersionDescription = versionDescription,
                    RefreshReadWindow = refreshReadWindow,
                    ViewportPosition = viewportPosition
                });
                EditView.ClearVersionDescription();

                return true;
            }
            catch (Exception exception)
            {
                SaveError = BusinessEntity.Infrastructure.Bus.BusError.FromException(exception).Message;
                return false;
            }
            finally
            {
                IsSaving = false;
            }
        }
    }
}

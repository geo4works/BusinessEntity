using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.RichText;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.Services;
using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace BusinessEntity.Components
{
    public partial class RichTextDocumentWidgets : ComponentBase
    {
        [Parameter] public IReadOnlyList<RichTextDocumentBookmark> Bookmarks { get; set; } = Array.Empty<RichTextDocumentBookmark>();
        [Parameter] public Guid BusinessEntityId { get; set; }
        [Parameter] public Guid? ActiveBookmarkId { get; set; }
        [Parameter] public int VersionsRefreshToken { get; set; }
        [Parameter] public int ViewedVersion { get; set; } = 1;
        [Parameter] public int LatestVersion { get; set; } = 1;
        [Parameter] public int PublishedVersion { get; set; }
        [Parameter] public bool CanBrowseVersions { get; set; } = true;
        [Parameter] public bool CanRestoreVersion { get; set; }
        [Parameter] public bool CanEditVersionDescription { get; set; }
        [Parameter] public string VersionDescription { get; set; } = string.Empty;
        [Parameter] public EventCallback<string> OnSearchNext { get; set; }
        [Parameter] public EventCallback<string> OnSearchPrevious { get; set; }
        [Parameter] public EventCallback OnCreateBookmark { get; set; }
        [Parameter] public EventCallback<RichTextDocumentBookmark> OnBookmarkSelected { get; set; }
        [Parameter] public EventCallback<Guid> OnBookmarkDeleted { get; set; }
        [Parameter] public EventCallback<int> OnVersionSelected { get; set; }
        [Parameter] public EventCallback<string> OnVersionDescriptionChanged { get; set; }

        [Inject] public EntitySaveBatchFactory Batches { get; set; } = default!;
        [Inject] public IDataProviderConnector DataProviderConnector { get; set; } = default!;
        [Inject] public IActivityConnector ActivityConnector { get; set; } = default!;
        [Inject] public RichTextDocumentHelper RichTextDocumentHelper { get; set; } = default!;

        private string SearchQuery { get; set; } = string.Empty;
        private Guid? SelectedBookmarkId { get; set; }
        private RichTextDocumentWidgetTab ActiveTab { get; set; } = RichTextDocumentWidgetTab.Search;
        private IReadOnlyList<BusinessEntityDataVersionInfo> Versions { get; set; } = Array.Empty<BusinessEntityDataVersionInfo>();
        private int CommentsCount { get; set; }
        private RichTextDocumentChunkStatistics? Statistics { get; set; }
        private bool IsVersionsLoading { get; set; }
        private bool IsStatisticsLoading { get; set; }
        private string? VersionsError { get; set; }
        private string? StatisticsError { get; set; }
        private Guid _versionsLoadedForEntityId;
        private int _versionsLoadedForRefreshToken = -1;
        private Guid _commentsCountLoadedForEntityId;
        private long _commentsCountRequestId;
        private Guid _statisticsLoadedForEntityId;
        private int _statisticsLoadedForViewedVersion = -1;
        private int _statisticsLoadedForRefreshToken = -1;
        private Guid _statisticsLoadingForEntityId;
        private int _statisticsLoadingForViewedVersion = -1;
        private int _statisticsLoadingForRefreshToken = -1;
        private long _statisticsRequestId;
        private bool IsSearchDisabled => string.IsNullOrWhiteSpace(SearchQuery);
        private bool CanNavigateBookmarks => Bookmarks.Count > 0;
        private bool CanLoadStatistics => BusinessEntityId != Guid.Empty && ViewedVersion > 0;

        protected override void OnParametersSet()
        {
            if (ActiveBookmarkId.HasValue)
            {
                SelectedBookmarkId = ActiveBookmarkId;
            }
            else if (SelectedBookmarkId.HasValue && Bookmarks.All(x => x.Id != SelectedBookmarkId.Value))
            {
                SelectedBookmarkId = null;
            }
        }

        protected override async Task OnParametersSetAsync()
        {
            if (BusinessEntityId == Guid.Empty)
            {
                Versions = Array.Empty<BusinessEntityDataVersionInfo>();
                _versionsLoadedForEntityId = Guid.Empty;
                CommentsCount = 0;
                _commentsCountLoadedForEntityId = Guid.Empty;
                ResetStatistics();
                return;
            }

            QueueStatisticsLoad();

            if (BusinessEntityId != _versionsLoadedForEntityId ||
                VersionsRefreshToken != _versionsLoadedForRefreshToken)
            {
                await LoadVersionsAsync();
            }

            if (BusinessEntityId != _commentsCountLoadedForEntityId)
            {
                await LoadCommentsCountAsync();
            }
        }

        private void HandleSearchInput(ChangeEventArgs args)
        {
            SearchQuery = args.Value?.ToString() ?? string.Empty;
        }

        private Task HandleSearchNextAsync()
        {
            return OnSearchNext.InvokeAsync(SearchQuery);
        }

        private Task HandleSearchPreviousAsync()
        {
            return OnSearchPrevious.InvokeAsync(SearchQuery);
        }

        private Task HandleCreateBookmarkAsync()
        {
            return OnCreateBookmark.InvokeAsync();
        }

        private async Task SelectBookmarkAsync(RichTextDocumentBookmark bookmark)
        {
            SelectedBookmarkId = bookmark.Id;
            await OnBookmarkSelected.InvokeAsync(bookmark);
        }

        private Task HandleDeleteBookmarkAsync()
        {
            return SelectedBookmarkId.HasValue
                ? OnBookmarkDeleted.InvokeAsync(SelectedBookmarkId.Value)
                : Task.CompletedTask;
        }

        private Task HandlePreviousBookmarkAsync()
        {
            return NavigateBookmarkAsync(-1);
        }

        private Task HandleNextBookmarkAsync()
        {
            return NavigateBookmarkAsync(1);
        }

        private async Task NavigateBookmarkAsync(int direction)
        {
            if (Bookmarks.Count == 0)
            {
                return;
            }

            var currentIndex = SelectedBookmarkId.HasValue
                ? Bookmarks.ToList().FindIndex(x => x.Id == SelectedBookmarkId.Value)
                : -1;

            var nextIndex = currentIndex < 0
                ? (direction > 0 ? 0 : Bookmarks.Count - 1)
                : (currentIndex + direction + Bookmarks.Count) % Bookmarks.Count;

            await SelectBookmarkAsync(Bookmarks[nextIndex]);
        }

        private void SetActiveTab(RichTextDocumentWidgetTab tab)
        {
            ActiveTab = tab;
        }

        private Task RefreshStatisticsAsync()
        {
            return CanLoadStatistics
                ? StartStatisticsLoadAsync(force: true)
                : Task.CompletedTask;
        }

        private Task HandleVersionDescriptionInputAsync(ChangeEventArgs args)
        {
            VersionDescription = args.Value?.ToString() ?? string.Empty;
            return OnVersionDescriptionChanged.InvokeAsync(VersionDescription);
        }

        private async Task SelectVersionAsync(int version)
        {
            if (!CanBrowseVersions || version <= 0 || version == ViewedVersion)
            {
                return;
            }

            await OnVersionSelected.InvokeAsync(version);
        }

        private string GetTabButtonClass(RichTextDocumentWidgetTab tab)
        {
            return ActiveTab == tab
                ? "rich-text-document-tabs__tab rich-text-document-tabs__tab--active"
                : "rich-text-document-tabs__tab";
        }

        // Restore создаёт новую полную версию; просмотр и указатель публикации остаются явными.
        private async Task RestoreVersionAsync(int version)
        {
            if (!CanRestoreVersion || IsVersionsLoading) return;
            IsVersionsLoading = true; VersionsError = null;
            try
            {
                var result = await DataProviderConnector.SaveAsync(Batches.Create(BusinessEntityTypeEnum.RichTextDocument) with
                    { Restores = [new(BusinessEntityId, version)] });
                await LoadVersionsAsync();
                await OnVersionSelected.InvokeAsync(result.Versions.Single().Version);
            }
            catch (Exception exception) { VersionsError = exception.Message; }
            finally { IsVersionsLoading = false; }
        }

        private async Task LoadVersionsAsync()
        {
            IsVersionsLoading = true;
            VersionsError = null;

            try
            {
                var versions = await DataProviderConnector.GetDataVersionsAsync(BusinessEntityId);
                Versions = CanBrowseVersions
                    ? versions
                    : versions.Where(version => version.Version == ViewedVersion).ToList();
                _versionsLoadedForEntityId = BusinessEntityId;
                _versionsLoadedForRefreshToken = VersionsRefreshToken;
            }
            catch (Exception ex)
            {
                Versions = Array.Empty<BusinessEntityDataVersionInfo>();
                VersionsError = ex.Message;
            }
            finally
            {
                IsVersionsLoading = false;
            }
        }

        private async Task LoadCommentsCountAsync()
        {
            var entityId = BusinessEntityId;
            var requestId = ++_commentsCountRequestId;

            try
            {
                var comments = await ActivityConnector.GetCommentsAsync(entityId);
                if (BusinessEntityId != entityId || requestId != _commentsCountRequestId)
                {
                    return;
                }

                CommentsCount = comments.Count;
                _commentsCountLoadedForEntityId = entityId;
            }
            catch
            {
                if (BusinessEntityId == entityId && requestId == _commentsCountRequestId)
                {
                    CommentsCount = 0;
                    _commentsCountLoadedForEntityId = entityId;
                }
            }
        }

        private Task HandleCommentsCountChangedAsync(int count)
        {
            CommentsCount = Math.Max(0, count);
            _commentsCountLoadedForEntityId = BusinessEntityId;
            return Task.CompletedTask;
        }

        private void QueueStatisticsLoad()
        {
            if (!CanLoadStatistics)
            {
                ResetStatistics();
                return;
            }

            if (IsStatisticsLoadedForCurrentVersion() || IsStatisticsLoadingForCurrentVersion())
            {
                return;
            }

            _ = StartStatisticsLoadAsync(force: false);
        }

        private async Task StartStatisticsLoadAsync(bool force)
        {
            if (!CanLoadStatistics)
            {
                ResetStatistics();
                return;
            }

            if (!force && (IsStatisticsLoadedForCurrentVersion() || IsStatisticsLoadingForCurrentVersion()))
            {
                return;
            }

            var entityId = BusinessEntityId;
            var viewedVersion = ViewedVersion;
            var refreshToken = VersionsRefreshToken;
            var requestId = ++_statisticsRequestId;

            IsStatisticsLoading = true;
            StatisticsError = null;
            _statisticsLoadingForEntityId = entityId;
            _statisticsLoadingForViewedVersion = viewedVersion;
            _statisticsLoadingForRefreshToken = refreshToken;
            await InvokeAsync(StateHasChanged);

            try
            {
                var statistics = await RichTextDocumentHelper.GetChunkStatisticsAsync(entityId, viewedVersion);
                if (!IsCurrentStatisticsRequest(entityId, viewedVersion, refreshToken, requestId))
                {
                    return;
                }

                Statistics = statistics;
                _statisticsLoadedForEntityId = entityId;
                _statisticsLoadedForViewedVersion = viewedVersion;
                _statisticsLoadedForRefreshToken = refreshToken;
            }
            catch (Exception ex)
            {
                if (!IsCurrentStatisticsRequest(entityId, viewedVersion, refreshToken, requestId))
                {
                    return;
                }

                Statistics = null;
                StatisticsError = ex.Message;
            }
            finally
            {
                if (IsCurrentStatisticsRequest(entityId, viewedVersion, refreshToken, requestId))
                {
                    IsStatisticsLoading = false;
                    _statisticsLoadingForEntityId = Guid.Empty;
                    _statisticsLoadingForViewedVersion = -1;
                    _statisticsLoadingForRefreshToken = -1;
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        private bool IsStatisticsLoadedForCurrentVersion()
        {
            return BusinessEntityId == _statisticsLoadedForEntityId &&
                   ViewedVersion == _statisticsLoadedForViewedVersion &&
                   VersionsRefreshToken == _statisticsLoadedForRefreshToken &&
                   Statistics != null;
        }

        private bool IsStatisticsLoadingForCurrentVersion()
        {
            return IsStatisticsLoading &&
                   BusinessEntityId == _statisticsLoadingForEntityId &&
                   ViewedVersion == _statisticsLoadingForViewedVersion &&
                   VersionsRefreshToken == _statisticsLoadingForRefreshToken;
        }

        private bool IsCurrentStatisticsRequest(Guid entityId, int viewedVersion, int refreshToken, long requestId)
        {
            return requestId == _statisticsRequestId &&
                   entityId == BusinessEntityId &&
                   viewedVersion == ViewedVersion &&
                   refreshToken == VersionsRefreshToken;
        }

        private void ResetStatistics()
        {
            Statistics = null;
            StatisticsError = null;
            IsStatisticsLoading = false;
            _statisticsLoadedForEntityId = Guid.Empty;
            _statisticsLoadedForViewedVersion = -1;
            _statisticsLoadedForRefreshToken = -1;
            _statisticsLoadingForEntityId = Guid.Empty;
            _statisticsLoadingForViewedVersion = -1;
            _statisticsLoadingForRefreshToken = -1;
        }

        private static string FormatVersionDate(DateTime date)
        {
            return date.ToLocalTime().ToString("g");
        }

        private static string FormatInteger(int value)
        {
            return UseSpaceGroupSeparators(value.ToString("#,0", CultureInfo.InvariantCulture));
        }

        private static string FormatAverage(double value)
        {
            return UseSpaceGroupSeparators(value.ToString("#,0.0", CultureInfo.InvariantCulture));
        }

        private static string UseSpaceGroupSeparators(string value)
        {
            return value.Replace(",", " ");
        }

        private string GetBookmarkButtonClass(RichTextDocumentBookmark bookmark)
        {
            var isSelected = SelectedBookmarkId == bookmark.Id;
            return isSelected
                ? "rich-text-document-bookmarks__item rich-text-document-bookmarks__item--selected"
                : "rich-text-document-bookmarks__item";
        }

        private string GetVersionRowClass(BusinessEntityDataVersionInfo version)
        {
            var normalizedVersion = version.Version;
            var classes = new List<string> { "rich-text-document-versions__row" };

            if (normalizedVersion == ViewedVersion)
            {
                classes.Add("rich-text-document-versions__row--selected");
            }

            if (IsVersionPublished(normalizedVersion))
            {
                classes.Add("rich-text-document-versions__row--published");
            }

            return string.Join(" ", classes);
        }

        // Проверяет, является ли версия опубликованной версией документа.
        private bool IsVersionPublished(int version)
        {
            return PublishedVersion > 0 && version == PublishedVersion;
        }

        // Проверяет, является ли версия рабочим драфтом поверх опубликованной версии.
        private bool IsVersionDraft(int version)
        {
            return version == LatestVersion && !IsVersionPublished(version);
        }

        private enum RichTextDocumentWidgetTab
        {
            Search,
            Bookmarks,
            Versions,
            Statistics,
            Comments
        }
    }
}

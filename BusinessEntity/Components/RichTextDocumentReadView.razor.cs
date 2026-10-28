using BusinessEntity.Core.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace BusinessEntity.Components
{
    public partial class RichTextDocumentReadView : ComponentBase, IAsyncDisposable
    {
        private readonly string ViewportElementId = $"rich-text-read-viewport-{Guid.NewGuid():N}";
        private readonly string OutlineTreeElementId = $"rich-text-read-outline-tree-{Guid.NewGuid():N}";
        private bool _outlineTreeRegistered;

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
        [Parameter] public bool CanEdit { get; set; } = true;
        [Parameter] public bool CanDelete { get; set; }
        [Parameter] public bool CanPublish { get; set; }
        [Parameter] public bool CanChangePublicFlag { get; set; }
        [Parameter] public bool IsPublic { get; set; }
        [Parameter] public bool CanRestoreVersion { get; set; }
        [Parameter] public bool CanBrowseVersions { get; set; } = true;
        [Parameter] public IReadOnlyList<RichTextDocumentOutlineNode>? OutlineNodes { get; set; }
        [Parameter] public EventCallback<InputFileChangeEventArgs> OnImportSelected { get; set; }
        [Parameter] public EventCallback OnRebuildTableOfContents { get; set; }
        [Parameter] public RichTextDocumentViewportPosition? InitialTargetPosition { get; set; }
        [Parameter] public EventCallback<RichTextDocumentViewportPosition?> OnEditRequested { get; set; }
        [Parameter] public EventCallback OnPublishRequested { get; set; }
        [Parameter] public EventCallback OnDeleteRequested { get; set; }
        [Parameter] public EventCallback<bool> OnPublicChanged { get; set; }
        [Parameter] public EventCallback<int> OnVersionSelected { get; set; }

        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public RichTextDocumentSettingsService RichTextDocumentSettingsService { get; set; } = default!;
        [Inject] public RichTextDocumentHelper RichTextDocumentHelper { get; set; } = default!;
        [Inject] public IUserConnector UserConnector { get; set; } = default!;
        [Inject] public BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextUserMessages MessagePanel { get; set; } = default!;

        private IReadOnlyList<RichTextDocumentOutlineNode> LocalOutlineNodes { get; set; } = Array.Empty<RichTextDocumentOutlineNode>();
        private IReadOnlyList<RichTextDocumentOutlineNode> VisibleOutlineNodes { get; set; } = Array.Empty<RichTextDocumentOutlineNode>();
        private IReadOnlyList<RichTextDocumentBookmark> Bookmarks { get; set; } = Array.Empty<RichTextDocumentBookmark>();
        private RichTextDocumentViewport? Viewport { get; set; }
        private int DisplayLevelCount { get; set; } = 1;
        private bool HideTableOfContentsScrollbar { get; set; } = true;
        private Guid _bookmarksLoadedForEntityId;
        private Guid _displayLevelDocumentId;
        private Guid _displayLevelLoadedForEntityId;
        private Guid? ActiveBookmarkId { get; set; }

        private bool IsDocumentEmpty =>
            !IsInitialContentLoading &&
            (InitialChunkWindow == null ||
            InitialChunkWindow.TotalChunkCount == 0 ||
            (InitialChunkWindow.TotalChunkCount == 1 &&
             InitialChunkWindow.Chunks.All(chunk => string.IsNullOrWhiteSpace(chunk.HtmlCache))));

        protected override async Task OnInitializedAsync()
        {
            var settings = await RichTextDocumentSettingsService.GetSettingsAsync();
            HideTableOfContentsScrollbar = settings.HideTableOfContentsScrollbar;
        }

        protected override void OnParametersSet()
        {
            if (BusinessEntityId != _displayLevelDocumentId)
            {
                _displayLevelDocumentId = BusinessEntityId;
                _displayLevelLoadedForEntityId = Guid.Empty;
                DisplayLevelCount = 1;
            }

            LocalOutlineNodes = OutlineNodes ?? Array.Empty<RichTextDocumentOutlineNode>();
            VisibleOutlineNodes = FilterOutlineNodes(LocalOutlineNodes, DisplayLevelCount);
        }

        protected override async Task OnParametersSetAsync()
        {
            if (BusinessEntityId != Guid.Empty && BusinessEntityId != _bookmarksLoadedForEntityId)
            {
                await LoadBookmarksAsync();
            }

            if (BusinessEntityId != Guid.Empty && BusinessEntityId != _displayLevelLoadedForEntityId)
            {
                await LoadDisplayedLevelAsync();
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (VisibleOutlineNodes.Count == 0)
            {
                return;
            }

            if (!_outlineTreeRegistered)
            {
                await JS.InvokeVoidAsync("richTextOutlineViewport.registerViewport", OutlineTreeElementId);
                _outlineTreeRegistered = true;
            }
            else
            {
                await JS.InvokeVoidAsync("richTextOutlineViewport.syncViewportSize", OutlineTreeElementId);
            }
        }

        private Task HandleImportSelected(InputFileChangeEventArgs args)
        {
            if (!CanEdit)
            {
                return Task.CompletedTask;
            }

            return OnImportSelected.InvokeAsync(args);
        }

        private Task HandleRebuildTableOfContentsAsync()
        {
            if (!CanEdit)
            {
                return Task.CompletedTask;
            }

            return OnRebuildTableOfContents.InvokeAsync();
        }

        private Task HandlePublishAsync()
        {
            return CanPublish ? OnPublishRequested.InvokeAsync() : Task.CompletedTask;
        }

        private Task HandleDeleteAsync()
        {
            return CanDelete
                ? OnDeleteRequested.InvokeAsync()
                : Task.CompletedTask;
        }

        private Task HandlePublicChangedAsync(ChangeEventArgs args)
        {
            if (!CanChangePublicFlag)
            {
                return Task.CompletedTask;
            }

            var value = args.Value is bool boolValue && boolValue;
            return OnPublicChanged.InvokeAsync(value);
        }

        private async Task HandleDisplayLevelCountChangedAsync(int value)
        {
            DisplayLevelCount = Math.Clamp(value, 1, 3);
            VisibleOutlineNodes = FilterOutlineNodes(LocalOutlineNodes, DisplayLevelCount);
            if (BusinessEntityId != Guid.Empty)
            {
                await UserConnector.SaveRichDocDisplayedLevelAsync(BusinessEntityId, DisplayLevelCount);
                _displayLevelLoadedForEntityId = BusinessEntityId;
            }
        }

        private Task HandleHeadingSelectedAsync(RichTextDocumentOutlineNode node)
        {
            return Viewport?.ScrollToHeadingAsync(node.HeadingId, node.ChunkSortOrder) ?? Task.CompletedTask;
        }

        private async Task HandleEditAsync()
        {
            if (!CanEdit)
            {
                return;
            }

            var position = Viewport == null
                ? null
                : await Viewport.GetCurrentViewportPositionAsync();
            await OnEditRequested.InvokeAsync(position);
        }

        private Task HandleSearchNextAsync(string query)
        {
            return HandleSearchAsync(query, searchDown: true);
        }

        private Task HandleSearchPreviousAsync(string query)
        {
            return HandleSearchAsync(query, searchDown: false);
        }

        private async Task HandleSearchAsync(string query, bool searchDown)
        {
            if (Viewport == null || BusinessEntityId == Guid.Empty)
            {
                return;
            }

            var origin = await Viewport.GetCurrentViewportPositionAsync();
            var result = await RichTextDocumentHelper.FindTextAsync(
                BusinessEntityId,
                query,
                origin,
                searchDown,
                ViewedVersion);
            if (result == null)
            {
                AddMessage("Ничего не найдено.");
                return;
            }

            await Viewport.ScrollToPositionAsync(result.Position);
            AddMessage(string.IsNullOrWhiteSpace(result.Preview)
                ? "Найдено."
                : result.Preview);
        }

        private async Task HandleCreateBookmarkAsync()
        {
            if (Viewport == null || BusinessEntityId == Guid.Empty)
            {
                return;
            }

            var selection = await Viewport.GetCurrentTextSelectionAsync();
            var bookmark = await UserConnector.AddRichDocBookmarkAsync(BusinessEntityId, selection);
            if (bookmark == null)
            {
                AddMessage("Выделите текст в документе.");
                return;
            }

            ActiveBookmarkId = bookmark.Id;
            await LoadBookmarksAsync();
            AddMessage("Закладка создана.");
        }

        private async Task HandleBookmarkSelectedAsync(RichTextDocumentBookmark bookmark)
        {
            ActiveBookmarkId = bookmark.Id;
            if (Viewport != null)
            {
                await Viewport.ScrollToPositionAsync(bookmark.Position);
            }
        }

        private async Task HandleBookmarkDeletedAsync(Guid bookmarkId)
        {
            var deleted = await UserConnector.DeleteRichDocBookmarkAsync(bookmarkId);
            if (deleted && ActiveBookmarkId == bookmarkId)
            {
                ActiveBookmarkId = null;
            }

            await LoadBookmarksAsync();
            AddMessage(deleted ? "Закладка удалена." : "Закладка не найдена.");
        }

        private Task HandleVersionSelectedAsync(int version)
        {
            return OnVersionSelected.InvokeAsync(version);
        }

        private async Task LoadBookmarksAsync()
        {
            if (BusinessEntityId == Guid.Empty)
            {
                Bookmarks = Array.Empty<RichTextDocumentBookmark>();
                _bookmarksLoadedForEntityId = Guid.Empty;
                return;
            }

            Bookmarks = await UserConnector.GetRichDocBookmarksAsync(BusinessEntityId);
            _bookmarksLoadedForEntityId = BusinessEntityId;
        }

        private async Task LoadDisplayedLevelAsync()
        {
            if (BusinessEntityId == Guid.Empty)
            {
                DisplayLevelCount = 1;
                _displayLevelLoadedForEntityId = Guid.Empty;
                return;
            }

            DisplayLevelCount = Math.Clamp(
                await UserConnector.GetRichDocDisplayedLevelAsync(BusinessEntityId),
                1,
                3);
            _displayLevelLoadedForEntityId = BusinessEntityId;
            VisibleOutlineNodes = FilterOutlineNodes(LocalOutlineNodes, DisplayLevelCount);
        }

        private void AddMessage(string message)
        {
            MessagePanel.Add(BusinessEntityId, message);
        }

        private static IReadOnlyList<RichTextDocumentOutlineNode> FilterOutlineNodes(
            IReadOnlyList<RichTextDocumentOutlineNode> nodes,
            int maxLevel)
        {
            if (nodes.Count == 0)
            {
                return Array.Empty<RichTextDocumentOutlineNode>();
            }

            var result = new List<RichTextDocumentOutlineNode>();
            foreach (var node in nodes)
            {
                if (node.Level > maxLevel)
                {
                    continue;
                }

                result.Add(new RichTextDocumentOutlineNode
                {
                    HeadingId = node.HeadingId,
                    ChunkSortOrder = node.ChunkSortOrder,
                    Title = node.Title,
                    Level = node.Level,
                    IsExpanded = node.IsExpanded,
                    Children = FilterOutlineNodes(node.Children, maxLevel).ToList()
                });
            }

            return result;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_outlineTreeRegistered)
            {
                return;
            }

            await BusinessEntity.Infrastructure.UI.BrowserLifecycle.UnregisterAsync(JS, "richTextOutlineViewport.unregisterViewport", OutlineTreeElementId);
        }
    }
}

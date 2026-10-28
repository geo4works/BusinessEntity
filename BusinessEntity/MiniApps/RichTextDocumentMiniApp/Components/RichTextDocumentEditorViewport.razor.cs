using BusinessEntity.Services;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextPaste;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.WebLogger.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using ReactiveUI;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Components
{
    public partial class RichTextDocumentEditorViewport : ComponentBase, IAsyncDisposable
    {
        [CascadingParameter] public BusinessEntity.Infrastructure.Modularity.MiniAppModule Module { get; set; } = default!;
        [Inject] public RichTextWorkingSetFactory WorkingSets { get; set; } = default!;
        private RichTextWorkingSet Working => WorkingSets.Get(Module);
        private const double DefaultEstimatedChunkHeight = 1400;
        private const double MinEstimatedChunkHeight = 360;
        private const double MaxEstimatedChunkHeight = 8000;
        private const int ProgrammaticScrollSuppressionMs = 350;
        private const string ProgrammaticScrollBehavior = "auto";
        private readonly Dictionary<long, double> _chunkHeights = new();
        private Dictionary<long, RichTextEditorDraft> _dirtyDrafts => Working.Drafts;
        private HashSet<long> _dirtySortOrders => Working.DirtySortOrders;
        private DotNetObjectReference<RichTextDocumentEditorViewport>? _dotNetReference;
        private IDisposable? _mediaVideoUploadedSubscription;
        private IDisposable? _mediaVideoUploadFailedSubscription;
        private RichTextDocumentChunkWindow? _appliedInitialWindow;
        private RichTextDocumentSettings _settings = new();
        private readonly HashSet<string> _pendingVideoUploadTokens = new(StringComparer.OrdinalIgnoreCase);
        private bool _viewportRegistered;
        private bool _isLoadingWindow;
        private bool _pendingEditorSync;
        private bool _initialEditWindowChecked;
        private long _loadVersion;
        private double? _lastScrollTop;
        private string? _pendingAnchor;
        private long? _pendingVisibleChunkSortOrder;
        private RichTextDocumentViewportPosition? _pendingViewportPosition;

        [Inject] public RichTextDocumentReader RichTextDocumentReader { get; set; } = default!;
        [Inject] public RichTextEditService RichTextEditService { get; set; } = default!;
        [Parameter] public string ViewportElementId { get; set; } = string.Empty;
        [Parameter] public Guid BusinessEntityId { get; set; }
        [Parameter] public RichTextDocumentChunkWindow? InitialWindow { get; set; }
        [Parameter] public IReadOnlyList<RichTextDocumentOutlineNode> OutlineNodes { get; set; } = Array.Empty<RichTextDocumentOutlineNode>();
        [Parameter] public bool IsInitialContentLoading { get; set; }
        [Parameter] public RichTextDocumentViewportPosition? InitialTargetPosition { get; set; }

        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public RichTextDocumentSettingsService RichTextDocumentSettingsService { get; set; } = default!;
        [Inject] public IUserContextService UserContextService { get; set; } = default!;
        [Inject] public IMessageBus MessageBus { get; set; } = default!;
        [Inject] public RichTextClipboardImportHelper ClipboardImportHelper { get; set; } = default!;
        [Inject] public IWebLoggerService? WebLogger { get; set; }

        private IReadOnlyList<BusinessEntityDataChunk> LoadedChunks { get; set; } = Array.Empty<BusinessEntityDataChunk>();
        private int TotalChunkCount { get; set; }
        private double EstimatedChunkHeight { get; set; } = DefaultEstimatedChunkHeight;
        private double TopSpacerPx { get; set; }
        private double BottomSpacerPx { get; set; }
        private int _baseDocumentVersion { get => Working.BaseVersion; set => Working.BaseVersion = value; }
        private Guid _baseEntityId;
        private bool HasDirtyDrafts => _dirtyDrafts.Count > 0 || _dirtySortOrders.Count > 0;
        private bool IsEmbedPickerOpen { get; set; }
        private string TopSpacerStyle => $"height: {Math.Max(TopSpacerPx, 0):0.##}px;";
        private string BottomSpacerStyle => $"height: {Math.Max(BottomSpacerPx, 0):0.##}px;";

        protected override async Task OnInitializedAsync()
        {
            _settings = await RichTextDocumentSettingsService.GetSettingsAsync();
            _mediaVideoUploadedSubscription = MessageBus
                .Listen<MediaVideoUploadedMessage>()
                .Subscribe(message => _ = HandleMediaVideoUploadedMessageAsync(message));
            _mediaVideoUploadFailedSubscription = MessageBus
                .Listen<MediaVideoUploadFailedMessage>()
                .Subscribe(message => _ = HandleMediaVideoUploadFailedMessageAsync(message));
        }

        protected override void OnParametersSet()
        {
            if (_baseEntityId != BusinessEntityId) { _baseEntityId = BusinessEntityId; Working.Open(BusinessEntityId, Working.Title); }
            if (_baseDocumentVersion == 0 && InitialWindow != null)
            {
                if (InitialWindow.DocumentVersion < 1) throw new InvalidDataException("Не задана база редактора.");
                _baseDocumentVersion = InitialWindow.DocumentVersion;
            }
            if (ReferenceEquals(InitialWindow, _appliedInitialWindow))
            {
                return;
            }

            _appliedInitialWindow = InitialWindow;
            ApplyWindow(InitialWindow);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (string.IsNullOrWhiteSpace(ViewportElementId))
            {
                return;
            }

            if (firstRender)
            {
                _dotNetReference = DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("richTextEditViewport.registerVirtualViewport", ViewportElementId, _dotNetReference);
                _viewportRegistered = true;
            }
            else
            {
                await JS.InvokeVoidAsync("richTextEditViewport.syncViewportSize", ViewportElementId);
            }

            if (!_initialEditWindowChecked)
            {
                _initialEditWindowChecked = true;
                if (InitialTargetPosition != null)
                {
                    var targetSortOrder = Math.Max(InitialTargetPosition.ChunkSortOrder, 0);
                    if (!IsChunkLoaded(targetSortOrder) && BusinessEntityId != Guid.Empty && !_isLoadingWindow)
                    {
                        await LoadWindowAroundAsync(targetSortOrder, pendingAnchor: null, pendingViewportPosition: InitialTargetPosition);
                        return;
                    }

                    _pendingViewportPosition = InitialTargetPosition;
                }

                if (BusinessEntityId != Guid.Empty &&
                    InitialTargetPosition == null &&
                    LoadedChunks.Count < _settings.GetEditChunksOnOpen() &&
                    !_isLoadingWindow)
                {
                    await LoadWindowAsync(0, _settings.GetEditChunksOnOpen(), pendingAnchor: null);
                    return;
                }
            }

            if (_pendingEditorSync)
            {
                _pendingEditorSync = false;
                await SyncEditorsAsync();
            }

            await JS.InvokeVoidAsync("richTextEditViewport.measureChunks", ViewportElementId);

            if (!string.IsNullOrWhiteSpace(_pendingAnchor))
            {
                var anchor = _pendingAnchor;
                _pendingAnchor = null;
                var scrolledToHeading = await ScrollToHeadingInViewportAsync(anchor);
                if (!scrolledToHeading && _pendingVisibleChunkSortOrder.HasValue)
                {
                    await ScrollToChunkInViewportAsync(_pendingVisibleChunkSortOrder.Value);
                }
            }
            else if (_pendingVisibleChunkSortOrder.HasValue)
            {
                await EnsureChunkVisibleInViewportAsync(_pendingVisibleChunkSortOrder.Value);
            }
            else if (_pendingViewportPosition != null)
            {
                await ScrollToBlockInViewportAsync(_pendingViewportPosition);
            }

            _pendingVisibleChunkSortOrder = null;
            _pendingViewportPosition = null;
        }

        // Сохраняет заголовок и чанки одним пакетом от фиксированной базы окна редактора.
        public async Task<RichTextDocumentSaveResult> SaveAsync(string title, string description)
        {
            await CaptureCurrentEditorsAsync();
            Working.SetTitle(title); Working.SetDescription(description);
            var hadChanges = Module.SaveContext!.IsDirty;
            var result = await Module.SaveContext.SaveAsync(Module.ModuleId);
            if (!result.Success) throw new InvalidOperationException(result.Error);
            var saved = hadChanges ? Working.LastSave! : new RichTextDocumentSaveResult(_baseDocumentVersion, 0, false, false);
            if (_viewportRegistered) await JS.InvokeVoidAsync("richTextEditor.markClean", ViewportElementId, Working.AcknowledgedHtml.Select(x => new { sortOrder = x.Key, html = x.Value }).ToArray());
            await InvokeAsync(StateHasChanged);
            return saved;
        }

        public async Task ScrollToHeadingAsync(string headingId, long chunkSortOrder)
        {
            if (string.IsNullOrWhiteSpace(headingId))
            {
                return;
            }

            if (IsChunkLoaded(chunkSortOrder))
            {
                var scrolledToHeading = await ScrollToHeadingInViewportAsync(headingId);
                if (!scrolledToHeading)
                {
                    await ScrollToChunkInViewportAsync(chunkSortOrder);
                }

                return;
            }

            await LoadWindowAroundAsync(chunkSortOrder, headingId);
        }

        public async Task<RichTextDocumentViewportPosition?> GetCurrentViewportPositionAsync()
        {
            if (!_viewportRegistered || string.IsNullOrWhiteSpace(ViewportElementId))
            {
                return null;
            }

            try
            {
                return await JS.InvokeAsync<RichTextDocumentViewportPosition?>(
                    "richTextEditViewport.getCurrentViewportPosition",
                    ViewportElementId);
            }
            catch (JSException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public async Task<RichTextDocumentTextSelection?> GetCurrentTextSelectionAsync()
        {
            if (!_viewportRegistered || string.IsNullOrWhiteSpace(ViewportElementId))
            {
                return null;
            }

            try
            {
                return await JS.InvokeAsync<RichTextDocumentTextSelection?>(
                    "richTextEditViewport.getCurrentTextSelection",
                    ViewportElementId);
            }
            catch (JSException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public async Task ScrollToPositionAsync(RichTextDocumentViewportPosition? position)
        {
            if (position == null)
            {
                return;
            }

            var targetSortOrder = Math.Max(position.ChunkSortOrder, 0);
            if (IsChunkLoaded(targetSortOrder))
            {
                var scrolled = await ScrollToBlockInViewportAsync(position);
                if (!scrolled)
                {
                    await ScrollToChunkInViewportAsync(targetSortOrder);
                }

                return;
            }

            await LoadWindowAroundAsync(targetSortOrder, pendingAnchor: null, pendingViewportPosition: position);
        }

        private Task RunEditorCommandAsync(string command)
        {
            return JS.InvokeVoidAsync("richTextEditor.runCommand", ViewportElementId, command).AsTask();
        }

        private async Task OpenEmbedPickerAsync()
        {
            IsEmbedPickerOpen = true;
            await Task.CompletedTask;
        }

        private Task CloseEmbedPickerAsync()
        {
            IsEmbedPickerOpen = false;
            return Task.CompletedTask;
        }

        private async Task InsertVideoAsync(MediaVideoInfo video)
        {
            if (video == null)
            {
                return;
            }

            await JS.InvokeVoidAsync(
                "richTextEditor.insertVideo",
                ViewportElementId,
                video.Id.ToString("D"),
                video.DisplayName,
                video.EmbedUrl);
            IsEmbedPickerOpen = false;
        }

        private async Task<string?> InsertUploadingVideoAsync(MediaVideoFileSelection selection)
        {
            if (selection == null)
            {
                return null;
            }

            var uploadToken = Guid.NewGuid().ToString("N");
            _pendingVideoUploadTokens.Add(uploadToken);
            var title = Path.GetFileNameWithoutExtension(selection.FileName);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = selection.FileName;
            }

            await JS.InvokeVoidAsync(
                "richTextEditor.insertVideoUploadPlaceholder",
                ViewportElementId,
                uploadToken,
                title);

            return uploadToken;
        }

        private Task HandleMediaVideoUploadedMessageAsync(MediaVideoUploadedMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.ClientUploadToken) || message.Video == null)
            {
                return Task.CompletedTask;
            }

            if (!_pendingVideoUploadTokens.Remove(message.ClientUploadToken))
            {
                return Task.CompletedTask;
            }

            return InvokeAsync(() => JS.InvokeVoidAsync(
                "richTextEditor.completeVideoUploadPlaceholder",
                ViewportElementId,
                message.ClientUploadToken,
                message.Video.Id.ToString("D"),
                message.Video.DisplayName,
                message.Video.EmbedUrl).AsTask());
        }

        private Task HandleMediaVideoUploadFailedMessageAsync(MediaVideoUploadFailedMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.ClientUploadToken))
            {
                return Task.CompletedTask;
            }

            if (!_pendingVideoUploadTokens.Remove(message.ClientUploadToken))
            {
                return Task.CompletedTask;
            }

            return InvokeAsync(() => JS.InvokeVoidAsync(
                "richTextEditor.failVideoUploadPlaceholder",
                ViewportElementId,
                message.ClientUploadToken,
                message.ErrorMessage).AsTask());
        }

        [JSInvokable]
        public Task OnEditorChunkDirty(long sortOrder)
        {
            if (sortOrder >= 0)
            {
                Working.MarkChunkChanged(sortOrder);
            }

            return Task.CompletedTask;
        }

        [JSInvokable]
        public async Task OnEditorChunkEdited(EditorChunkSnapshot snapshot, bool shouldRefreshDirtyState)
        {
            if (snapshot == null || snapshot.SortOrder < 0 || !snapshot.IsDirty)
            {
                return;
            }

            var wasAlreadyCached = _dirtyDrafts.ContainsKey(snapshot.SortOrder);
            PutDirtyDraft(snapshot);

            await LogEditorViewportAsync(
                "[rich-doc-edit-chunk-cache-put] " +
                $"chunkId={snapshot.ChunkId:D} sortOrder={snapshot.SortOrder} " +
                $"htmlLength={(snapshot.Html?.Length ?? 0)} originalHtmlLength={(snapshot.OriginalHtml?.Length ?? 0)} " +
                $"source=edit wasAlreadyCached={wasAlreadyCached} dirtyCache={_dirtyDrafts.Count}");

            if (shouldRefreshDirtyState)
            {
                _pendingEditorSync = true;
                await InvokeAsync(StateHasChanged);
            }
        }

        [JSInvokable]
        public Task OnEditorChunkDisposed(string chunkId, long sortOrder, bool isDirty, string reason)
        {
            return LogEditorViewportAsync(
                "[rich-doc-edit-chunk-dispose] " +
                $"chunkId={chunkId} sortOrder={sortOrder} " +
                $"isDirty={isDirty} reason={reason} dirtyCache={_dirtyDrafts.Count} " +
                $"dirtySortOrders={FormatDirtySortOrders()}");
        }

        [JSInvokable]
        public Task OnTableDebug(string message)
        {
            var safeMessage = message ?? string.Empty;
            if (safeMessage.Length > 4000)
            {
                safeMessage = safeMessage[..4000] + "...";
            }

            return LogEditorViewportAsync(
                "[rich-doc-table] " +
                $"entityId={BusinessEntityId:D} viewport={ViewportElementId} " +
                safeMessage);
        }

        // Конвертирует clipboard Markdown/HTML через server-side import pipeline и возвращает editor HTML.
        [JSInvokable]
        public async Task<RichTextClipboardPasteResult> OnClipboardPasteAsync(RichTextClipboardPasteRequest request)
        {
            try
            {
                return await ClipboardImportHelper.ConvertAsync(BusinessEntityId, request);
            }
            catch (Exception ex)
            {
                await LogEditorViewportAsync(
                    "[rich-doc-clipboard-paste] " +
                    $"entityId={BusinessEntityId:D} error={ex.Message}");
                return new RichTextClipboardPasteResult
                {
                    Handled = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        [JSInvokable]
        public async Task OnVirtualViewportScrolled(double scrollTop, double clientHeight, double scrollHeight)
        {
            if (_isLoadingWindow || BusinessEntityId == Guid.Empty || TotalChunkCount <= 0)
            {
                _lastScrollTop = scrollTop;
                return;
            }

            var isScrollingUp = _lastScrollTop.HasValue && scrollTop < _lastScrollTop.Value;
            _lastScrollTop = scrollTop;

            if (isScrollingUp && await TryLoadPreviousWindowAsync(scrollTop, clientHeight))
            {
                return;
            }

            var targetSortOrder = EstimateSortOrderForOffset(scrollTop + Math.Max(clientHeight * 0.35, 0));
            if (IsChunkLoaded(targetSortOrder))
            {
                return;
            }

            var desiredStart = ClampStartSortOrder(
                targetSortOrder - _settings.GetEditChunksBeforeFocused(),
                _settings.GetEditWindowChunkCount());
            await LoadWindowAsync(
                desiredStart,
                _settings.GetEditWindowChunkCount(),
                pendingAnchor: null,
                pendingVisibleChunkSortOrder: targetSortOrder,
                pendingViewportPosition: null);
        }

        [JSInvokable]
        public async Task OnVirtualViewportScrollbarReleased(double scrollTop, double clientHeight, double scrollHeight)
        {
            if (_isLoadingWindow || BusinessEntityId == Guid.Empty || TotalChunkCount <= 0)
            {
                _lastScrollTop = scrollTop;
                return;
            }

            _lastScrollTop = scrollTop;

            var targetSortOrder = EstimateSortOrderForScrollbarPosition(scrollTop, clientHeight, scrollHeight);
            var targetOutlineNode = FindNearestOutlineNode(targetSortOrder);
            if (targetOutlineNode != null)
            {
                await ScrollToHeadingAsync(targetOutlineNode.HeadingId, targetOutlineNode.ChunkSortOrder);
                return;
            }

            await LoadWindowAroundAsync(targetSortOrder, pendingAnchor: null);
        }

        [JSInvokable]
        public Task OnChunkHeightsMeasured(RichTextDocumentViewport.ChunkHeightMeasurement[] measurements)
        {
            if (measurements == null || measurements.Length == 0)
            {
                return Task.CompletedTask;
            }

            var changed = false;
            foreach (var measurement in measurements)
            {
                if (measurement.Height <= 0)
                {
                    continue;
                }

                if (!_chunkHeights.TryGetValue(measurement.SortOrder, out var previous) ||
                    Math.Abs(previous - measurement.Height) > 1)
                {
                    _chunkHeights[measurement.SortOrder] = measurement.Height;
                    changed = true;
                }
            }

            if (!changed)
            {
                return Task.CompletedTask;
            }

            RecalculateEstimatedChunkHeight();
            RecalculateSpacers();
            _pendingEditorSync = true;
            return InvokeAsync(StateHasChanged);
        }

        private async Task<bool> TryLoadPreviousWindowAsync(double scrollTop, double clientHeight)
        {
            if (LoadedChunks.Count == 0)
            {
                return false;
            }

            var firstLoadedSortOrder = LoadedChunks[0].SortOrder;
            if (firstLoadedSortOrder <= 0)
            {
                return false;
            }

            var firstLoadedOffset = EstimateRangeHeight(0, firstLoadedSortOrder);
            var preloadThreshold = Math.Max(clientHeight * 0.15, 48);
            if (scrollTop > firstLoadedOffset + preloadThreshold)
            {
                return false;
            }

            var targetSortOrder = firstLoadedSortOrder - 1;
            var desiredStart = ClampStartSortOrder(
                targetSortOrder - _settings.GetEditChunksBeforeFocused(),
                _settings.GetEditWindowChunkCount());
            await LoadWindowAsync(
                desiredStart,
                _settings.GetEditWindowChunkCount(),
                pendingAnchor: null,
                pendingVisibleChunkSortOrder: targetSortOrder,
                pendingViewportPosition: null);
            return true;
        }

        private Task LoadWindowAroundAsync(long targetSortOrder, string? pendingAnchor)
        {
            return LoadWindowAroundAsync(targetSortOrder, pendingAnchor, pendingViewportPosition: null);
        }

        private async Task LoadWindowAroundAsync(
            long targetSortOrder,
            string? pendingAnchor,
            RichTextDocumentViewportPosition? pendingViewportPosition)
        {
            var start = ClampStartSortOrder(
                targetSortOrder - _settings.GetEditChunksBeforeFocused(),
                _settings.GetEditWindowChunkCount());
            await LoadWindowAsync(
                start,
                _settings.GetEditWindowChunkCount(),
                pendingAnchor,
                pendingVisibleChunkSortOrder: pendingViewportPosition == null ? targetSortOrder : null,
                pendingViewportPosition: pendingViewportPosition);
        }

        private async Task LoadWindowAsync(
            long startSortOrder,
            int take,
            string? pendingAnchor,
            long? pendingVisibleChunkSortOrder = null,
            RichTextDocumentViewportPosition? pendingViewportPosition = null)
        {
            if (_isLoadingWindow)
            {
                return;
            }

            var version = ++_loadVersion;
            _isLoadingWindow = true;

            try
            {
                await LogEditorViewportAsync(
                    "[rich-doc-edit-window-request] " +
                    $"startSortOrder={startSortOrder} take={take} " +
                    $"loaded={FormatLoadedWindow()} dirtyCache={_dirtyDrafts.Count}");

                await CaptureCurrentEditorsAsync();
                var window = await GetChunkWindowWithCacheAsync(startSortOrder, take);

                if (version != _loadVersion)
                {
                    return;
                }

                ApplyWindow(window);
                _pendingAnchor = pendingAnchor;
                _pendingVisibleChunkSortOrder = pendingVisibleChunkSortOrder;
                _pendingViewportPosition = pendingViewportPosition;
                await LogEditorViewportAsync(
                    "[rich-doc-edit-window-loaded] " +
                    $"startSortOrder={window.StartSortOrder} " +
                    $"chunks={string.Join(",", window.Chunks.Select(chunk => chunk.SortOrder))} " +
                    $"totalChunks={window.TotalChunkCount} dirtyCache={_dirtyDrafts.Count}");
                await InvokeAsync(StateHasChanged);
            }
            finally
            {
                if (version == _loadVersion)
                {
                    _isLoadingWindow = false;
                }
            }
        }

        private async Task<RichTextDocumentChunkWindow> GetChunkWindowWithCacheAsync(long startSortOrder, int take)
        {
            if (LoadedChunks.Count == 0 || TotalChunkCount <= 0)
            {
                return await RichTextDocumentReader.GetChunkWindowAsync(
                    BusinessEntityId,
                    startSortOrder,
                    take,
                    _baseDocumentVersion);
            }

            var normalizedTake = Math.Max(take, 0);
            var endExclusive = Math.Min(startSortOrder + normalizedTake, TotalChunkCount);
            if (normalizedTake == 0 || endExclusive <= startSortOrder)
            {
                return new RichTextDocumentChunkWindow
                {
                    BusinessEntityId = BusinessEntityId,
                    StartSortOrder = startSortOrder,
                    TotalChunkCount = TotalChunkCount,
                    Chunks = Array.Empty<BusinessEntityDataChunk>()
                };
            }

            var cachedChunks = LoadedChunks
                .Where(chunk => chunk.SortOrder >= startSortOrder && chunk.SortOrder < endExclusive)
                .GroupBy(chunk => chunk.SortOrder)
                .ToDictionary(group => group.Key, group => group.Last());
            var fetchedChunks = new Dictionary<long, BusinessEntityDataChunk>();
            var totalChunkCount = TotalChunkCount;

            long? missingStart = null;
            for (var sortOrder = startSortOrder; sortOrder < endExclusive; sortOrder++)
            {
                if (!cachedChunks.ContainsKey(sortOrder) && !_dirtyDrafts.ContainsKey(sortOrder))
                {
                    missingStart ??= sortOrder;
                    continue;
                }

                if (missingStart.HasValue)
                {
                    totalChunkCount = await FetchMissingRangeAsync(missingStart.Value, sortOrder, fetchedChunks, totalChunkCount);
                    missingStart = null;
                }
            }

            if (missingStart.HasValue)
            {
                totalChunkCount = await FetchMissingRangeAsync(missingStart.Value, endExclusive, fetchedChunks, totalChunkCount);
            }

            var chunks = new List<BusinessEntityDataChunk>();
            for (var sortOrder = startSortOrder; sortOrder < endExclusive; sortOrder++)
            {
                if (_dirtyDrafts.TryGetValue(sortOrder, out var draft))
                {
                    chunks.Add(new BusinessEntityDataChunk
                    {
                        Id = draft.ChunkId,
                        BusinessEntityId = BusinessEntityId,
                        SortOrder = sortOrder,
                        HtmlCache = draft.CurrentHtml,
                        CharCount = draft.CurrentHtml.Length
                    });
                }
                else if (cachedChunks.TryGetValue(sortOrder, out var cachedChunk))
                {
                    chunks.Add(cachedChunk);
                }
                else if (fetchedChunks.TryGetValue(sortOrder, out var fetchedChunk))
                {
                    chunks.Add(fetchedChunk);
                }
            }

            return new RichTextDocumentChunkWindow
            {
                BusinessEntityId = BusinessEntityId,
                StartSortOrder = startSortOrder,
                TotalChunkCount = totalChunkCount,
                Chunks = chunks
            };
        }

        private async Task<int> FetchMissingRangeAsync(
            long startSortOrder,
            long endExclusive,
            Dictionary<long, BusinessEntityDataChunk> fetchedChunks,
            int fallbackTotalChunkCount)
        {
            var take = (int)Math.Max(endExclusive - startSortOrder, 0);
            if (take <= 0)
            {
                return fallbackTotalChunkCount;
            }

            var window = await RichTextDocumentReader.GetChunkWindowAsync(
                BusinessEntityId,
                startSortOrder,
                take,
                _baseDocumentVersion);

            foreach (var chunk in window.Chunks)
            {
                fetchedChunks[chunk.SortOrder] = chunk;
            }

            return window.TotalChunkCount > 0 ? window.TotalChunkCount : fallbackTotalChunkCount;
        }

        private void ApplyWindow(RichTextDocumentChunkWindow? window)
        {
            if (window == null)
            {
                LoadedChunks = Array.Empty<BusinessEntityDataChunk>();
                TotalChunkCount = 0;
                TopSpacerPx = 0;
                BottomSpacerPx = 0;
                _lastScrollTop = null;
                _pendingEditorSync = true;
                return;
            }

            LoadedChunks = window.Chunks ?? Array.Empty<BusinessEntityDataChunk>();
            TotalChunkCount = window.TotalChunkCount;
            RecalculateSpacers();
            _lastScrollTop = null;
            _pendingEditorSync = true;
        }

        private async Task CaptureCurrentEditorsAsync()
        {
            if (!_viewportRegistered || string.IsNullOrWhiteSpace(ViewportElementId))
            {
                return;
            }

            EditorChunkSnapshot[] snapshots;
            try
            {
                snapshots = await JS.InvokeAsync<EditorChunkSnapshot[]>("richTextEditor.collectEditors", ViewportElementId);
            }
            catch (JSException)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            var dirtySnapshotCount = 0;
            foreach (var snapshot in snapshots ?? Array.Empty<EditorChunkSnapshot>())
            {
                if (snapshot.SortOrder < 0)
                {
                    continue;
                }

                if (snapshot.IsDirty)
                {
                    dirtySnapshotCount++;
                    var wasAlreadyCached = _dirtyDrafts.ContainsKey(snapshot.SortOrder);
                    PutDirtyDraft(snapshot);

                    await LogEditorViewportAsync(
                        "[rich-doc-edit-chunk-cache-put] " +
                        $"chunkId={snapshot.ChunkId:D} sortOrder={snapshot.SortOrder} " +
                        $"htmlLength={(snapshot.Html?.Length ?? 0)} originalHtmlLength={(snapshot.OriginalHtml?.Length ?? 0)} " +
                        $"source=capture wasAlreadyCached={wasAlreadyCached} dirtyCache={_dirtyDrafts.Count}");
                }
                else
                {
                    _dirtySortOrders.Remove(snapshot.SortOrder);
                    _dirtyDrafts.Remove(snapshot.SortOrder);
                }
            }

            await LogEditorViewportAsync(
                "[rich-doc-edit-capture] " +
                $"snapshots={(snapshots?.Length ?? 0)} " +
                $"dirtySnapshots={dirtySnapshotCount} dirtyCache={_dirtyDrafts.Count} " +
                $"dirtySortOrders={FormatDirtySortOrders()}");
        }

        private void PutDirtyDraft(EditorChunkSnapshot snapshot)
        {
            Working.Put(new RichTextEditorDraft
            {
                ChunkId = snapshot.ChunkId, SortOrder = snapshot.SortOrder,
                OriginalHtml = snapshot.OriginalHtml ?? string.Empty, CurrentHtml = snapshot.Html ?? string.Empty
            });
        }

        private async Task<bool> ScrollToHeadingInViewportAsync(string headingId)
        {
            _lastScrollTop = null;
            return await JS.InvokeAsync<bool>(
                "richTextEditViewport.scrollToHeading",
                ViewportElementId,
                headingId,
                ProgrammaticScrollBehavior,
                ProgrammaticScrollSuppressionMs);
        }

        private async Task<bool> ScrollToChunkInViewportAsync(long sortOrder)
        {
            _lastScrollTop = null;
            return await JS.InvokeAsync<bool>(
                "richTextEditViewport.scrollToChunk",
                ViewportElementId,
                sortOrder,
                ProgrammaticScrollBehavior,
                ProgrammaticScrollSuppressionMs);
        }

        private async Task<bool> EnsureChunkVisibleInViewportAsync(long sortOrder)
        {
            _lastScrollTop = null;
            return await JS.InvokeAsync<bool>(
                "richTextEditViewport.ensureChunkVisible",
                ViewportElementId,
                sortOrder,
                ProgrammaticScrollBehavior,
                ProgrammaticScrollSuppressionMs);
        }

        private async Task<bool> ScrollToBlockInViewportAsync(RichTextDocumentViewportPosition position)
        {
            _lastScrollTop = null;
            return await JS.InvokeAsync<bool>(
                "richTextEditViewport.scrollToBlock",
                ViewportElementId,
                position.ChunkSortOrder,
                position.BlockIndex,
                ProgrammaticScrollBehavior,
                ProgrammaticScrollSuppressionMs);
        }

        private Task SyncEditorsAsync()
        {
            if (!_viewportRegistered || string.IsNullOrWhiteSpace(ViewportElementId))
            {
                return Task.CompletedTask;
            }

            var payload = LoadedChunks.Select(chunk =>
            {
                var hasDraft = _dirtyDrafts.TryGetValue(chunk.SortOrder, out var draft);
                return new
                {
                    chunkId = chunk.Id,
                    sortOrder = chunk.SortOrder,
                    html = hasDraft ? draft!.CurrentHtml : chunk.HtmlCache,
                    originalHtml = hasDraft ? draft!.OriginalHtml : chunk.HtmlCache,
                    isDraft = hasDraft
                };
            }).ToArray();

            return JS.InvokeVoidAsync(
                "richTextEditor.syncEditors",
                ViewportElementId,
                payload,
                _dotNetReference,
                BusinessEntityId.ToString("D")).AsTask();
        }

        private void RecalculateSpacers()
        {
            if (LoadedChunks.Count == 0 || TotalChunkCount <= 0)
            {
                TopSpacerPx = 0;
                BottomSpacerPx = 0;
                return;
            }

            var first = LoadedChunks[0].SortOrder;
            var last = LoadedChunks[^1].SortOrder;
            TopSpacerPx = EstimateRangeHeight(0, first);
            BottomSpacerPx = EstimateRangeHeight(last + 1, TotalChunkCount);
        }

        private double EstimateRangeHeight(long startInclusive, long endExclusive)
        {
            if (endExclusive <= startInclusive)
            {
                return 0;
            }

            var count = endExclusive - startInclusive;
            var height = count * EstimatedChunkHeight;
            foreach (var pair in _chunkHeights)
            {
                if (pair.Key >= startInclusive && pair.Key < endExclusive)
                {
                    height += pair.Value - EstimatedChunkHeight;
                }
            }

            return Math.Max(height, 0);
        }

        private long EstimateSortOrderForOffset(double offset)
        {
            if (TotalChunkCount <= 0)
            {
                return 0;
            }

            var sortOrder = (long)Math.Floor(Math.Max(offset, 0) / Math.Max(EstimatedChunkHeight, 1));
            return Math.Clamp(sortOrder, 0, TotalChunkCount - 1);
        }

        private long EstimateSortOrderForScrollbarPosition(double scrollTop, double clientHeight, double scrollHeight)
        {
            if (TotalChunkCount <= 0)
            {
                return 0;
            }

            var maxScrollTop = Math.Max(scrollHeight - clientHeight, 1);
            var position = Math.Clamp(scrollTop / maxScrollTop, 0, 1);
            var sortOrder = (long)Math.Round(position * (TotalChunkCount - 1));
            return Math.Clamp(sortOrder, 0, TotalChunkCount - 1);
        }

        private RichTextDocumentOutlineNode? FindNearestOutlineNode(long targetSortOrder)
        {
            var flatNodes = FlattenOutlineNodes(OutlineNodes)
                .OrderBy(node => node.ChunkSortOrder)
                .ThenBy(node => node.Level)
                .ToList();
            if (flatNodes.Count == 0)
            {
                return null;
            }

            var nearestPrevious = flatNodes.LastOrDefault(node => node.ChunkSortOrder <= targetSortOrder);
            return nearestPrevious ?? flatNodes[0];
        }

        private static IEnumerable<RichTextDocumentOutlineNode> FlattenOutlineNodes(IEnumerable<RichTextDocumentOutlineNode>? nodes)
        {
            if (nodes == null)
            {
                yield break;
            }

            foreach (var node in nodes)
            {
                yield return node;

                foreach (var child in FlattenOutlineNodes(node.Children))
                {
                    yield return child;
                }
            }
        }

        private long ClampStartSortOrder(long startSortOrder, int windowChunkCount)
        {
            if (TotalChunkCount <= 0)
            {
                return 0;
            }

            var maxStart = Math.Max(TotalChunkCount - Math.Max(windowChunkCount, 1), 0);
            return Math.Clamp(startSortOrder, 0, maxStart);
        }

        private bool IsChunkLoaded(long sortOrder)
        {
            return LoadedChunks.Any(chunk => chunk.SortOrder == sortOrder);
        }

        private bool IsChunkDirty(long sortOrder)
        {
            return _dirtyDrafts.ContainsKey(sortOrder) || _dirtySortOrders.Contains(sortOrder);
        }

        private string GetChunkCssClass(long sortOrder)
        {
            return IsChunkDirty(sortOrder)
                ? "rich-text-document-editor__chunk rich-text-document-editor__chunk--dirty"
                : "rich-text-document-editor__chunk";
        }

        private string FormatLoadedWindow()
        {
            if (LoadedChunks.Count == 0)
            {
                return "empty";
            }

            return $"{LoadedChunks.Min(chunk => chunk.SortOrder)}..{LoadedChunks.Max(chunk => chunk.SortOrder)}";
        }

        private string FormatDirtySortOrders()
        {
            return _dirtyDrafts.Count == 0 && _dirtySortOrders.Count == 0
                ? "none"
                : string.Join(",", _dirtyDrafts.Keys.Concat(_dirtySortOrders).Distinct().OrderBy(x => x));
        }

        private Task LogEditorViewportAsync(string message)
        {
            return WebLogger == null
                ? Task.CompletedTask
                : WebLogger.Information(message);
        }

        private void RecalculateEstimatedChunkHeight()
        {
            if (_chunkHeights.Count == 0)
            {
                EstimatedChunkHeight = DefaultEstimatedChunkHeight;
                return;
            }

            var average = _chunkHeights.Values.Average();
            EstimatedChunkHeight = Math.Clamp(average, MinEstimatedChunkHeight, MaxEstimatedChunkHeight);
        }

        public async ValueTask DisposeAsync()
        {
            if (_viewportRegistered && !string.IsNullOrWhiteSpace(ViewportElementId))
            {
                try
                {
                    await CaptureCurrentEditorsAsync();
                    await JS.InvokeVoidAsync("richTextEditor.destroyEditors", ViewportElementId);
                    await JS.InvokeVoidAsync("richTextEditViewport.unregisterViewport", ViewportElementId);
                }
                catch (JSDisconnectedException)
                {
                    // Blazor Server can disconnect JS runtime during teardown.
                }
                catch (OperationCanceledException)
                {
                    // Blazor Server can cancel JS interop during teardown.
                }
            }

            _dotNetReference?.Dispose();
            _mediaVideoUploadedSubscription?.Dispose();
            _mediaVideoUploadFailedSubscription?.Dispose();
        }

        public sealed class EditorChunkSnapshot
        {
            public Guid ChunkId { get; set; }
            public long SortOrder { get; set; }
            public string? OriginalHtml { get; set; }
            public string? Html { get; set; }
            public bool IsDirty { get; set; }
        }
    }
}

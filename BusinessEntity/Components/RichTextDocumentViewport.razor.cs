using BusinessEntity.Core.RichText;
using BusinessEntity.Services;
using BusinessEntity.Settings;
using BusinessEntity.WebLogger.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BusinessEntity.Components
{
    public partial class RichTextDocumentViewport : ComponentBase, IAsyncDisposable
    {
        private const double DefaultEstimatedChunkHeight = 1400;
        private const double MinEstimatedChunkHeight = 360;
        private const double MaxEstimatedChunkHeight = 8000;
        private const int ProgrammaticScrollSuppressionMs = 350;
        private const string ProgrammaticScrollBehavior = "auto";

        private readonly Dictionary<long, double> _chunkHeights = new();
        private DotNetObjectReference<RichTextDocumentViewport>? _dotNetReference;
        private RichTextDocumentChunkWindow? _appliedInitialWindow;
        private RichTextDocumentSettings _settings = new();
        private Guid _appliedBusinessEntityId;
        private int? _appliedDocumentVersion;
        private long? _appliedInitialTargetChunkSortOrder;
        private int? _appliedInitialTargetBlockIndex;
        private bool _viewportRegistered;
        private bool _isLoadingWindow;
        private bool _pendingLoadedChunksLog;
        private bool _initialTargetPositionChecked;
        private long _loadVersion;
        private double? _lastScrollTop;
        private string? _pendingAnchor;
        private long? _pendingVisibleChunkSortOrder;
        private RichTextDocumentViewportPosition? _pendingViewportPosition;

        [Parameter] public string ViewportElementId { get; set; } = string.Empty;
        [Parameter] public Guid BusinessEntityId { get; set; }
        [Parameter] public int? DocumentVersion { get; set; }
        [Parameter] public RichTextDocumentChunkWindow? InitialWindow { get; set; }
        [Parameter] public RichTextDocumentViewportPosition? InitialTargetPosition { get; set; }
        [Parameter] public IReadOnlyList<RichTextDocumentOutlineNode> OutlineNodes { get; set; } = Array.Empty<RichTextDocumentOutlineNode>();
        [Parameter] public bool IsInitialContentLoading { get; set; }
        [Parameter] public int BottomReservePx { get; set; }

        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public RichTextDocumentHelper RichTextDocumentHelper { get; set; } = default!;
        [Inject] public RichTextDocumentSettingsService RichTextDocumentSettingsService { get; set; } = default!;
        [Inject] public IWebLoggerService? WebLogger { get; set; }

        private IReadOnlyList<RichTextDocumentChunk> LoadedChunks { get; set; } = Array.Empty<RichTextDocumentChunk>();
        private int TotalChunkCount { get; set; }
        private double EstimatedChunkHeight { get; set; } = DefaultEstimatedChunkHeight;
        private double TopSpacerPx { get; set; }
        private double BottomSpacerPx { get; set; }
        private string TopSpacerStyle => $"height: {Math.Max(TopSpacerPx, 0):0.##}px;";
        private string BottomSpacerStyle => $"height: {Math.Max(BottomSpacerPx, 0):0.##}px;";
        private string BottomReserveAttributeValue => Math.Max(BottomReservePx, 0).ToString();

        protected override async Task OnInitializedAsync()
        {
            _settings = await RichTextDocumentSettingsService.GetSettingsAsync();
        }

        protected override void OnParametersSet()
        {
            var contextChanged = BusinessEntityId != _appliedBusinessEntityId ||
                                 DocumentVersion != _appliedDocumentVersion;
            if (contextChanged)
            {
                ResetViewportStateForContext();
            }

            if (contextChanged || !IsSameInitialTargetPosition(InitialTargetPosition))
            {
                StoreAppliedInitialTargetPosition(InitialTargetPosition);
                _initialTargetPositionChecked = false;
            }

            if (!contextChanged && ReferenceEquals(InitialWindow, _appliedInitialWindow))
            {
                return;
            }

            _appliedInitialWindow = InitialWindow;
            ApplyWindow(InitialWindow);
        }

        // Сбрасывает локальное окно и фоновые операции при смене документа или версии.
        private void ResetViewportStateForContext()
        {
            _appliedBusinessEntityId = BusinessEntityId;
            _appliedDocumentVersion = DocumentVersion;
            _appliedInitialWindow = null;
            _chunkHeights.Clear();
            EstimatedChunkHeight = DefaultEstimatedChunkHeight;
            _isLoadingWindow = false;
            _pendingLoadedChunksLog = false;
            _pendingAnchor = null;
            _pendingVisibleChunkSortOrder = null;
            _pendingViewportPosition = null;
            _lastScrollTop = null;
            _loadVersion++;
        }

        // Проверяет, совпадает ли новая целевая позиция с уже примененной.
        private bool IsSameInitialTargetPosition(RichTextDocumentViewportPosition? position)
        {
            if (position == null)
            {
                return !_appliedInitialTargetChunkSortOrder.HasValue &&
                       !_appliedInitialTargetBlockIndex.HasValue;
            }

            return _appliedInitialTargetChunkSortOrder == position.ChunkSortOrder &&
                   _appliedInitialTargetBlockIndex == position.BlockIndex;
        }

        // Запоминает позицию, чтобы не выполнять программную прокрутку на каждом render.
        private void StoreAppliedInitialTargetPosition(RichTextDocumentViewportPosition? position)
        {
            _appliedInitialTargetChunkSortOrder = position?.ChunkSortOrder;
            _appliedInitialTargetBlockIndex = position?.BlockIndex;
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
                await JS.InvokeVoidAsync("richTextReadViewport.registerVirtualViewport", ViewportElementId, _dotNetReference);
                _viewportRegistered = true;
            }
            else
            {
                await JS.InvokeVoidAsync("richTextReadViewport.syncViewportSize", ViewportElementId);
            }

            await JS.InvokeVoidAsync("richTextReadViewport.measureChunks", ViewportElementId);

            if (!_initialTargetPositionChecked)
            {
                _initialTargetPositionChecked = true;
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
            }

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

            if (_pendingLoadedChunksLog)
            {
                _pendingLoadedChunksLog = false;
                await LogLoadedChunksStateAsync();
            }
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

        public async Task<long?> GetCurrentVisibleChunkSortOrderAsync()
        {
            if (!_viewportRegistered || string.IsNullOrWhiteSpace(ViewportElementId))
            {
                return LoadedChunks.Count == 0 ? null : LoadedChunks[0].SortOrder;
            }

            try
            {
                return await JS.InvokeAsync<long?>("richTextReadViewport.getCurrentChunkSortOrder", ViewportElementId);
            }
            catch (JSException)
            {
                return LoadedChunks.Count == 0 ? null : LoadedChunks[0].SortOrder;
            }
            catch (InvalidOperationException)
            {
                return LoadedChunks.Count == 0 ? null : LoadedChunks[0].SortOrder;
            }
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
                    "richTextReadViewport.getCurrentViewportPosition",
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
                    "richTextReadViewport.getCurrentTextSelection",
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
                targetSortOrder - GetScrollPreviousChunkCount(),
                GetScrollWindowChunkCount());
            await LoadWindowAsync(
                desiredStart,
                GetScrollWindowChunkCount(),
                pendingAnchor: null,
                pendingVisibleChunkSortOrder: targetSortOrder,
                pendingViewportPosition: null,
                mergeAdjacentWindow: ShouldMergeAdjacentWindow(desiredStart, GetScrollWindowChunkCount()));
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
                targetSortOrder - GetScrollPreviousChunkCount(),
                GetScrollWindowChunkCount());
            await LoadWindowAsync(
                desiredStart,
                GetScrollWindowChunkCount(),
                pendingAnchor: null,
                pendingVisibleChunkSortOrder: targetSortOrder,
                pendingViewportPosition: null,
                mergeAdjacentWindow: ShouldMergeAdjacentWindow(desiredStart, GetScrollWindowChunkCount()));
            return true;
        }

        [JSInvokable]
        public Task OnChunkHeightsMeasured(ChunkHeightMeasurement[] measurements)
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
            return InvokeAsync(StateHasChanged);
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
                targetSortOrder - GetTableOfContentsBeforeBuffer(),
                GetTableOfContentsWindowChunkCount());
            await LoadWindowAsync(
                start,
                GetTableOfContentsWindowChunkCount(),
                pendingAnchor,
                pendingVisibleChunkSortOrder: pendingViewportPosition == null ? targetSortOrder : null,
                pendingViewportPosition: pendingViewportPosition,
                mergeAdjacentWindow: false);
        }

        private async Task LoadWindowAsync(
            long startSortOrder,
            int take,
            string? pendingAnchor,
            long? pendingVisibleChunkSortOrder,
            RichTextDocumentViewportPosition? pendingViewportPosition,
            bool mergeAdjacentWindow)
        {
            if (_isLoadingWindow)
            {
                return;
            }

            var version = ++_loadVersion;
            _isLoadingWindow = true;

            try
            {
                var window = await GetChunkWindowWithCacheAsync(startSortOrder, take);

                if (version != _loadVersion)
                {
                    return;
                }

                ApplyWindow(window, mergeAdjacentWindow);
                _pendingAnchor = pendingAnchor;
                _pendingVisibleChunkSortOrder = pendingVisibleChunkSortOrder;
                _pendingViewportPosition = pendingViewportPosition;
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
                return await RichTextDocumentHelper.GetChunkWindowAsync(
                    BusinessEntityId,
                    startSortOrder,
                    take,
                    DocumentVersion);
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
                    Chunks = Array.Empty<RichTextDocumentChunk>()
                };
            }

            var cachedChunks = LoadedChunks
                .Where(chunk => chunk.SortOrder >= startSortOrder && chunk.SortOrder < endExclusive)
                .GroupBy(chunk => chunk.SortOrder)
                .ToDictionary(group => group.Key, group => group.Last());
            var fetchedChunks = new Dictionary<long, RichTextDocumentChunk>();
            var totalChunkCount = TotalChunkCount;

            long? missingStart = null;
            for (var sortOrder = startSortOrder; sortOrder < endExclusive; sortOrder++)
            {
                if (!cachedChunks.ContainsKey(sortOrder))
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

            var chunks = new List<RichTextDocumentChunk>();
            for (var sortOrder = startSortOrder; sortOrder < endExclusive; sortOrder++)
            {
                if (cachedChunks.TryGetValue(sortOrder, out var cachedChunk))
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
            Dictionary<long, RichTextDocumentChunk> fetchedChunks,
            int fallbackTotalChunkCount)
        {
            var take = (int)Math.Max(endExclusive - startSortOrder, 0);
            if (take <= 0)
            {
                return fallbackTotalChunkCount;
            }

            var window = await RichTextDocumentHelper.GetChunkWindowAsync(
                BusinessEntityId,
                startSortOrder,
                take,
                DocumentVersion);

            foreach (var chunk in window.Chunks)
            {
                fetchedChunks[chunk.SortOrder] = chunk;
            }

            return window.TotalChunkCount > 0 ? window.TotalChunkCount : fallbackTotalChunkCount;
        }

        private void ApplyWindow(RichTextDocumentChunkWindow? window, bool mergeAdjacentWindow = false)
        {
            if (window == null)
            {
                LoadedChunks = Array.Empty<RichTextDocumentChunk>();
                TotalChunkCount = 0;
                TopSpacerPx = 0;
                BottomSpacerPx = 0;
                _lastScrollTop = null;
                return;
            }

            LoadedChunks = mergeAdjacentWindow
                ? MergeLoadedChunks(window.Chunks ?? Array.Empty<RichTextDocumentChunk>())
                : window.Chunks ?? Array.Empty<RichTextDocumentChunk>();
            TotalChunkCount = window.TotalChunkCount;
            RecalculateSpacers();
            if (!mergeAdjacentWindow)
            {
                _lastScrollTop = null;
            }

            _pendingLoadedChunksLog = true;
        }

        private async Task<bool> ScrollToHeadingInViewportAsync(string headingId)
        {
            _lastScrollTop = null;
            return await JS.InvokeAsync<bool>(
                "richTextReadViewport.scrollToHeading",
                ViewportElementId,
                headingId,
                ProgrammaticScrollBehavior,
                ProgrammaticScrollSuppressionMs);
        }

        private async Task<bool> ScrollToChunkInViewportAsync(long sortOrder)
        {
            _lastScrollTop = null;
            return await JS.InvokeAsync<bool>(
                "richTextReadViewport.scrollToChunk",
                ViewportElementId,
                sortOrder,
                ProgrammaticScrollBehavior,
                ProgrammaticScrollSuppressionMs);
        }

        private async Task<bool> EnsureChunkVisibleInViewportAsync(long sortOrder)
        {
            _lastScrollTop = null;
            return await JS.InvokeAsync<bool>(
                "richTextReadViewport.ensureChunkVisible",
                ViewportElementId,
                sortOrder,
                ProgrammaticScrollBehavior,
                ProgrammaticScrollSuppressionMs);
        }

        private async Task<bool> ScrollToBlockInViewportAsync(RichTextDocumentViewportPosition position)
        {
            _lastScrollTop = null;
            return await JS.InvokeAsync<bool>(
                "richTextReadViewport.scrollToBlock",
                ViewportElementId,
                position.ChunkSortOrder,
                position.BlockIndex,
                ProgrammaticScrollBehavior,
                ProgrammaticScrollSuppressionMs);
        }

        private IReadOnlyList<RichTextDocumentChunk> MergeLoadedChunks(IReadOnlyList<RichTextDocumentChunk> incomingChunks)
        {
            if (LoadedChunks.Count == 0)
            {
                return incomingChunks;
            }

            if (incomingChunks.Count == 0)
            {
                return LoadedChunks;
            }

            return LoadedChunks
                .Concat(incomingChunks)
                .GroupBy(chunk => chunk.SortOrder)
                .Select(group => group.Last())
                .OrderBy(chunk => chunk.SortOrder)
                .ToArray();
        }

        private bool ShouldMergeAdjacentWindow(long startSortOrder, int take)
        {
            if (LoadedChunks.Count == 0 || take <= 0)
            {
                return false;
            }

            var firstLoaded = LoadedChunks[0].SortOrder;
            var lastLoaded = LoadedChunks[^1].SortOrder;
            var endSortOrder = startSortOrder + take - 1;

            return endSortOrder >= firstLoaded - 1 && startSortOrder <= lastLoaded + 1;
        }

        private async Task LogLoadedChunksStateAsync()
        {
            if (WebLogger == null || BusinessEntityId == Guid.Empty)
            {
                return;
            }

            var loadedBySortOrder = LoadedChunks
                .GroupBy(chunk => chunk.SortOrder)
                .ToDictionary(group => group.Key, group => group.Last());
            var loadedStart = LoadedChunks.Count == 0 ? -1 : LoadedChunks.Min(chunk => chunk.SortOrder);
            var loadedEnd = LoadedChunks.Count == 0 ? -1 : LoadedChunks.Max(chunk => chunk.SortOrder);

            await WebLogger.Information(
                "[rich-doc-loaded-chunks] " +
                $"totalChunks={TotalChunkCount} " +
                $"loadedWindow={loadedStart}..{loadedEnd} " +
                $"chunks={BuildLoadedChunksMap(loadedBySortOrder)}");
        }

        private string BuildLoadedChunksMap(IReadOnlyDictionary<long, RichTextDocumentChunk> loadedBySortOrder)
        {
            if (TotalChunkCount <= 0)
            {
                return "none";
            }

            if (TotalChunkCount <= 200)
            {
                var values = new List<string>(TotalChunkCount);
                for (var sortOrder = 0L; sortOrder < TotalChunkCount; sortOrder++)
                {
                    values.Add(loadedBySortOrder.TryGetValue(sortOrder, out var chunk)
                        ? chunk.CharCount.ToString()
                        : "0");
                }

                return string.Join(",", values);
            }

            return BuildCompactLoadedChunksMap(loadedBySortOrder);
        }

        private string BuildCompactLoadedChunksMap(IReadOnlyDictionary<long, RichTextDocumentChunk> loadedBySortOrder)
        {
            var parts = new List<string>();
            var missingCount = 0;

            for (var sortOrder = 0L; sortOrder < TotalChunkCount; sortOrder++)
            {
                if (loadedBySortOrder.TryGetValue(sortOrder, out var chunk))
                {
                    if (missingCount > 0)
                    {
                        parts.Add($"0x{missingCount}");
                        missingCount = 0;
                    }

                    parts.Add(chunk.CharCount.ToString());
                    continue;
                }

                missingCount++;
            }

            if (missingCount > 0)
            {
                parts.Add($"0x{missingCount}");
            }

            return string.Join(",", parts);
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
            if (nearestPrevious != null)
            {
                return nearestPrevious;
            }

            return flatNodes[0];
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

        private int GetTableOfContentsBeforeBuffer()
        {
            return _settings.GetTableOfContentsBeforeBuffer();
        }

        private int GetTableOfContentsWindowChunkCount()
        {
            return _settings.GetTableOfContentsWindowChunkCount();
        }

        private int GetScrollPreviousChunkCount()
        {
            return _settings.GetScrollPreviousChunkCount();
        }

        private int GetScrollWindowChunkCount()
        {
            return _settings.GetScrollWindowChunkCount();
        }

        private bool IsChunkLoaded(long sortOrder)
        {
            return LoadedChunks.Any(chunk => chunk.SortOrder == sortOrder);
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
                await BusinessEntity.Infrastructure.UI.BrowserLifecycle.UnregisterAsync(JS, "richTextReadViewport.unregisterViewport", ViewportElementId);
            }

            _dotNetReference?.Dispose();
        }

        public sealed class ChunkHeightMeasurement
        {
            public long SortOrder { get; set; }
            public double Height { get; set; }
        }
    }
}

using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.Components;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.TreeMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using ReactiveUI;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Components
{
    // Предметная страница статьи: чтение, история, публикация и импорт.
    public partial class RichDocumentContent : IDisposable
    {
        [Parameter] public Guid Id { get; set; }
        [Parameter]
        [SupplyParameterFromQuery(Name = "edit")]
        public string? EditQuery { get; set; }

        [Inject] public RichTextDocumentHelper RichTextDocumentHelper { get; set; } = default!;
        [Inject] public RichTextDocumentImportService ImportService { get; set; } = default!;
        [Inject] public RichTextDocumentSettingsService RichTextDocumentSettingsService { get; set; } = default!;
        [Inject] public BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextUserMessages MessagePanel { get; set; } = default!;
        [Inject] public IDataProviderConnector DataProviderConnector { get; set; } = default!;
        [Inject] public IUserConnector UserConnector { get; set; } = default!;
        [Inject] public IMessageBus MessageBus { get; set; } = default!;
        [Inject] public ITreeSelectionService TreeSelectionService { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private TreeMiniAppService TreeMiniAppService { get; set; } = default!;

        private BusinessEntity.Core.Classes.BusinessEntity? Entity;
        private RichTextDocument? Manifest;
        private RichTextDocumentChunkWindow? InitialChunkWindow { get; set; }
        private IReadOnlyList<RichTextDocumentOutlineNode> OutlineNodes { get; set; } = Array.Empty<RichTextDocumentOutlineNode>();
        private bool IsLoading { get; set; } = true;
        private bool IsInitialContentLoading { get; set; }
        private bool IsOutlineLoading { get; set; }
        private bool IsImporting { get; set; }
        private bool IsRebuildingTableOfContents { get; set; }
        private string? Error;
        private string? StatusMessage;
        private CancellationTokenSource? LoadCancellationTokenSource { get; set; }
        private long LoadVersion { get; set; }
        private int VersionsRefreshToken { get; set; }
        private string ViewedTitle { get; set; } = string.Empty;
        private int ViewedVersion { get; set; } = 1;
        private int LatestVersion { get; set; } = 1;
        private bool IsDocumentOwner { get; set; }
        private bool IsCurrentUserAdmin { get; set; }
        private bool HasFullDocumentAccess { get; set; }
        private bool CanViewPublishedDocument { get; set; }
        private bool CanEditDocument { get; set; }
        private bool CanDeleteDocument { get; set; }
        private bool CanPublishDocument { get; set; }
        private bool CanChangePublicFlag { get; set; }
        private bool RequestedEditMode => string.Equals(EditQuery, "1", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(EditQuery, "true", StringComparison.OrdinalIgnoreCase);
        private bool StartInEditMode => RequestedEditMode && CanEditViewedVersion;
        private bool CanEditViewedVersion => CanEditDocument && ViewedVersion >= LatestVersion;
        private Guid _messagesForDocumentId;
        private Guid _loadedDocumentId;
        private const int OutlineChunkBatchSize = 5;

        private RichTextDocumentView? DocumentView;
        private IDisposable? _dataSubscription;
        private bool _disposed;
        protected override void OnInitialized()
        {
            _dataSubscription = MessageBus.Listen<EntityBatchCommitted>().Subscribe(message =>
            {
                if (message.ChangedEntities.Contains(Id) || message.DeletedEntities.Contains(Id))
                    _ = InvokeAsync(async () =>
                    {
                        if (_disposed || IsLoading || IsImporting || IsRebuildingTableOfContents || DocumentView?.IsEditingNow == true) return;
                        if (ViewedVersion == LatestVersion) ViewedVersion = 0;
                        VersionsRefreshToken++;
                        await LoadAsync(); StateHasChanged();
                    });
            });
        }

        protected override async Task OnParametersSetAsync()
        {
            await LoadAsync();
        }

        // Loads only the document shell synchronously; heavy content parts are loaded in background tasks.
        private async Task LoadAsync()
        {
            LoadCancellationTokenSource?.Cancel();
            LoadCancellationTokenSource?.Dispose();
            LoadCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = LoadCancellationTokenSource.Token;
            var version = ++LoadVersion;

            if (_messagesForDocumentId != Id)
            {

                _messagesForDocumentId = Id;
            }

            if (_loadedDocumentId != Id)
            {
                ViewedVersion = 0;
                LatestVersion = 1;
                _loadedDocumentId = Id;
            }

            IsLoading = true;
            IsInitialContentLoading = false;
            IsOutlineLoading = false;
            Error = null;
            InitialChunkWindow = null;
            OutlineNodes = Array.Empty<RichTextDocumentOutlineNode>();

            try
            {
                var shell = await RichTextDocumentHelper.GetRichTextDocumentShellAsync(Id, cancellationToken);
                if (shell == null)
                {
                    Entity = null;
                    Manifest = null;
                    Error = "Rich-text документ не найден.";
                    return;
                }

                Entity = shell.Entity;
                Manifest = shell.Manifest;
                await ResolveAccessAsync(cancellationToken);
                if (!CanReadCurrentDocument())
                {
                    Entity = null;
                    Manifest = null;
                    Error = "Документ недоступен: опубликованная версия отсутствует.";
                    return;
                }

                TreeSelectionService.RequestEntitySelection(Entity.Id);

                await RefreshVersionsAsync(cancellationToken);
                if (!HasFullDocumentAccess)
                {
                    ViewedVersion = Math.Min(Manifest.PublishedVersion, LatestVersion);
                }

                await LoadViewedTitleAsync(cancellationToken);
                IsInitialContentLoading = true;
                IsOutlineLoading = true;
                _ = LoadInitialChunkWindowAsync(Id, version, ViewedVersion, cancellationToken);
                _ = LoadOutlineAsync(Id, version, ViewedVersion, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // A newer document navigation superseded this load.
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                if (version == LoadVersion)
                {
                    IsLoading = false;
                }
            }
        }

        private async Task LoadInitialChunkWindowAsync(
            Guid documentId,
            long loadVersion,
            int documentVersion,
            CancellationToken cancellationToken)
        {
            try
            {
                var richTextDocumentSettings = await RichTextDocumentSettingsService.GetSettingsAsync(cancellationToken);
                var chunkWindow = await RichTextDocumentHelper.GetChunkWindowAsync(
                    documentId,
                    0,
                    richTextDocumentSettings.GetInitialChunkCount(),
                    documentVersion,
                    cancellationToken);

                await InvokeAsync(() =>
                {
                    if (loadVersion != LoadVersion || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    InitialChunkWindow = chunkWindow;
                    IsInitialContentLoading = false;
                    StateHasChanged();
                });
            }
            catch (OperationCanceledException)
            {
                // A newer document navigation superseded this load.
            }
            catch (Exception ex)
            {
                await InvokeAsync(() =>
                {
                    if (loadVersion != LoadVersion || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    Error = ex.Message;
                    IsInitialContentLoading = false;
                    StateHasChanged();
                });
            }
        }

        private async Task LoadOutlineAsync(
            Guid documentId,
            long loadVersion,
            int documentVersion,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var tableOfContents in RichTextDocumentHelper.GetTableOfContentsBatchesAsync(
                    documentId,
                    OutlineChunkBatchSize,
                    documentVersion,
                    cancellationToken))
                {
                    var outlineNodes = tableOfContents.Select(MapTableOfContentsNode).ToList();

                    await InvokeAsync(() =>
                    {
                        if (loadVersion != LoadVersion || cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        OutlineNodes = outlineNodes;
                        StateHasChanged();
                    });
                }

                await InvokeAsync(() =>
                {
                    if (loadVersion != LoadVersion || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    IsOutlineLoading = false;
                    StateHasChanged();
                });
            }
            catch (OperationCanceledException)
            {
                // A newer document navigation superseded this load.
            }
            catch (Exception ex)
            {
                await InvokeAsync(() =>
                {
                    if (loadVersion != LoadVersion || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    Error = ex.Message;
                    IsOutlineLoading = false;
                    StateHasChanged();
                });
            }
        }

        // Импортирует выбранный пользователем файл в rich-text документ.
        private async Task OnImportSelectedAsync(InputFileChangeEventArgs args)
        {
            if (Entity == null || !CanEditViewedVersion)
            {
                return;
            }

            var file = args.File;
            if (file == null)
            {
                return;
            }

            IsImporting = true;
            SetStatusMessage($"Импорт файла '{file.Name}'...");
            Error = null;

            try
            {
                await using var stream = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024);
                var importResult = await ImportService.ImportAsync(file.Name, stream);

                var manifest = Manifest ?? new RichTextDocument();
                manifest.Tag = string.IsNullOrWhiteSpace(importResult.Manifest.Tag)
                    ? manifest.Tag
                    : importResult.Manifest.Tag;
                manifest.ContentStorage = importResult.Manifest.ContentStorage;
                manifest.EditorFormat = importResult.Manifest.EditorFormat;
                manifest.ChunkPolicy = importResult.Manifest.ChunkPolicy;
                manifest.EmbeddedFileStorage = importResult.Manifest.EmbeddedFileStorage;
                manifest.SupportsImages = importResult.Manifest.SupportsImages;

                // Импорт больше не заменяет документ целиком: новый контент добавляется в конец текущего содержимого.
                await RichTextDocumentHelper.AppendImportedContentAsync(
                    Entity,
                    manifest,
                    importResult.Chunks,
                    importResult.Files);

                SetStatusMessage($"Импорт файла '{file.Name}' добавлен в конец документа.");
                VersionsRefreshToken++;
                ViewedVersion = 0;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                SetStatusMessage(null);
            }
            finally
            {
                IsImporting = false;
            }
        }

        // Пересоздаёт сохранённые chunk-properties содержания и обновляет HTML-cache на странице.
        private async Task OnRebuildTableOfContentsAsync()
        {
            if (Entity == null || !CanEditViewedVersion)
            {
                return;
            }

            IsRebuildingTableOfContents = true;
            SetStatusMessage("Пересоздание содержания...");
            Error = null;

            try
            {
                var richTextDocumentSettings = await RichTextDocumentSettingsService.GetSettingsAsync();
                InitialChunkWindow = await RichTextDocumentHelper.GetChunkWindowAsync(
                    Id,
                    0,
                    richTextDocumentSettings.GetInitialChunkCount(),
                    ViewedVersion);

                await foreach (var tableOfContents in RichTextDocumentHelper.GetTableOfContentsBatchesAsync(
                    Id,
                    OutlineChunkBatchSize,
                    ViewedVersion))
                {
                    OutlineNodes = tableOfContents.Select(MapTableOfContentsNode).ToList();
                    await InvokeAsync(StateHasChanged);
                }

                SetStatusMessage("Содержание обновлено.");
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                SetStatusMessage(null);
            }
            finally
            {
                IsRebuildingTableOfContents = false;
            }
        }

        private async Task OnEditorSavedAsync(RichTextDocumentEditorSaveRequest request)
        {
            if (!CanEditViewedVersion)
            {
                return;
            }

            Error = null;

            try
            {
                var shell = await RichTextDocumentHelper.GetRichTextDocumentShellAsync(Id)
                    ?? throw new KeyNotFoundException("Статья после сохранения не найдена.");
                Entity = shell.Entity;
                Manifest = shell.Manifest;

                var statusParts = new List<string>();
                if (request.SavedChunkCount > 0)
                {
                    statusParts.Add($"Сохранено чанков: {request.SavedChunkCount}");
                }

                if (request.TitleChanged)
                {
                    statusParts.Add("название сохранено");
                }

                var versionDescription = (request.VersionDescription ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(versionDescription))
                {
                    statusParts.Add("описание версии сохранено");
                }

                SetStatusMessage(statusParts.Count == 0
                    ? "Нет изменений для сохранения."
                    : string.Join("; ", statusParts) + ".");
                VersionsRefreshToken++;
                await RefreshVersionsAsync();

                ViewedVersion = request.SavedVersion;
                ViewedTitle = request.Title;
                if (request.RefreshReadWindow)
                {
                    await RefreshInitialChunkWindowForReadModeAsync(request.ViewportPosition);
                    await InvokeAsync(StateHasChanged);
                }

                IsOutlineLoading = true;
                await foreach (var tableOfContents in RichTextDocumentHelper.GetTableOfContentsBatchesAsync(
                    Id,
                    OutlineChunkBatchSize,
                    ViewedVersion))
                {
                    OutlineNodes = tableOfContents.Select(MapTableOfContentsNode).ToList();
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                SetStatusMessage(null);
            }
            finally
            {
                IsOutlineLoading = false;
            }
        }

        private async Task RefreshInitialChunkWindowForReadModeAsync(RichTextDocumentViewportPosition? targetPosition)
        {
            IsInitialContentLoading = true;

            try
            {
                var settings = await RichTextDocumentSettingsService.GetSettingsAsync();
                var take = Math.Max(settings.GetInitialChunkCount(), 1);
                InitialChunkWindow = targetPosition == null
                    ? await RichTextDocumentHelper.GetChunkWindowAsync(Id, 0, take, ViewedVersion)
                    : await RichTextDocumentHelper.GetChunkWindowAroundAsync(
                        Id,
                        Math.Max(targetPosition.ChunkSortOrder, 0),
                        take / 2,
                        Math.Max(take - (take / 2) - 1, 0),
                        ViewedVersion);
            }
            finally
            {
                IsInitialContentLoading = false;
            }
        }

        private async Task OnVersionSelectedAsync(int version)
        {
            if (Entity == null || version <= 0 || version == ViewedVersion)
            {
                return;
            }

            if (!HasFullDocumentAccess && version != Manifest?.PublishedVersion)
            {
                return;
            }

            // Общий loader отменяет прежнюю загрузку и завершает IsLoading для последнего выбора.
            ViewedVersion = version;
            await LoadAsync();
        }

        private async Task LoadViewedTitleAsync(CancellationToken ct)
        {
            var selected = await DataProviderConnector.GetDataVersionAsync<RichTextDocument>(Id, ViewedVersion, ct)
                ?? throw new InvalidDataException("Отсутствует выбранная версия статьи.");
            ViewedTitle = selected.Name;
        }

        private async Task RefreshVersionsAsync(CancellationToken cancellationToken = default)
        {
            var versions = await DataProviderConnector.GetDataVersionsAsync(Id, cancellationToken);
            if (versions.Count == 0 || versions.Any(x => x.Version < 1)) throw new InvalidDataException("История статьи отсутствует или повреждена.");
            LatestVersion = versions.Max(x => x.Version);

            if (ViewedVersion <= 0 || ViewedVersion > LatestVersion)
            {
                ViewedVersion = LatestVersion;
            }
        }

        private async Task OnPublishRequestedAsync()
        {
            if (Entity == null || Manifest == null || !CanPublishDocument)
            {
                return;
            }

            Error = null;

            try
            {
                if (ViewedVersion < 1) throw new InvalidOperationException("Не выбрана версия для публикации.");
                await DataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
                    { Publications = [new(Id, ViewedVersion)] });
                Manifest.PublishedVersion = ViewedVersion;
                Entity.PublishedVersion = ViewedVersion;
                SetStatusMessage($"Опубликована версия {Manifest.PublishedVersion}.");
                VersionsRefreshToken++;
                await RefreshVersionsAsync();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                SetStatusMessage(null);
            }
        }

        private async Task OnDeleteRequestedAsync()
        {
            if (Entity == null || !CanDeleteDocument)
            {
                return;
            }

            var entityId = Entity.Id;
            var entityName = Entity.Name;
            var confirmed = await JSRuntime.InvokeAsync<bool>("confirm", $"Удалить rich-text документ \"{entityName}\"?");
            if (!confirmed)
            {
                return;
            }

            Error = null;

            try
            {
                LoadCancellationTokenSource?.Cancel();
                await TreeMiniAppService.DeleteEntitiesAsync(new[] { entityId });

                TreeSelectionService.RequestEntitySelection(null);
                NavigationManager.NavigateTo("/", replace: true);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                SetStatusMessage(null);
            }
        }

        private async Task OnPublicChangedAsync(bool value)
        {
            if (Entity == null || !CanChangePublicFlag)
            {
                return;
            }

            Error = null;

            try
            {
                Entity.IsPublic = value;
                Entity.LastModifiedDate = DateTime.UtcNow;
                await DataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null) { Entities = [Entity] });

                SetStatusMessage(value ? "Документ сделан общим." : "Документ больше не общий.");
            }
            catch (Exception ex)
            {
                Entity.IsPublic = !value;
                Error = ex.Message;
                SetStatusMessage(null);
            }
        }

        private async Task ResolveAccessAsync(CancellationToken cancellationToken)
        {
            IsDocumentOwner = false;
            IsCurrentUserAdmin = false;
            HasFullDocumentAccess = false;
            CanViewPublishedDocument = false;
            CanEditDocument = false;
            CanDeleteDocument = false;
            CanPublishDocument = false;
            CanChangePublicFlag = false;

            if (Entity == null || Manifest == null)
            {
                return;
            }

            var access = await UserConnector.GetCurrentUserContentAccessForEntityAsync(
                new UserContentAccessRequest
                {
                    EntityId = Entity.Id,
                    EntityType = Entity.EntityType,
                    IsCommon = Entity.IsPublic,
                    CreatedByUserId = Entity.CreatedByUserId,
                    PublishedVersion = Manifest.PublishedVersion
                },
                cancellationToken);
            IsDocumentOwner = access.IsOwner;
            IsCurrentUserAdmin = access.IsAccessAdmin;
            HasFullDocumentAccess = access.CanViewDraft;
            CanViewPublishedDocument = access.CanViewPublished;
            CanEditDocument = access.CanEditDraft;
            CanPublishDocument = access.CanPublishDraft;
            CanChangePublicFlag = access.CanChangeCommonFlag;

            var permissions = await UserConnector.GetCurrentUserPermissionsForEntityAsync(Entity.Id, cancellationToken);
            CanDeleteDocument = access.IsAccessAdmin ||
                                permissions.CanAdminItems ||
                                permissions.CanAdminSpace ||
                                permissions.CanGlobalAdmin;
        }

        private bool CanReadCurrentDocument()
        {
            if (Entity == null || Manifest == null)
            {
                return false;
            }

            return HasFullDocumentAccess || (CanViewPublishedDocument && Manifest.PublishedVersion > 0);
        }

        // Converts storage-backed rich-text table-of-contents entries into UI outline nodes.
        private static RichTextDocumentOutlineNode MapTableOfContentsNode(RichTextDocumentTableOfContentsEntry entry)
        {
            return new RichTextDocumentOutlineNode
            {
                HeadingId = entry.Anchor,
                ChunkSortOrder = entry.ChunkSortOrder,
                Title = entry.Title,
                Level = entry.Level,
                IsExpanded = true,
                Children = entry.Children.Select(MapTableOfContentsNode).ToList()
            };
        }

        private void SetStatusMessage(string? message)
        {
            StatusMessage = message;
            MessagePanel.Add(Id, message);
        }

        public void Dispose()
        {
            _disposed = true; _dataSubscription?.Dispose();
            LoadCancellationTokenSource?.Cancel();
            LoadCancellationTokenSource?.Dispose();
        }
    }
}

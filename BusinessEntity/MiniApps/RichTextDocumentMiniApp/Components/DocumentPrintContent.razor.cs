using BusinessEntity.Services;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Components;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Components
{
    // Отдельная страница печати для обычных и rich-text документов.
    // Подготавливает отдельную печатную страницу доступного контента.
    public partial class DocumentPrintContent : IDisposable
    {
        private const int OutlineChunkBatchSize = 5;

        private readonly string ViewportElementId = $"rich-text-print-viewport-{Guid.NewGuid():N}";
        private CancellationTokenSource? LoadCancellationTokenSource { get; set; }
        private long LoadVersion { get; set; }

        [Inject] public RichTextDocumentReader RichTextDocumentReader { get; set; } = default!;
        [Inject] public RichTextOutlineService RichTextOutlineService { get; set; } = default!;
        [Parameter] public Guid Id { get; set; }

        [Inject] public BusinessEntityHelper Helper { get; set; } = default!;
        [Inject] public RichTextDocumentSettingsService RichTextDocumentSettingsService { get; set; } = default!;
        [Inject] public IDataProviderConnector DataProviderConnector { get; set; } = default!;
        [Inject] public IUserConnector UserConnector { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;

        private BusinessEntity.Core.Classes.BusinessEntity? Entity { get; set; }
        private RichTextDocument? Manifest { get; set; }
        private RichTextDocumentChunkWindow? InitialChunkWindow { get; set; }
        private IReadOnlyList<RichTextDocumentOutlineNode> OutlineNodes { get; set; } = Array.Empty<RichTextDocumentOutlineNode>();
        private string? DocumentText { get; set; }
        private string? Error { get; set; }
        private bool IsLoading { get; set; } = true;
        private bool IsInitialContentLoading { get; set; }
        private int ViewedVersion { get; set; } = 1;
        private int LatestVersion { get; set; } = 1;
        private bool HasFullDocumentAccess { get; set; }
        private bool CanViewPublishedDocument { get; set; }
        private bool IsPrintCommandRunning { get; set; }
        private DocPrintSettings PrintSettings { get; set; } = new();
        private DocPrintSettingsPresetCollection PrintPresetCollection { get; set; } = new();
        private string PrintPresetName { get; set; } = string.Empty;
        private string SelectedPrintPresetName { get; set; } = string.Empty;
        private IReadOnlyList<DocPrintSettingsPreset> PrintPresets => PrintPresetCollection.Presets;
        private string PageTitleText => Entity == null ? "Печать документа" : $"Печать: {Entity.Name}";
        private bool IsRichTextDocument => Entity?.EntityType == global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id;
        private bool IsPrintCommandDisabled => IsLoading || IsPrintCommandRunning || Entity == null || !string.IsNullOrEmpty(Error);
        private bool IsPrintSettingsDisabled => IsLoading || IsPrintCommandRunning;
        private bool HasNamedPrintPreset => PrintPresets.Any(x =>
            string.Equals(x.Name, NormalizePrintPresetNameInput(PrintPresetName), StringComparison.OrdinalIgnoreCase));
        private bool IsSaveSettingsDisabled => IsLoading || IsPrintCommandRunning || string.IsNullOrWhiteSpace(PrintPresetName);
        private bool IsDeleteSettingsDisabled => IsLoading || IsPrintCommandRunning || !HasNamedPrintPreset;
        private string PrintScaleMultiplier => ((decimal)PrintSettings.FontScalePercent / 100m).ToString("0.##", CultureInfo.InvariantCulture);
        private string PrintSurfaceStyle =>
            $"--doc-print-font-scale: {PrintScaleMultiplier}; " +
            $"--doc-print-margin-top: {PrintSettings.MarginTopMm}mm; " +
            $"--doc-print-margin-bottom: {PrintSettings.MarginBottomMm}mm; " +
            $"--doc-print-margin-right: {PrintSettings.MarginRightMm}mm; " +
            $"--doc-print-margin-left: {PrintSettings.MarginLeftMm}mm;";
        private string PrintPageStyleMarkup =>
            $"<style>@page {{ margin: {PrintSettings.MarginTopMm}mm {PrintSettings.MarginRightMm}mm {PrintSettings.MarginBottomMm}mm {PrintSettings.MarginLeftMm}mm; }}</style>";

        // Загружает печатное представление при смене id документа.
        protected override async Task OnParametersSetAsync()
        {
            await LoadAsync();
        }

        // Загружает shell документа и стартовое окно содержимого для печатной страницы.
        private async Task LoadAsync()
        {
            LoadCancellationTokenSource?.Cancel();
            LoadCancellationTokenSource?.Dispose();
            LoadCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = LoadCancellationTokenSource.Token;
            var loadVersion = ++LoadVersion;

            IsLoading = true;
            IsInitialContentLoading = false;
            Entity = null;
            Manifest = null;
            InitialChunkWindow = null;
            OutlineNodes = Array.Empty<RichTextDocumentOutlineNode>();
            DocumentText = null;
            ViewedVersion = 0;
            LatestVersion = 1;
            Error = null;
            PrintSettings = new DocPrintSettings();
            PrintPresetCollection = new DocPrintSettingsPresetCollection();
            PrintPresetName = string.Empty;
            SelectedPrintPresetName = string.Empty;

            try
            {
                await LoadPrintSettingsAsync(cancellationToken);

                var entity = await Helper.GetBusinessEntityById(Id);
                if (entity == null)
                {
                    Error = "Документ не найден.";
                    return;
                }

                if (entity.EntityType == global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id)
                {
                    await LoadRichTextDocumentAsync(loadVersion, cancellationToken);
                    return;
                }

                if (entity.EntityType == global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id)
                {
                    await LoadPlainDocumentAsync(entity, cancellationToken);
                    return;
                }

                Error = "Печать доступна только для Document и RichTextDocument.";
            }
            catch (OperationCanceledException)
            {
                // Новая навигация отменила текущую загрузку.
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                if (loadVersion == LoadVersion)
                {
                    IsLoading = false;
                }
            }
        }

        // Загружает пользовательские пресеты печати и применяет выбранный пресет.
        private async Task LoadPrintSettingsAsync(CancellationToken cancellationToken)
        {
            PrintPresetCollection = await UserConnector.GetDocPrintPresetsAsync(cancellationToken);
            ApplyInitialPrintPreset();
        }

        // Загружает обычный документ для печати.
        private async Task LoadPlainDocumentAsync(
            BusinessEntity.Core.Classes.BusinessEntity entity,
            CancellationToken cancellationToken)
        {
            Entity = entity;
            await ResolvePlainDocumentAccessAsync(cancellationToken);
            if (!HasFullDocumentAccess && !CanViewPublishedDocument)
            {
                Entity = null;
                Error = "Документ недоступен.";
                return;
            }

            IReadOnlyList<BusinessEntityData> dataItems;
            if (HasFullDocumentAccess)
            {
                dataItems = await Helper.GetData(entity);
            }
            else
            {
                var latestDocument = await DataProviderConnector.GetDataAsync<BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document>(Id, cancellationToken);
                dataItems = latestDocument == null
                    ? Array.Empty<BusinessEntityData>()
                    : new BusinessEntityData[] { latestDocument };
            }

            DocumentText = string.Join(
                Environment.NewLine + Environment.NewLine,
                dataItems.Select(GetPlainDocumentText).Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        // Загружает rich-text документ для печати с тем же оконным viewport, что и обычный просмотр.
        private async Task LoadRichTextDocumentAsync(long loadVersion, CancellationToken cancellationToken)
        {
            var shell = await RichTextDocumentReader.GetRichTextDocumentShellAsync(Id, cancellationToken);
            if (shell == null)
            {
                Error = "Rich-text документ не найден.";
                return;
            }

            Entity = shell.Entity;
            Manifest = shell.Manifest;
            await ResolveRichTextDocumentAccessAsync(cancellationToken);
            if (!CanReadRichTextDocument())
            {
                Entity = null;
                Manifest = null;
                Error = "Документ недоступен: опубликованная версия отсутствует.";
                return;
            }

            await RefreshVersionsAsync(cancellationToken);
            if (HasFullDocumentAccess)
            {
                ViewedVersion = LatestVersion;
            }
            else if (Manifest.PublishedVersion > 0)
            {
                ViewedVersion = Math.Min(Manifest.PublishedVersion, LatestVersion);
            }

            var selected = await DataProviderConnector.GetDataVersionAsync<RichTextDocument>(Id, ViewedVersion, cancellationToken)
                ?? throw new InvalidDataException("Выбранная версия статьи отсутствует.");
            Entity.Name = selected.Name;
            IsInitialContentLoading = true;
            await LoadInitialChunkWindowAsync(loadVersion, cancellationToken);
            _ = LoadOutlineAsync(loadVersion, cancellationToken);
        }

        // Загружает первое окно rich-text чанков для печатной страницы.
        private async Task LoadInitialChunkWindowAsync(long loadVersion, CancellationToken cancellationToken)
        {
            var settings = await RichTextDocumentSettingsService.GetSettingsAsync(cancellationToken);
            var chunkWindow = await RichTextDocumentReader.GetChunkWindowAsync(
                Id,
                0,
                settings.GetInitialChunkCount(),
                ViewedVersion,
                cancellationToken);

            if (loadVersion != LoadVersion || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            InitialChunkWindow = chunkWindow;
            IsInitialContentLoading = false;
        }

        // Загружает содержание rich-text документа батчами для навигационной механики viewport.
        private async Task LoadOutlineAsync(long loadVersion, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var tableOfContents in RichTextOutlineService.GetTableOfContentsBatchesAsync(
                    Id,
                    OutlineChunkBatchSize,
                    ViewedVersion,
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
            }
            catch (OperationCanceledException)
            {
                // Новая навигация отменила текущую загрузку содержания.
            }
            catch (Exception ex)
            {
                await InvokeAsync(() =>
                {
                    if (loadVersion == LoadVersion && !cancellationToken.IsCancellationRequested)
                    {
                        Error = ex.Message;
                        StateHasChanged();
                    }
                });
            }
        }

        // Проверяет права текущего пользователя на обычный документ.
        private async Task ResolvePlainDocumentAccessAsync(CancellationToken cancellationToken)
        {
            HasFullDocumentAccess = false;
            CanViewPublishedDocument = false;

            if (Entity == null)
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
                    PublishedVersion = 0
                },
                cancellationToken);

            HasFullDocumentAccess = access.CanViewDraft;
            CanViewPublishedDocument = access.CanViewPublished;
        }

        // Проверяет права текущего пользователя на rich-text документ.
        private async Task ResolveRichTextDocumentAccessAsync(CancellationToken cancellationToken)
        {
            HasFullDocumentAccess = false;
            CanViewPublishedDocument = false;

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

            HasFullDocumentAccess = access.CanViewDraft;
            CanViewPublishedDocument = access.CanViewPublished;
        }

        // Обновляет номера доступных версий rich-text документа.
        private async Task RefreshVersionsAsync(CancellationToken cancellationToken)
        {
            var versions = await DataProviderConnector.GetDataVersionsAsync(Id, cancellationToken);
            if (versions.Count == 0 || versions.Any(x => x.Version < 1)) throw new InvalidDataException("История статьи отсутствует или повреждена.");
            LatestVersion = versions.Max(x => x.Version);

            if (ViewedVersion <= 0 || ViewedVersion > LatestVersion)
            {
                ViewedVersion = LatestVersion;
            }
        }

        // Запускает системный print dialog для печати на принтер.
        private async Task PrintToPrinterAsync()
        {
            await PrintAsync();
        }

        // Запускает системный print dialog, где браузер позволяет выбрать сохранение в PDF.
        private async Task PrintToPdfAsync()
        {
            await PrintAsync();
        }

        // Открывает системный print dialog с текущими настройками на экране.
        private async Task PrintAsync()
        {
            if (IsPrintCommandDisabled)
            {
                return;
            }

            IsPrintCommandRunning = true;
            try
            {
                await JS.InvokeVoidAsync("window.print");
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsPrintCommandRunning = false;
            }
        }

        // Сохраняет или перезаписывает пользовательский пресет печати.
        private async Task SavePrintPresetAsync()
        {
            if (IsSaveSettingsDisabled)
            {
                return;
            }

            IsPrintCommandRunning = true;
            try
            {
                var savedPreset = await UserConnector.SaveDocPrintPresetAsync(
                    new DocPrintSettingsPreset
                    {
                        Name = PrintPresetName,
                        Settings = ClonePrintSettings(PrintSettings)
                    });

                PrintPresetCollection = await UserConnector.GetDocPrintPresetsAsync();
                ApplyPrintPreset(FindPrintPreset(savedPreset.Name) ?? savedPreset);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsPrintCommandRunning = false;
            }
        }

        // Удаляет пользовательский пресет печати по имени из поля.
        private async Task DeletePrintPresetAsync()
        {
            if (IsDeleteSettingsDisabled)
            {
                return;
            }

            IsPrintCommandRunning = true;
            try
            {
                await UserConnector.DeleteDocPrintPresetAsync(NormalizePrintPresetNameInput(PrintPresetName));
                PrintPresetCollection = await UserConnector.GetDocPrintPresetsAsync();
                ApplyInitialPrintPreset();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsPrintCommandRunning = false;
            }
        }

        // Применяет сохраненный выбранный пресет либо оставляет значения по умолчанию.
        private void ApplyInitialPrintPreset()
        {
            var selected = FindPrintPreset(PrintPresetCollection.SelectedPresetName) ?? PrintPresets.FirstOrDefault();
            if (selected == null)
            {
                PrintSettings = new DocPrintSettings();
                PrintPresetName = string.Empty;
                SelectedPrintPresetName = string.Empty;
                return;
            }

            ApplyPrintPreset(selected);
        }

        // Применяет числовые настройки пресета к текущему печатному представлению.
        private void ApplyPrintPreset(DocPrintSettingsPreset preset)
        {
            PrintSettings = ClonePrintSettings(preset.Settings);
            PrintPresetName = preset.Name;
            SelectedPrintPresetName = preset.Name;
        }

        // Обрабатывает выбор пресета в комбобоксе.
        private void OnPrintPresetChanged(ChangeEventArgs args)
        {
            var presetName = args.Value?.ToString() ?? string.Empty;
            SelectedPrintPresetName = presetName;
            if (string.IsNullOrWhiteSpace(presetName))
            {
                PrintPresetName = string.Empty;
                return;
            }

            var preset = FindPrintPreset(presetName);
            if (preset != null)
            {
                ApplyPrintPreset(preset);
            }
        }

        // Ищет пресет по пользовательскому имени без учета регистра.
        private DocPrintSettingsPreset? FindPrintPreset(string? name)
        {
            var normalizedName = NormalizePrintPresetNameInput(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return null;
            }

            return PrintPresets.FirstOrDefault(x =>
                string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        }

        // Нормализует имя пресета так же, как серверная user-property логика.
        private static string NormalizePrintPresetNameInput(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Join(
                " ",
                value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        }

        // Создает независимую копию настроек с теми же clamp-правилами.
        private static DocPrintSettings ClonePrintSettings(DocPrintSettings? settings)
        {
            if (settings == null)
            {
                return new DocPrintSettings();
            }

            return new DocPrintSettings
            {
                SchemaVersion = settings.SchemaVersion > 0 ? settings.SchemaVersion : 1,
                Kind = string.IsNullOrWhiteSpace(settings.Kind) ? nameof(DocPrintSettings) : settings.Kind,
                FontScalePercent = settings.FontScalePercent,
                MarginTopMm = settings.MarginTopMm,
                MarginBottomMm = settings.MarginBottomMm,
                MarginRightMm = settings.MarginRightMm,
                MarginLeftMm = settings.MarginLeftMm
            };
        }

        // Закрывает печатный режим и возвращает пользователя на страницу документа.
        private Task CloseAsync()
        {
            NavigationManager.NavigateTo(BuildCloseUrl());
            return Task.CompletedTask;
        }

        // Определяет URL возврата по типу документа.
        private string BuildCloseUrl()
        {
            return Entity?.EntityType switch
            {
                global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id => $"/rich-document/{Id}",
                global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id => $"/document/{Id}",
                _ => "/"
            };
        }

        // Проверяет возможность чтения rich-text документа в выбранной версии.
        private bool CanReadRichTextDocument()
        {
            if (Entity == null || Manifest == null)
            {
                return false;
            }

            return HasFullDocumentAccess || (CanViewPublishedDocument && Manifest.PublishedVersion > 0);
        }

        // Возвращает текст обычного документа из storage payload.
        private static string GetPlainDocumentText(BusinessEntityData data)
        {
            return data switch
            {
                BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document document => document.Text ?? string.Empty,
                _ => string.Empty
            };
        }

        // Преобразует storage-запись содержания rich-text документа в UI-узел viewport.
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

        // Отменяет фоновые операции чтения при уничтожении страницы.
        public void Dispose()
        {
            LoadCancellationTokenSource?.Cancel();
            LoadCancellationTokenSource?.Dispose();
        }
    }
}

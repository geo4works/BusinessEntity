using BusinessEntity.Core.Saving;
using BusinessEntity.Core.BaseClasses.Relations;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal;
using BusinessEntity.Services.RichTextImport;
using BusinessEntity.WebLogger.Services;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessEntity.Services
{
    // Инкапсулирует все rich-text операции и использует BusinessEntityHelper как базовый entity-layer.
    public class RichTextDocumentHelper
    {
        public const int MaxRichTextDocumentTitleLength = 120;

        private const string InvalidTitleCharacters = "<>:\"/\\|?*";
        private const long MaxEmbeddedImageUploadBytes = 20L * 1024L * 1024L;

        // Базовый helper обычных business-entity операций.
        private readonly BusinessEntityHelper _businessEntityHelper;
        // Коннектор data-provider нужен для технических rich-text данных: chunks и embedded files.
        private readonly IDataProviderConnector _dataProviderConnector;
        private readonly HtmlToRichTextBlocksConverter _htmlToRichTextBlocksConverter;
        private readonly IWebLoggerService? _webLogger;
        // Фабрика нужна для сборки runtime entity rich-text документа.
        private readonly IBusinessEntityFactory _businessEntityFactory;

        // Подключает базовый helper и технические зависимости rich-text storage.
        public RichTextDocumentHelper(
            BusinessEntityHelper businessEntityHelper,
            IDataProviderConnector dataProviderConnector,
            HtmlToRichTextBlocksConverter htmlToRichTextBlocksConverter,
            IWebLoggerService? webLogger,
            IBusinessEntityFactory businessEntityFactory)
        {
            _businessEntityHelper = businessEntityHelper ?? throw new ArgumentNullException(nameof(businessEntityHelper));
            _dataProviderConnector = dataProviderConnector ?? throw new ArgumentNullException(nameof(dataProviderConnector));
            _htmlToRichTextBlocksConverter = htmlToRichTextBlocksConverter ?? throw new ArgumentNullException(nameof(htmlToRichTextBlocksConverter));
            _webLogger = webLogger;
            _businessEntityFactory = businessEntityFactory ?? throw new ArgumentNullException(nameof(businessEntityFactory));
        }

        // Создаёт карточку, manifest, стартовый чанк и родительскую связь одним пакетом.
        public async Task<BusinessEntity.Core.Classes.BusinessEntity> CreateRichTextDocumentAsync(
            BusinessEntity.Core.Classes.BusinessEntity parent, CancellationToken ct = default)
        {
            if (parent.EntityType != BusinessEntityTypeEnum.Folder && parent.EntityType != BusinessEntityTypeEnum.Space)
                throw new ArgumentException("Parent must be Folder or Space", nameof(parent));
            var name = await GetNewRichTextDocumentNameAsync(parent, ct);
            var entity = CreateRichTextDocumentEntity(name);
            var manifest = new RichTextDocument { Id = entity.Id, Name = name, Tag = BusinessEntityTypeEnum.RichTextDocument.ToString() };
            var chunk = new RichTextDocumentChunk { BusinessEntityId = entity.Id };
            await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
            {
                Entities = [entity], Payloads = [new(entity.Id, 0, manifest, [chunk], [chunk.Id])],
                Relations = [new BusinessEntityRelation { ObjectAId = parent.Id, ObjectBId = entity.Id,
                    RelationType = BusinessEntityRelationTypeEnum.Contains.ToString() }]
            }, ct);
            return entity;
        }

        /// <summary>
        /// Loads only entity and manifest metadata without body chunks.
        /// </summary>
        public async Task<RichTextDocumentShell?> GetRichTextDocumentShellAsync(Guid entityId, CancellationToken ct = default)
        {
            var entity = await _businessEntityHelper.GetBusinessEntityById(entityId);
            if (entity == null || entity.EntityType != BusinessEntityTypeEnum.RichTextDocument)
            {
                return null;
            }

            var typedEntity = await _businessEntityHelper.GetEntityWithDataAsync<RichTextDocument>(entity.Id, ct);
            return new RichTextDocumentShell
            {
                Entity = entity,
                Manifest = typedEntity?.Data ?? throw new InvalidDataException("Отсутствует manifest статьи.")
            };
        }

        public static string FilterRichTextDocumentTitle(string? title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(title.Length);
            var previousWasWhitespace = false;

            foreach (var ch in title)
            {
                if (char.IsControl(ch) || InvalidTitleCharacters.Contains(ch))
                {
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (previousWasWhitespace || builder.Length == 0)
                    {
                        continue;
                    }

                    builder.Append(' ');
                    previousWasWhitespace = true;
                    continue;
                }

                builder.Append(ch);
                previousWasWhitespace = false;

                if (builder.Length >= MaxRichTextDocumentTitleLength)
                {
                    break;
                }
            }

            return builder.ToString().TrimEnd(' ', '.');
        }

        public static string NormalizeRichTextDocumentTitle(string? title)
        {
            var normalized = FilterRichTextDocumentTitle(title);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Название не может быть пустым.");
            }

            return normalized;
        }

        /// <summary>
        /// Loads the rich-text document table of contents from the selected chunk versions and returns it as a tree.
        /// </summary>
        public Task<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsAsync(
            Guid entityId,
            CancellationToken ct = default)
        {
            return GetTableOfContentsAsync(entityId, documentVersion: null, ct);
        }

        /// <summary>
        /// Loads the rich-text document table of contents from the selected chunk versions and returns it as a tree.
        /// </summary>
        public async Task<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsAsync(
            Guid entityId,
            int? documentVersion,
            CancellationToken ct = default)
        {
            var flatEntries = await GetTableOfContentsEntriesFromChunksAsync(entityId, documentVersion, maxChunkCount: null, ct);
            return BuildTableOfContentsTree(flatEntries);
        }

        /// <summary>
        /// Loads persisted table-of-contents entries progressively by chunk batches.
        /// </summary>
        public IAsyncEnumerable<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsBatchesAsync(
            Guid entityId,
            int chunkBatchSize = 5,
            CancellationToken ct = default)
        {
            return GetTableOfContentsBatchesAsync(entityId, chunkBatchSize, documentVersion: null, ct);
        }

        /// <summary>
        /// Loads table-of-contents entries progressively from the selected chunk versions.
        /// </summary>
        public async IAsyncEnumerable<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsBatchesAsync(
            Guid entityId,
            int chunkBatchSize,
            int? documentVersion,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var batch in GetTableOfContentsBatchesAsync(
                entityId,
                chunkBatchSize,
                documentVersion,
                maxChunkCount: null,
                ct))
            {
                yield return batch;
            }
        }

        /// <summary>
        /// Loads table-of-contents entries progressively from a bounded prefix of selected chunk versions.
        /// </summary>
        public async IAsyncEnumerable<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsBatchesAsync(
            Guid entityId,
            int chunkBatchSize,
            int? documentVersion,
            int? maxChunkCount,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var normalizedBatchSize = Math.Max(chunkBatchSize, 1);
            var resolvedDocumentVersion = await ResolveDocumentVersionAsync(entityId, documentVersion, ct);
            var totalChunkCount = await GetChunkSortOrderUpperBoundAsync(entityId, resolvedDocumentVersion, ct);
            var effectiveChunkCount = maxChunkCount.HasValue
                ? Math.Min(totalChunkCount, Math.Max(maxChunkCount.Value, 0))
                : totalChunkCount;

            if (effectiveChunkCount <= 0)
            {
                yield return Array.Empty<RichTextDocumentTableOfContentsEntry>();
                yield break;
            }

            var flatEntries = new List<RichTextDocumentTableOfContentsEntry>();
            for (var startSortOrder = 0; startSortOrder < effectiveChunkCount; startSortOrder += normalizedBatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var take = Math.Min(normalizedBatchSize, effectiveChunkCount - startSortOrder);
                var chunkDtos = await GetSelectedChunkWindowAsync(
                    entityId,
                    startSortOrder,
                    take,
                    resolvedDocumentVersion,
                    ct);

                foreach (var chunkDto in chunkDtos.OrderBy(d => d.SortOrder))
                {
                    ct.ThrowIfCancellationRequested();

                    var blocks = chunkDto.Blocks;
                    flatEntries.AddRange(BuildTableOfContentsEntries(chunkDto, blocks));
                }

                yield return BuildTableOfContentsTree(flatEntries);
            }
        }

        /// <summary>
        /// Returns the number of persisted chunks for virtualized reading.
        /// </summary>
        public Task<int> GetChunkCountAsync(Guid entityId, CancellationToken ct = default)
        {
            return GetChunkCountAsync(entityId, documentVersion: null, ct);
        }

        /// <summary>
        /// Returns the number of persisted chunks for a specific document version.
        /// </summary>
        public async Task<int> GetChunkCountAsync(Guid entityId, int? documentVersion, CancellationToken ct = default)
        {
            var resolvedDocumentVersion = await ResolveDocumentVersionAsync(entityId, documentVersion, ct);
            return await GetChunkSortOrderUpperBoundAsync(entityId, resolvedDocumentVersion, ct);
        }

        /// <summary>
        /// Calculates chunk metrics for a specific document version without building a full document snapshot.
        /// </summary>
        public async Task<RichTextDocumentChunkStatistics> GetChunkStatisticsAsync(
            Guid entityId,
            int? documentVersion,
            CancellationToken ct = default)
        {
            var resolvedDocumentVersion = await ResolveDocumentVersionAsync(entityId, documentVersion, ct);
            var totalSortOrderUpperBound = await GetChunkSortOrderUpperBoundAsync(entityId, resolvedDocumentVersion, ct);
            if (totalSortOrderUpperBound <= 0)
            {
                return new RichTextDocumentChunkStatistics();
            }

            const int chunkBatchSize = 100;
            long totalCharacters = 0;
            var totalChunks = 0;
            var minCharacters = int.MaxValue;
            var maxCharacters = 0;

            for (var startSortOrder = 0; startSortOrder < totalSortOrderUpperBound; startSortOrder += chunkBatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var take = Math.Min(chunkBatchSize, totalSortOrderUpperBound - startSortOrder);
                var chunkDtos = await GetSelectedChunkWindowAsync(
                    entityId,
                    startSortOrder,
                    take,
                    resolvedDocumentVersion,
                    ct);

                foreach (var chunkDto in chunkDtos)
                {
                    ct.ThrowIfCancellationRequested();

                    var charCount = Math.Max(chunkDto.CharCount, 0);
                    totalChunks++;
                    totalCharacters += charCount;
                    minCharacters = Math.Min(minCharacters, charCount);
                    maxCharacters = Math.Max(maxCharacters, charCount);
                }
            }

            if (totalChunks == 0)
            {
                return new RichTextDocumentChunkStatistics();
            }

            return new RichTextDocumentChunkStatistics
            {
                TotalChunkCount = totalChunks,
                AverageCharCount = totalCharacters / (double)totalChunks,
                MinCharCount = minCharacters,
                MaxCharCount = maxCharacters
            };
        }

        /// <summary>
        /// Loads a sort-order window of chunks for virtualized reading.
        /// </summary>
        public Task<RichTextDocumentChunkWindow> GetChunkWindowAsync(
            Guid entityId,
            long startSortOrder,
            int take,
            CancellationToken ct = default)
        {
            return GetChunkWindowAsync(entityId, startSortOrder, take, documentVersion: null, ct);
        }

        /// <summary>
        /// Loads a sort-order window of chunks for a specific document version.
        /// </summary>
        public async Task<RichTextDocumentChunkWindow> GetChunkWindowAsync(
            Guid entityId,
            long startSortOrder,
            int take,
            int? documentVersion,
            CancellationToken ct = default)
        {
            var resolvedDocumentVersion = await ResolveDocumentVersionAsync(entityId, documentVersion, ct);
            var totalCount = await GetChunkSortOrderUpperBoundAsync(entityId, resolvedDocumentVersion, ct);
            if (totalCount <= 0 || take <= 0)
            {
                return new RichTextDocumentChunkWindow
                {
                    BusinessEntityId = entityId,
                    StartSortOrder = 0,
                    TotalChunkCount = totalCount,
                    DocumentVersion = resolvedDocumentVersion
                };
            }

            var normalizedStart = Math.Clamp(startSortOrder, 0, Math.Max(totalCount - 1, 0));
            var dtos = await GetSelectedChunkWindowAsync(
                entityId,
                normalizedStart,
                take,
                resolvedDocumentVersion,
                ct);

            await LogChunkWindowReadAsync(entityId, normalizedStart, take, totalCount, dtos);

            return new RichTextDocumentChunkWindow
            {
                BusinessEntityId = entityId,
                StartSortOrder = normalizedStart,
                TotalChunkCount = totalCount,
                DocumentVersion = resolvedDocumentVersion,
                Chunks = dtos.ToArray()
            };
        }

        /// <summary>
        /// Loads a chunk window centered around a target sort order.
        /// </summary>
        public Task<RichTextDocumentChunkWindow> GetChunkWindowAroundAsync(
            Guid entityId,
            long centerSortOrder,
            int before,
            int after,
            CancellationToken ct = default)
        {
            return GetChunkWindowAroundAsync(entityId, centerSortOrder, before, after, documentVersion: null, ct);
        }

        /// <summary>
        /// Loads a chunk window centered around a target sort order for a specific document version.
        /// </summary>
        public Task<RichTextDocumentChunkWindow> GetChunkWindowAroundAsync(
            Guid entityId,
            long centerSortOrder,
            int before,
            int after,
            int? documentVersion,
            CancellationToken ct = default)
        {
            var normalizedBefore = Math.Max(before, 0);
            var normalizedAfter = Math.Max(after, 0);
            var startSortOrder = Math.Max(centerSortOrder - normalizedBefore, 0);
            return GetChunkWindowAsync(
                entityId,
                startSortOrder,
                normalizedBefore + 1 + normalizedAfter,
                documentVersion,
                ct);
        }

        /// <summary>
        /// Ищет следующее или предыдущее вхождение текста по чанкам rich-text документа.
        /// </summary>
        public Task<RichTextDocumentSearchResult?> FindTextAsync(
            Guid entityId,
            string? query,
            RichTextDocumentViewportPosition? origin,
            bool searchDown,
            CancellationToken ct = default)
        {
            return FindTextAsync(entityId, query, origin, searchDown, documentVersion: null, ct);
        }

        /// <summary>
        /// Ищет следующее или предыдущее вхождение текста по чанкам конкретной версии rich-text документа.
        /// </summary>
        public async Task<RichTextDocumentSearchResult?> FindTextAsync(
            Guid entityId,
            string? query,
            RichTextDocumentViewportPosition? origin,
            bool searchDown,
            int? documentVersion,
            CancellationToken ct = default)
        {
            if (entityId == Guid.Empty)
            {
                throw new ArgumentException("Entity id is required.", nameof(entityId));
            }

            var normalizedQuery = (query ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return null;
            }

            var totalChunkCount = await GetChunkCountAsync(entityId, documentVersion, ct);
            if (totalChunkCount <= 0)
            {
                return null;
            }

            var hasOrigin = origin != null;
            var startSortOrder = Math.Clamp(origin?.ChunkSortOrder ?? (searchDown ? 0 : totalChunkCount - 1), 0, totalChunkCount - 1);
            var startBlockIndex = Math.Max(origin?.BlockIndex ?? (searchDown ? 0 : int.MaxValue), 0);

            if (searchDown)
            {
                for (var sortOrder = startSortOrder; sortOrder < totalChunkCount; sortOrder++)
                {
                    var result = await FindInChunkAsync(
                        entityId,
                        sortOrder,
                        normalizedQuery,
                        searchDown: true,
                        sortOrder == startSortOrder && hasOrigin ? startBlockIndex + 1 : 0,
                        documentVersion,
                        ct);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            else
            {
                for (var sortOrder = startSortOrder; sortOrder >= 0; sortOrder--)
                {
                    var result = await FindInChunkAsync(
                        entityId,
                        sortOrder,
                        normalizedQuery,
                        searchDown: false,
                        sortOrder == startSortOrder && hasOrigin ? startBlockIndex - 1 : int.MaxValue,
                        documentVersion,
                        ct);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        // Заголовок, manifest, изменения автора и подготовленные изображения сохраняются одним коммитом.
        public async Task<RichTextDocumentSaveResult> SaveEditedDocumentAsync(Guid entityId, int baseVersion,
            string title, string description, IReadOnlyList<RichTextDocumentChunkEditDraft> drafts, CancellationToken ct = default)
        {
            if (baseVersion < 1) throw new ArgumentOutOfRangeException(nameof(baseVersion), "Не задана база редактора.");
            var manifest = await _dataProviderConnector.GetDataVersionAsync<RichTextDocument>(entityId, baseVersion, ct)
                ?? throw new KeyNotFoundException("База редактора не найдена.");
            var normalizedTitle = NormalizeRichTextDocumentTitle(title);
            var titleChanged = manifest.Name != normalizedTitle;
            manifest.Name = normalizedTitle;
            var changed = new List<RichTextDocumentChunk>();
            var files = new List<EntityFileChange>();
            foreach (var draft in drafts)
            {
                var window = await _dataProviderConnector.GetVersionChunkWindowAsync(entityId, baseVersion, 0, 1,
                    [draft.ChunkId], cancellationToken: ct);
                var original = window.Chunks.SingleOrDefault() ?? throw new KeyNotFoundException("Чанк отсутствует в базе автора.");
                var converted = await _htmlToRichTextBlocksConverter.ConvertHtmlAsync(draft.Html, ct);
                var blocks = converted.Blocks ?? throw new InvalidDataException("Конвертер не вернул содержимое чанка.");
                if (JsonSerializer.Serialize(blocks) == JsonSerializer.Serialize(original.Blocks)) continue;
                changed.Add(new RichTextDocumentChunk { Id = original.Id, BusinessEntityId = entityId,
                    SortOrder = original.SortOrder, Blocks = blocks.ToList() });
                files.AddRange(converted.Files.Select(file => FileChange(entityId, file)));
            }
            var result = await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
                { Payloads = [new(entityId, baseVersion, manifest, changed, VersionDescription: description)], Files = files }, ct);
            var saved = result.Versions.Single();
            return new(saved.Version, saved.CreatedVersion ? changed.Count : 0, saved.CreatedVersion, titleChanged);
        }

        // Логирует каждое фактическое чтение rich-text chunk DTO для наблюдения за virtual viewport.
        private async Task LogChunkWindowReadAsync(
            Guid entityId,
            long normalizedStart,
            int requestedTake,
            int totalCount,
            IReadOnlyList<RichTextDocumentChunk> chunkDtos)
        {
            if (_webLogger == null || chunkDtos.Count == 0)
            {
                return;
            }

            foreach (var chunkDto in chunkDtos)
            {
                await _webLogger.Information(
                    "[rich-doc-chunk-read] " +
                    $"chunkId={chunkDto.Id:D} " +
                    $"sortOrder={chunkDto.SortOrder} " +
                    $"windowStart={normalizedStart} " +
                    $"requestedTake={requestedTake} " +
                    $"totalChunks={totalCount} " +
                    $"blockCount={chunkDto.BlockCount} " +
                    $"charCount={chunkDto.CharCount} " +
                    $"dataSizeBytes={chunkDto.DataSizeBytes} " +
                    $"htmlLength={chunkDto.HtmlCache?.Length ?? 0}");
            }
        }

        // Постоянный файл читает владелец физического хранения.
        public Task<RichTextEmbeddedFileContent?> GetRichTextEmbeddedFileAsync(Guid entityId, string imageId,
            string variant, CancellationToken ct = default)
            => _dataProviderConnector.GetRichTextEmbeddedFileAsync(entityId, imageId, variant, ct);

        /// <summary>
        /// Сохраняет одно embedded-изображение rich-text документа без изменения chunk-ов.
        /// </summary>
        public async Task<RichTextEmbeddedImageUploadResult> SaveRichTextEmbeddedImageAsync(
            Guid entityId,
            Stream content,
            string? fileName,
            string? contentType,
            CancellationToken ct = default)
        {
            if (entityId == Guid.Empty)
            {
                throw new ArgumentException("Entity id is required.", nameof(entityId));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var shell = await GetRichTextDocumentShellAsync(entityId, ct);
            if (shell == null)
            {
                throw new InvalidOperationException("Rich-text документ не найден.");
            }

            var normalizedContentType = NormalizeImageContentType(contentType);
            var normalizedFileName = NormalizeEmbeddedFileName(fileName, normalizedContentType);
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            if (buffer.Length <= 0)
            {
                throw new InvalidOperationException("Файл изображения пустой.");
            }

            if (buffer.Length > MaxEmbeddedImageUploadBytes)
            {
                throw new InvalidOperationException(
                    $"Размер изображения превышает лимит {MaxEmbeddedImageUploadBytes / 1024 / 1024} MB.");
            }

            var imageId = Guid.NewGuid().ToString("D");
            var variant = "original";
            var embeddedFile = new RichTextEmbeddedFile
            {
                ImageId = imageId,
                Variant = variant,
                FileName = normalizedFileName,
                ContentType = normalizedContentType,
                Content = buffer.ToArray()
            };

            await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
                { Files = [FileChange(entityId, embeddedFile)] }, ct);

            return new RichTextEmbeddedImageUploadResult
            {
                ImageId = imageId,
                Variant = variant,
                Url = $"/rich-document-files/{entityId:D}/images/{Uri.EscapeDataString(imageId)}/{variant}",
                FileName = normalizedFileName,
                ContentType = normalizedContentType
            };
        }

        // Полная замена состава создаёт новый снимок, не стирая ни одной исторической строки или файла.
        public async Task SaveRichTextDocumentAsync(BusinessEntity.Core.Classes.BusinessEntity entity,
            RichTextDocument manifest, IReadOnlyList<RichTextDocumentChunk> chunks,
            IReadOnlyList<RichTextEmbeddedFile>? files, CancellationToken ct = default)
        {
            manifest.Id = entity.Id; manifest.Name = entity.Name;
            var existing = await _dataProviderConnector.GetDataAsync<RichTextDocument>(entity.Id, ct);
            var snapshot = chunks.OrderBy(x => x.SortOrder).Select((chunk, index) => CloneChunkForSave(chunk, entity.Id, index)).ToArray();
            var saved = await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
            {
                Entities = [entity], Payloads = [new(entity.Id, existing == null ? 0 : manifest.Version, manifest,
                    snapshot, snapshot.Select(x => x.Id).ToArray())],
                Files = (files ?? []).Select(file => FileChange(entity.Id, file)).ToArray()
            }, ct);
            manifest.Version = saved.Versions.Single().Version;
        }

        // Добавляет импорт к базе автора; удаление пустого стартового чанка затрагивает только новый состав.
        public async Task AppendImportedContentAsync(BusinessEntity.Core.Classes.BusinessEntity entity,
            RichTextDocument manifest, IReadOnlyList<RichTextDocumentChunk> appendedChunks,
            IReadOnlyList<RichTextEmbeddedFile>? appendedFiles, CancellationToken ct = default)
        {
            var membership = await _dataProviderConnector.GetVersionChunkMembershipAsync(entity.Id, manifest.Version, ct);
            var order = membership.Select(x => x.ChunkId).ToList();
            if (order.Count == 1 && IsOnlyInitialEmptyChunk((await GetChunkWindowAsync(entity.Id, 0, 1, manifest.Version, ct)).Chunks))
                order.Clear();
            var inserted = appendedChunks.OrderBy(x => x.SortOrder)
                .Select((chunk, index) => CloneChunkForSave(chunk, entity.Id, order.Count + index)).ToArray();
            order.AddRange(inserted.Select(x => x.Id));
            manifest.Name = entity.Name;
            var result = await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
            {
                Payloads = [new(entity.Id, manifest.Version, manifest, inserted, order)],
                Files = (appendedFiles ?? []).Select(file => FileChange(entity.Id, file)).ToArray()
            }, ct);
            manifest.Version = result.Versions.Single().Version;
        }

        /// <summary>
        /// Генерирует следующее имя rich-text документа среди дочерних элементов родителя.
        /// </summary>
        private async Task<string> GetNewRichTextDocumentNameAsync(
            BusinessEntity.Core.Classes.BusinessEntity parent,
            CancellationToken ct)
        {
            var baseName = $"New{BusinessEntityTypeEnum.RichTextDocument}";
            var children = await _businessEntityHelper.GetContainedEntitiesAsync(parent.Id, ct);
            var sameTypeChildren = children
                .Where(c => c.EntityType == BusinessEntityTypeEnum.RichTextDocument)
                .Select(c => c.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var i = 1; ; i++)
            {
                var candidateName = $"{baseName}{i}";
                if (!sameTypeChildren.Contains(candidateName))
                {
                    return candidateName;
                }
            }
        }

        /// <summary>
        /// Создает runtime entity rich-text документа как document-подтип с пустым manifest.
        /// </summary>
        private BusinessEntity.Core.Classes.BusinessEntity CreateRichTextDocumentEntity(string name)
        {
            var entity = _businessEntityFactory.Create(
                BusinessEntityTypeEnum.RichTextDocument,
                new RichTextDocument
                {
                    Name = name,
                    Tag = BusinessEntityTypeEnum.RichTextDocument.ToString()
                },
                name);

            return entity;
        }

        /// <summary>
        /// Клонирует chunk в новый runtime-экземпляр перед replace-save, чтобы не тащить старые служебные ссылки.
        /// </summary>
        private static RichTextDocumentChunk CloneChunkForSave(RichTextDocumentChunk source, Guid businessEntityId, long sortOrder)
        {
            return new RichTextDocumentChunk
            {
                Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
                BusinessEntityId = businessEntityId,
                SortOrder = sortOrder,
                Blocks = source.Blocks?.Select(CloneBlock).ToList() ?? new List<RichTextBlock>(),
                Version = source.Version
            };
        }

        // Сохраняет все поля блока, включая размеры изображения и видео.
        private static RichTextBlock CloneBlock(RichTextBlock source) =>
            JsonSerializer.Deserialize<RichTextBlock>(JsonSerializer.Serialize(source))
                ?? throw new InvalidDataException("Не удалось скопировать блок.");

        private async Task<RichTextDocumentSearchResult?> FindInChunkAsync(
            Guid entityId,
            long sortOrder,
            string query,
            bool searchDown,
            int startBlockIndex,
            int? documentVersion,
            CancellationToken ct)
        {
            var window = await GetChunkWindowAsync(entityId, sortOrder, 1, documentVersion, ct);
            var chunk = window.Chunks.FirstOrDefault(x => x.SortOrder == sortOrder);
            if (chunk?.Blocks == null || chunk.Blocks.Count == 0)
            {
                return null;
            }

            var blockIndexes = searchDown
                ? BuildForwardBlockIndexes(startBlockIndex, chunk.Blocks.Count)
                : BuildBackwardBlockIndexes(startBlockIndex, chunk.Blocks.Count);

            foreach (var blockIndex in blockIndexes)
            {
                var blockText = BuildBlockSearchText(chunk.Blocks[blockIndex]);
                if (string.IsNullOrWhiteSpace(blockText))
                {
                    continue;
                }

                if (blockText.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    return new RichTextDocumentSearchResult
                    {
                        DocumentId = entityId,
                        Query = query,
                        Position = new RichTextDocumentViewportPosition
                        {
                            ChunkSortOrder = chunk.SortOrder,
                            BlockIndex = blockIndex
                        },
                        Preview = BuildSearchPreview(blockText, query)
                    };
                }
            }

            return null;
        }

        private static IEnumerable<int> BuildForwardBlockIndexes(int startBlockIndex, int blockCount)
        {
            if (blockCount <= 0 || startBlockIndex >= blockCount)
            {
                return Array.Empty<int>();
            }

            var start = Math.Max(startBlockIndex, 0);
            return Enumerable.Range(start, blockCount - start);
        }

        private static IEnumerable<int> BuildBackwardBlockIndexes(int startBlockIndex, int blockCount)
        {
            if (blockCount <= 0 || startBlockIndex < 0)
            {
                return Array.Empty<int>();
            }

            var start = Math.Min(startBlockIndex, blockCount - 1);
            return Enumerable.Range(0, start + 1).Reverse();
        }

        private static string BuildBlockSearchText(RichTextBlock block)
        {
            return block.Kind switch
            {
                "heading" or "paragraph" or "code" or "list" => RichTextChunkStorageSerializer.BuildInlineText(block.Html),
                "image" => block.AltText ?? string.Empty,
                _ => string.Empty
            };
        }

        private static string BuildSearchPreview(string text, string query)
        {
            var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return text.Length <= 160 ? text : text[..160] + "...";
            }

            var start = Math.Max(index - 60, 0);
            var length = Math.Min(query.Length + 120, text.Length - start);
            var prefix = start > 0 ? "..." : string.Empty;
            var suffix = start + length < text.Length ? "..." : string.Empty;
            return prefix + text.Substring(start, length).Trim() + suffix;
        }

        private async Task<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsEntriesFromChunksAsync(
            Guid entityId,
            int? documentVersion,
            int? maxChunkCount,
            CancellationToken ct)
        {
            var resolvedDocumentVersion = await ResolveDocumentVersionAsync(entityId, documentVersion, ct);
            var totalChunkCount = await GetChunkSortOrderUpperBoundAsync(entityId, resolvedDocumentVersion, ct);
            var effectiveChunkCount = maxChunkCount.HasValue
                ? Math.Min(totalChunkCount, Math.Max(maxChunkCount.Value, 0))
                : totalChunkCount;

            var entries = new List<RichTextDocumentTableOfContentsEntry>();
            if (effectiveChunkCount <= 0)
            {
                return entries;
            }

            const int chunkBatchSize = 25;
            for (var startSortOrder = 0; startSortOrder < effectiveChunkCount; startSortOrder += chunkBatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var take = Math.Min(chunkBatchSize, effectiveChunkCount - startSortOrder);
                var chunkDtos = await GetSelectedChunkWindowAsync(
                    entityId,
                    startSortOrder,
                    take,
                    resolvedDocumentVersion,
                    ct);

                foreach (var chunkDto in chunkDtos)
                {
                    ct.ThrowIfCancellationRequested();

                    var blocks = chunkDto.Blocks;
                    entries.AddRange(BuildTableOfContentsEntries(chunkDto, blocks));
                }
            }

            return entries;
        }

        private static IReadOnlyList<RichTextDocumentTableOfContentsEntry> BuildTableOfContentsEntries(
            RichTextDocumentChunk chunkDto,
            IReadOnlyList<RichTextBlock>? blocks)
        {
            if (blocks == null || blocks.Count == 0)
            {
                return Array.Empty<RichTextDocumentTableOfContentsEntry>();
            }

            var entries = new List<RichTextDocumentTableOfContentsEntry>();
            for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                var block = blocks[blockIndex];
                if (!string.Equals(block.Kind, "heading", StringComparison.Ordinal))
                {
                    continue;
                }

                var level = block.Level;
                if (level < 1 || level > 3)
                {
                    continue;
                }

                var title = RichTextChunkStorageSerializer.BuildInlineText(block.Html);
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = $"Heading {blockIndex + 1}";
                }

                entries.Add(new RichTextDocumentTableOfContentsEntry
                {
                    ChunkId = chunkDto.Id,
                    ChunkSortOrder = chunkDto.SortOrder,
                    BlockIndex = blockIndex,
                    Level = level,
                    Title = title,
                    Anchor = RichTextChunkStorageSerializer.BuildBlockAnchor(chunkDto.Id, blockIndex)
                });
            }

            return entries;
        }

        // Отсутствующий manifest не подменяется безграничной или фиктивной версией.
        private async Task<int> GetLatestDocumentVersionAsync(Guid entityId, CancellationToken ct)
        {
            var versions = await _dataProviderConnector.GetDataVersionsAsync(entityId, ct);
            if (versions.Count == 0 || versions.Any(v => v.Version < 1)) throw new InvalidDataException("История статьи отсутствует или повреждена.");
            return versions.Max(v => v.Version);
        }

        private async Task<int> ResolveDocumentVersionAsync(Guid entityId, int? documentVersion, CancellationToken ct)
        {
            return documentVersion.HasValue && documentVersion.Value > 0
                ? documentVersion.Value
                : await GetLatestDocumentVersionAsync(entityId, ct);
        }

        // Читает размер состава версии без загрузки тела документа.
        private async Task<int> GetChunkSortOrderUpperBoundAsync(Guid entityId, int resolvedDocumentVersion, CancellationToken ct)
            => (await _dataProviderConnector.GetVersionChunkWindowAsync(entityId, resolvedDocumentVersion, 0, 0, cancellationToken: ct)).TotalChunkCount;

        // Читает точные строки нужного окна по списку ссылок версии.
        private async Task<IReadOnlyList<RichTextDocumentChunk>> GetSelectedChunkWindowAsync(Guid entityId,
            long startSortOrder, int take, int resolvedDocumentVersion, CancellationToken ct)
            => (await _dataProviderConnector.GetVersionChunkWindowAsync(entityId, resolvedDocumentVersion, startSortOrder,
                Math.Max(take, 0), cancellationToken: ct)).Chunks;

        /// <summary>
        /// Проверяет, состоит ли документ только из одного пустого стартового chunk-а.
        /// </summary>
        private static bool IsOnlyInitialEmptyChunk(IReadOnlyList<RichTextDocumentChunk> chunks)
        {
            if (chunks == null || chunks.Count != 1)
            {
                return false;
            }

            var chunk = chunks[0];
            return chunk.Blocks == null || chunk.Blocks.Count == 0;
        }

        private static string NormalizeImageContentType(string? contentType)
        {
            var normalized = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType.Trim();

            if (!normalized.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Можно загружать только изображения.");
            }

            return normalized;
        }

        private static string NormalizeEmbeddedFileName(string? fileName, string contentType)
        {
            var normalized = Path.GetFileName(fileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = $"clipboard-image.{GetExtensionFromContentType(contentType)}";
            }

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                normalized = normalized.Replace(invalidChar, '_');
            }

            return normalized.Length <= 120
                ? normalized
                : normalized[^120..];
        }

        private static string GetExtensionFromContentType(string contentType)
        {
            return contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => "jpg",
                "image/png" => "png",
                "image/gif" => "gif",
                "image/webp" => "webp",
                "image/svg+xml" => "svg",
                _ => "bin"
            };
        }

        // Builds a hierarchical H1-H3 tree from persisted flat table-of-contents entries.
        private static IReadOnlyList<RichTextDocumentTableOfContentsEntry> BuildTableOfContentsTree(IReadOnlyList<RichTextDocumentTableOfContentsEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return Array.Empty<RichTextDocumentTableOfContentsEntry>();
            }

            var roots = new List<RichTextDocumentTableOfContentsEntry>();
            var stack = new Stack<RichTextDocumentTableOfContentsEntry>();

            foreach (var entry in entries
                .OrderBy(x => x.ChunkSortOrder)
                .ThenBy(x => x.BlockIndex))
            {
                var node = CloneTableOfContentsEntry(entry);
                while (stack.Count > 0 && stack.Peek().Level >= node.Level)
                {
                    stack.Pop();
                }

                if (stack.Count == 0)
                {
                    roots.Add(node);
                }
                else
                {
                    stack.Peek().Children.Add(node);
                }

                stack.Push(node);
            }

            return roots;
        }

        // Copies a persisted entry into a tree node with its own children collection.
        private static RichTextDocumentTableOfContentsEntry CloneTableOfContentsEntry(RichTextDocumentTableOfContentsEntry source)
        {
            return new RichTextDocumentTableOfContentsEntry
            {
                ChunkId = source.ChunkId,
                ChunkSortOrder = source.ChunkSortOrder,
                BlockIndex = source.BlockIndex,
                Level = source.Level,
                Title = source.Title,
                Anchor = source.Anchor,
                Children = new List<RichTextDocumentTableOfContentsEntry>()
            };
        }

        // Формирует типизированное изменение файла; его подготовка и публикация принадлежат DataProvider.
        private static EntityFileChange FileChange(Guid entityId, RichTextEmbeddedFile file)
            => new(entityId, file.ImageId, file.Variant, file.FileName, file.ContentType, file.Content);
    }
}

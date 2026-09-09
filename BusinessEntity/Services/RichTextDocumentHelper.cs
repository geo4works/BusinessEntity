using BusinessEntity.Core.BaseClasses.Relations;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Dtos;
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
        private readonly IAsyncRepository<BusinessEntityDataChunkDto> _businessEntityDataChunkRepository;
        private readonly IAsyncRepository<BusinessEntityDataChunkPropertyDto> _businessEntityDataChunkPropertyRepository;
        private readonly HtmlToRichTextBlocksConverter _htmlToRichTextBlocksConverter;
        private readonly IWebLoggerService? _webLogger;
        // Фабрика нужна для сборки runtime entity rich-text документа.
        private readonly IBusinessEntityFactory _businessEntityFactory;

        // Подключает базовый helper и технические зависимости rich-text storage.
        public RichTextDocumentHelper(
            BusinessEntityHelper businessEntityHelper,
            IDataProviderConnector dataProviderConnector,
            IAsyncRepository<BusinessEntityDataChunkDto> businessEntityDataChunkRepository,
            IAsyncRepository<BusinessEntityDataChunkPropertyDto> businessEntityDataChunkPropertyRepository,
            HtmlToRichTextBlocksConverter htmlToRichTextBlocksConverter,
            IWebLoggerService? webLogger,
            IBusinessEntityFactory businessEntityFactory)
        {
            _businessEntityHelper = businessEntityHelper ?? throw new ArgumentNullException(nameof(businessEntityHelper));
            _dataProviderConnector = dataProviderConnector ?? throw new ArgumentNullException(nameof(dataProviderConnector));
            _businessEntityDataChunkRepository = businessEntityDataChunkRepository ?? throw new ArgumentNullException(nameof(businessEntityDataChunkRepository));
            _businessEntityDataChunkPropertyRepository = businessEntityDataChunkPropertyRepository ?? throw new ArgumentNullException(nameof(businessEntityDataChunkPropertyRepository));
            _htmlToRichTextBlocksConverter = htmlToRichTextBlocksConverter ?? throw new ArgumentNullException(nameof(htmlToRichTextBlocksConverter));
            _webLogger = webLogger;
            _businessEntityFactory = businessEntityFactory ?? throw new ArgumentNullException(nameof(businessEntityFactory));
        }

        /// <summary>
        /// Создает новый пустой rich-text документ под папкой или пространством.
        /// </summary>
        public async Task<BusinessEntity.Core.Classes.BusinessEntity> CreateRichTextDocumentAsync(
            BusinessEntity.Core.Classes.BusinessEntity parent,
            CancellationToken ct = default)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (parent.EntityType != BusinessEntityTypeEnum.Folder && parent.EntityType != BusinessEntityTypeEnum.Space)
            {
                throw new ArgumentException("Parent must be a Folder or Space", nameof(parent));
            }

            // Имя rich-text документа генерируем по той же схеме, что и остальные дочерние элементы дерева.
            var name = await GetNewRichTextDocumentNameAsync(parent, ct);

            // Базовую entity создаем как подтип документа, чтобы дерево и страницы видели корректную предметную роль.
            var entity = CreateRichTextDocumentEntity(name);
            await _dataProviderConnector.AddAsync(entity, cancellationToken: ct);

            // Связь дерева rich-text документ не инкапсулирует сам — она идет через обычный helper relations.
            await _businessEntityHelper.CreateRelation(
                parent,
                entity,
                new MacroRelationType { RelationType = BusinessEntityRelationTypeEnum.Contains });

            // На старте у rich-text документа всегда есть manifest и один пустой chunk.
            var manifest = new RichTextDocument
            {
                Name = name,
                Tag = BusinessEntityTypeEnum.RichTextDocument.ToString()
            };

            var initialChunk = new RichTextDocumentChunk
            {
                BusinessEntityId = entity.Id,
                SortOrder = 0,
                Blocks = new List<RichTextBlock>()
            };

            await SaveRichTextDocumentAsync(
                entity,
                manifest,
                new[] { initialChunk },
                Array.Empty<RichTextEmbeddedFile>(),
                replaceExistingFiles: true,
                ct);

            return entity;
        }

        /// <summary>
        /// Загружает readonly-снимок rich-text документа: entity, manifest и набор chunk-ов.
        /// </summary>
        public Task<RichTextDocumentSnapshot?> GetRichTextDocumentSnapshotAsync(Guid entityId, CancellationToken ct = default)
        {
            throw new InvalidOperationException(
                "Full rich-text document snapshots are disabled. Use versioned chunk windows instead.");
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
                Manifest = typedEntity?.Data ?? new RichTextDocument()
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

        public async Task<RichTextDocumentTitleSaveResult> SaveRichTextDocumentTitleAsync(
            Guid entityId,
            string? title,
            CancellationToken ct = default)
        {
            if (entityId == Guid.Empty)
            {
                throw new ArgumentException("Entity id is required.", nameof(entityId));
            }

            var normalizedTitle = NormalizeRichTextDocumentTitle(title);
            var shell = await GetRichTextDocumentShellAsync(entityId, ct);
            if (shell == null)
            {
                throw new InvalidOperationException("Rich-text документ не найден.");
            }

            var titleChanged = !string.Equals(shell.Entity.Name, normalizedTitle, StringComparison.Ordinal);
            if (!titleChanged)
            {
                return new RichTextDocumentTitleSaveResult
                {
                    Entity = shell.Entity,
                    Title = normalizedTitle,
                    TitleChanged = false
                };
            }

            shell.Entity.Name = normalizedTitle;
            shell.Entity.EntityType = BusinessEntityTypeEnum.RichTextDocument;
            shell.Entity.BusinessEntityType = BusinessEntityTypeEnum.Document;
            shell.Entity.LastModifiedDate = DateTime.UtcNow;

            shell.Manifest.Id = shell.Entity.Id;
            shell.Manifest.Name = normalizedTitle;
            shell.Manifest.EntityType = BusinessEntityTypeEnum.RichTextDocument;
            shell.Manifest.LastModifiedDate = shell.Entity.LastModifiedDate;
            if (shell.Manifest.CreatedDate == default)
            {
                shell.Manifest.CreatedDate = shell.Entity.CreatedDate;
            }

            if (string.IsNullOrWhiteSpace(shell.Manifest.Tag))
            {
                shell.Manifest.Tag = BusinessEntityTypeEnum.RichTextDocument.ToString();
            }

            await _businessEntityHelper.SaveEntity(shell.Entity, shell.Manifest);

            return new RichTextDocumentTitleSaveResult
            {
                Entity = shell.Entity,
                Title = normalizedTitle,
                TitleChanged = true
            };
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
                var chunkDtos = await GetSelectedChunkWindowDtosAsync(
                    entityId,
                    startSortOrder,
                    take,
                    resolvedDocumentVersion,
                    ct);

                foreach (var chunkDto in chunkDtos.OrderBy(d => d.SortOrder))
                {
                    ct.ThrowIfCancellationRequested();

                    var blocks = RichTextChunkStorageSerializer.DeserializeChunkData(chunkDto.Data);
                    flatEntries.AddRange(BuildTableOfContentsEntries(chunkDto, blocks));
                }

                yield return BuildTableOfContentsTree(flatEntries);
            }
        }

        /// <summary>
        /// Rebuilds persisted chunk table-of-contents properties and returns the fresh tree.
        /// </summary>
        public Task<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> RebuildTableOfContentsAsync(Guid entityId, CancellationToken ct = default)
        {
            throw new InvalidOperationException(
                "Full rich-text table-of-contents rebuild is disabled. Use bounded table-of-contents loading instead.");
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
                var chunkDtos = await GetSelectedChunkWindowDtosAsync(
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
                    TotalChunkCount = totalCount
                };
            }

            var normalizedStart = Math.Clamp(startSortOrder, 0, Math.Max(totalCount - 1, 0));
            var dtos = await GetSelectedChunkWindowDtosAsync(
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
                Chunks = dtos.Select(MapChunkDtoToRuntime).ToList()
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

        /// <summary>
        /// Saves dirty chunks from rich-document edit mode without replacing the whole chunk set.
        /// </summary>
        public async Task<int> SaveEditedChunksAsync(
            Guid entityId,
            IReadOnlyList<RichTextDocumentChunkEditDraft> drafts,
            CancellationToken ct = default)
        {
            if (entityId == Guid.Empty)
            {
                throw new ArgumentException("Entity id is required.", nameof(entityId));
            }

            if (drafts == null || drafts.Count == 0)
            {
                return 0;
            }

            var latestDocumentVersion = await GetLatestDocumentVersionAsync(entityId, ct);
            var nextDocumentVersion = latestDocumentVersion == int.MaxValue
                ? 1
                : latestDocumentVersion + 1;
            var savedCount = 0;
            foreach (var draft in drafts
                .Where(x => x != null)
                .GroupBy(x => x.ChunkId != Guid.Empty ? x.ChunkId.ToString("D") : $"sort:{x.SortOrder}")
                .Select(group => group.Last())
                .OrderBy(x => x.SortOrder))
            {
                var chunkDto = await FindChunkDtoAsync(entityId, draft, ct);
                if (chunkDto == null)
                {
                    continue;
                }

                var converted = await _htmlToRichTextBlocksConverter.ConvertHtmlAsync(draft.Html ?? string.Empty, ct);
                var blocks = converted.Blocks ?? new List<RichTextBlock>();
                var dataJson = RichTextChunkStorageSerializer.SerializeChunkData(blocks);
                var checksum = RichTextChunkStorageSerializer.BuildChecksum(dataJson);
                if (IsSameChunkData(chunkDto, dataJson, checksum))
                {
                    continue;
                }

                if (converted.Files.Count > 0)
                {
                    // Inline HTML save may import data-uri/http images into embedded files before chunk JSON references them.
                    await _dataProviderConnector.SaveRichTextEmbeddedFilesAsync(
                        entityId,
                        converted.Files,
                        replaceExistingFiles: false,
                        ct);
                }

                var now = DateTime.UtcNow;
                var newChunkDto = new BusinessEntityDataChunkDto
                {
                    Id = chunkDto.Id,
                    CreatedDate = now,
                    LastModifiedDate = now,
                    BusinessEntityId = entityId,
                    SortOrder = chunkDto.SortOrder,
                    Data = dataJson,
                    PlainText = RichTextChunkStorageSerializer.BuildPlainText(blocks),
                    HtmlCache = string.Empty,
                    BlockCount = blocks.Count,
                    CharCount = RichTextChunkStorageSerializer.BuildCharCount(blocks),
                    DataSizeBytes = DataPayloadEnvelopeSerializer.GetJsonLength(dataJson),
                    Version = nextDocumentVersion,
                    Checksum = checksum
                };
                newChunkDto.HtmlCache = RichTextChunkStorageSerializer.BuildHtmlCache(entityId, newChunkDto.Id, blocks);

                var savedChunkDto = await _businessEntityDataChunkRepository.AddAsync(newChunkDto, ct);
                await RebuildTableOfContentsPropertyAsync(savedChunkDto, blocks, ct);
                savedCount++;
            }

            if (savedCount > 0)
            {
                var shell = await GetRichTextDocumentShellAsync(entityId, ct);
                if (shell != null)
                {
                    shell.Entity.LastModifiedDate = DateTime.UtcNow;
                    shell.Manifest.LastModifiedDate = shell.Entity.LastModifiedDate;
                    await _businessEntityHelper.SaveEntity(shell.Entity, shell.Manifest);
                }
            }

            return savedCount;
        }

        private async Task<BusinessEntityDataChunkDto?> FindChunkDtoAsync(
            Guid entityId,
            RichTextDocumentChunkEditDraft draft,
            CancellationToken ct)
        {
            if (draft.ChunkId != Guid.Empty)
            {
                var documentVersion = await GetLatestDocumentVersionAsync(entityId, ct);
                var byId = await _businessEntityDataChunkRepository.GetAllAsync(
                    x => x.BusinessEntityId == entityId && x.Id == draft.ChunkId,
                    ct: ct);
                var chunkDto = SelectChunkVersions(byId, documentVersion).FirstOrDefault();
                if (chunkDto != null)
                {
                    return chunkDto;
                }
            }

            var bySortOrder = await _businessEntityDataChunkRepository.GetAllAsync(
                x => x.BusinessEntityId == entityId && x.SortOrder == draft.SortOrder,
                ct: ct);

            var latestDocumentVersion = await GetLatestDocumentVersionAsync(entityId, ct);
            return SelectChunkVersions(bySortOrder, latestDocumentVersion).FirstOrDefault();
        }

        private async Task RebuildTableOfContentsPropertyAsync(
            BusinessEntityDataChunkDto chunkDto,
            IReadOnlyList<RichTextBlock> blocks,
            CancellationToken ct)
        {
            await DeletePropertiesAsync(
                _businessEntityDataChunkPropertyRepository,
                chunkDto.Id,
                (int)BusinessEntityDataChunkPropertyTypeEnum.RichDocTableOfContents,
                ct);

            var tableOfContentsProperty = BuildTableOfContentsProperty(chunkDto, blocks);
            if (tableOfContentsProperty != null)
            {
                await _businessEntityDataChunkPropertyRepository.AddAsync(tableOfContentsProperty, ct);
            }
        }

        // Логирует каждое фактическое чтение rich-text chunk DTO для наблюдения за virtual viewport.
        private async Task LogChunkWindowReadAsync(
            Guid entityId,
            long normalizedStart,
            int requestedTake,
            int totalCount,
            IReadOnlyList<BusinessEntityDataChunkDto> chunkDtos)
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

        /// <summary>
        /// Возвращает embedded-файл rich-text документа для HTTP-выдачи.
        /// </summary>
        public Task<RichTextEmbeddedFileContent?> GetRichTextEmbeddedFileAsync(
            Guid entityId,
            string imageId,
            string variant,
            CancellationToken ct = default)
        {
            return _dataProviderConnector.GetRichTextEmbeddedFileAsync(entityId, imageId, variant, ct);
        }

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

            await _dataProviderConnector.SaveRichTextEmbeddedFilesAsync(
                entityId,
                new[] { embeddedFile },
                replaceExistingFiles: false,
                ct);

            return new RichTextEmbeddedImageUploadResult
            {
                ImageId = imageId,
                Variant = variant,
                Url = $"/rich-document-files/{entityId:D}/images/{Uri.EscapeDataString(imageId)}/{variant}",
                FileName = normalizedFileName,
                ContentType = normalizedContentType
            };
        }

        /// <summary>
        /// Сохраняет rich-text документ как fan-out: entity, manifest, chunks и embedded files.
        /// </summary>
        public async Task SaveRichTextDocumentAsync(
            BusinessEntity.Core.Classes.BusinessEntity entity,
            RichTextDocument manifest,
            IReadOnlyList<RichTextDocumentChunk> chunks,
            IReadOnlyList<RichTextEmbeddedFile>? files,
            bool replaceExistingFiles,
            CancellationToken ct = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            // Нормализуем entity как document-подтип rich-text.
            entity.EntityType = BusinessEntityTypeEnum.RichTextDocument;
            entity.BusinessEntityType = BusinessEntityTypeEnum.Document;
            entity.LastModifiedDate = DateTime.UtcNow;

            // Manifest должен быть синхронизирован с основной entity до сохранения через базовый helper.
            manifest.Id = entity.Id;
            manifest.Name = entity.Name;
            manifest.EntityType = BusinessEntityTypeEnum.RichTextDocument;
            manifest.LastModifiedDate = entity.LastModifiedDate;
            if (manifest.CreatedDate == default)
            {
                manifest.CreatedDate = entity.CreatedDate;
            }

            if (string.IsNullOrWhiteSpace(manifest.Tag))
            {
                manifest.Tag = BusinessEntityTypeEnum.RichTextDocument.ToString();
            }

            // Entity и manifest идут через общий helper-path, чтобы storage-формализация оставалась централизованной.
            await _businessEntityHelper.SaveEntity(entity, manifest);

            // Техническое rich-text содержимое сохраняется отдельным storage fan-out.
            await _dataProviderConnector.ReplaceRichTextChunksAsync(entity.Id, chunks ?? Array.Empty<RichTextDocumentChunk>(), ct);
            await _dataProviderConnector.SaveRichTextEmbeddedFilesAsync(
                entity.Id,
                files ?? Array.Empty<RichTextEmbeddedFile>(),
                replaceExistingFiles,
                ct);
        }

        /// <summary>
        /// Добавляет новый импортированный контент в конец существующего rich-text документа.
        /// </summary>
        public async Task AppendImportedContentAsync(
            BusinessEntity.Core.Classes.BusinessEntity entity,
            RichTextDocument manifest,
            IReadOnlyList<RichTextDocumentChunk> appendedChunks,
            IReadOnlyList<RichTextEmbeddedFile>? appendedFiles,
            CancellationToken ct = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            var latestDocumentVersion = await GetLatestDocumentVersionAsync(entity.Id, ct);
            var targetDocumentVersion = latestDocumentVersion == int.MaxValue
                ? 1
                : latestDocumentVersion + 1;
            var firstWindow = await GetChunkWindowAsync(entity.Id, 0, 2, latestDocumentVersion, ct);

            // Для нового пустого документа стартовый технический chunk убираем, чтобы импорт не создавал пустую строку сверху.
            var nextSortOrder = firstWindow.TotalChunkCount;
            var firstWindowChunks = firstWindow.Chunks ?? Array.Empty<RichTextDocumentChunk>();
            if (IsOnlyInitialEmptyChunk(firstWindowChunks))
            {
                var initialChunk = firstWindowChunks.First();
                await DeletePropertiesAsync(
                    _businessEntityDataChunkPropertyRepository,
                    initialChunk.Id,
                    (int)BusinessEntityDataChunkPropertyTypeEnum.RichDocTableOfContents,
                    ct);
                await _businessEntityDataChunkRepository.DeleteAsync(initialChunk.Id, ct);
                nextSortOrder = 0;
            }

            var chunksToAppend = new List<RichTextDocumentChunk>();
            foreach (var appendedChunk in appendedChunks ?? Array.Empty<RichTextDocumentChunk>())
            {
                chunksToAppend.Add(CloneChunkForSave(appendedChunk, entity.Id, nextSortOrder++));
            }

            if (chunksToAppend.Count == 0 && nextSortOrder == 0)
            {
                chunksToAppend.Add(new RichTextDocumentChunk
                {
                    BusinessEntityId = entity.Id,
                    SortOrder = 0,
                    Blocks = new List<RichTextBlock>()
                });
            }

            foreach (var chunk in chunksToAppend.OrderBy(x => x.SortOrder))
            {
                var dto = MapChunkRuntimeToDto(entity.Id, chunk, chunk.SortOrder);
                dto.Version = targetDocumentVersion;
                var savedDto = await _businessEntityDataChunkRepository.AddAsync(dto, ct);
                var tableOfContentsProperty = BuildTableOfContentsProperty(savedDto, chunk.Blocks);
                if (tableOfContentsProperty != null)
                {
                    await _businessEntityDataChunkPropertyRepository.AddAsync(tableOfContentsProperty, ct);
                }
            }

            entity.EntityType = BusinessEntityTypeEnum.RichTextDocument;
            entity.BusinessEntityType = BusinessEntityTypeEnum.Document;
            entity.LastModifiedDate = DateTime.UtcNow;
            manifest.Id = entity.Id;
            manifest.Name = entity.Name;
            manifest.EntityType = BusinessEntityTypeEnum.RichTextDocument;
            manifest.Version = targetDocumentVersion;
            manifest.LastModifiedDate = entity.LastModifiedDate;
            if (manifest.CreatedDate == default)
            {
                manifest.CreatedDate = entity.CreatedDate;
            }

            if (string.IsNullOrWhiteSpace(manifest.Tag))
            {
                manifest.Tag = BusinessEntityTypeEnum.RichTextDocument.ToString();
            }

            await _businessEntityHelper.SaveEntity(entity, manifest);

            await _dataProviderConnector.SaveRichTextEmbeddedFilesAsync(
                entity.Id,
                appendedFiles ?? Array.Empty<RichTextEmbeddedFile>(),
                replaceExistingFiles: false,
                ct);
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

            entity.BusinessEntityType = BusinessEntityTypeEnum.Document;
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
                Version = source.Version <= 0 ? 1 : source.Version
            };
        }

        /// <summary>
        /// Клонирует один rich-text блок, чтобы merged save работал с независимым набором данных.
        /// </summary>
        private static RichTextBlock CloneBlock(RichTextBlock source)
        {
            return new RichTextBlock
            {
                Kind = source.Kind,
                Level = source.Level,
                Html = source.Html,
                ImageId = source.ImageId,
                DisplayVariant = source.DisplayVariant,
                AltText = source.AltText
            };
        }

        private static async Task DeletePropertiesAsync<TProperty>(
            IAsyncRepository<TProperty> repository,
            Guid parentEntityId,
            int propertyType,
            CancellationToken cancellationToken)
            where TProperty : class, IPropertyDto
        {
            var properties = await repository.GetAllAsync(
                p => p.ParentEntityId == parentEntityId && p.PropertyType == propertyType,
                ct: cancellationToken);
            foreach (var property in properties)
            {
                await repository.DeleteAsync(property.Id, cancellationToken);
            }
        }

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

        // Собирает property-строку с оглавлением чанка, если в нём есть heading-блоки H1-H3.
        private static BusinessEntityDataChunkPropertyDto? BuildTableOfContentsProperty(
            BusinessEntityDataChunkDto chunkDto,
            IReadOnlyList<RichTextBlock>? blocks)
        {
            var entries = BuildTableOfContentsEntries(chunkDto, blocks);

            if (entries.Count == 0)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            return new BusinessEntityDataChunkPropertyDto
            {
                Id = Guid.NewGuid(),
                CreatedDate = now,
                LastModifiedDate = now,
                ParentEntityId = chunkDto.Id,
                PropertyType = (int)BusinessEntityDataChunkPropertyTypeEnum.RichDocTableOfContents,
                Data = JsonSerializer.Serialize(
                    new RichTextTableOfContentsPayload
                    {
                        Entries = entries.Select(x => new RichTextTableOfContentsPayloadEntry
                        {
                            ChunkId = x.ChunkId,
                            ChunkSortOrder = x.ChunkSortOrder,
                            BlockIndex = x.BlockIndex,
                            Level = x.Level,
                            Title = x.Title,
                            Anchor = x.Anchor
                        }).ToList()
                    },
                    StorageJsonOptions.Default),
                Metadata = JsonSerializer.Serialize(
                    new RichTextTableOfContentsMetadata
                    {
                        EntryCount = entries.Count
                    },
                    StorageJsonOptions.Default)
            };
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
                var chunkDtos = await GetSelectedChunkWindowDtosAsync(
                    entityId,
                    startSortOrder,
                    take,
                    resolvedDocumentVersion,
                    ct);

                foreach (var chunkDto in chunkDtos)
                {
                    ct.ThrowIfCancellationRequested();

                    var blocks = RichTextChunkStorageSerializer.DeserializeChunkData(chunkDto.Data);
                    entries.AddRange(BuildTableOfContentsEntries(chunkDto, blocks));
                }
            }

            return entries;
        }

        private static IReadOnlyList<RichTextDocumentTableOfContentsEntry> BuildTableOfContentsEntries(
            BusinessEntityDataChunkDto chunkDto,
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

        // Читает entries оглавления из persisted chunk-property JSON.
        private static IReadOnlyList<RichTextDocumentTableOfContentsEntry> ReadTableOfContentsEntries(BusinessEntityDataChunkPropertyDto property)
        {
            if (string.IsNullOrWhiteSpace(property.Data))
            {
                return Array.Empty<RichTextDocumentTableOfContentsEntry>();
            }

            var payload = JsonSerializer.Deserialize<RichTextTableOfContentsPayload>(property.Data, StorageJsonOptions.Default)
                ?? new RichTextTableOfContentsPayload();

            if (payload.SchemaVersion != 1 ||
                !string.Equals(payload.Kind, "RichDocChunkTableOfContents", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported rich-text table of contents payload for property '{property.Id}'.");
            }

            return payload.Entries
                .Select(x => new RichTextDocumentTableOfContentsEntry
                {
                    ChunkId = x.ChunkId,
                    ChunkSortOrder = x.ChunkSortOrder,
                    BlockIndex = x.BlockIndex,
                    Level = x.Level,
                    Title = x.Title ?? string.Empty,
                    Anchor = x.Anchor ?? string.Empty
                })
                .ToList();
        }

        // Преобразует DTO-строку чанка в runtime-объект rich-text чанка.
        private static RichTextDocumentChunk MapChunkDtoToRuntime(BusinessEntityDataChunkDto dto)
        {
            var blocks = RichTextChunkStorageSerializer.DeserializeChunkData(dto.Data);
            return new RichTextDocumentChunk
            {
                Id = dto.Id,
                BusinessEntityId = dto.BusinessEntityId,
                SortOrder = dto.SortOrder,
                Blocks = blocks,
                PlainText = dto.PlainText ?? string.Empty,
                HtmlCache = dto.HtmlCache ?? string.Empty,
                BlockCount = dto.BlockCount,
                CharCount = dto.CharCount,
                DataSizeBytes = dto.DataSizeBytes,
                Version = dto.Version,
                Checksum = dto.Checksum ?? string.Empty
            };
        }

        private static BusinessEntityDataChunkDto MapChunkRuntimeToDto(
            Guid businessEntityId,
            RichTextDocumentChunk chunk,
            long sortOrder)
        {
            var chunkId = chunk.Id == Guid.Empty ? Guid.NewGuid() : chunk.Id;
            var blocks = chunk.Blocks ?? new List<RichTextBlock>();
            var dataJson = RichTextChunkStorageSerializer.SerializeChunkData(blocks);
            var now = DateTime.UtcNow;

            return new BusinessEntityDataChunkDto
            {
                Id = chunkId,
                CreatedDate = now,
                LastModifiedDate = now,
                BusinessEntityId = businessEntityId,
                SortOrder = sortOrder,
                Data = dataJson,
                PlainText = RichTextChunkStorageSerializer.BuildPlainText(blocks),
                HtmlCache = RichTextChunkStorageSerializer.BuildHtmlCache(businessEntityId, chunkId, blocks),
                BlockCount = blocks.Count,
                CharCount = RichTextChunkStorageSerializer.BuildCharCount(blocks),
                DataSizeBytes = DataPayloadEnvelopeSerializer.GetJsonLength(dataJson),
                Version = chunk.Version <= 0 ? 1 : chunk.Version,
                Checksum = RichTextChunkStorageSerializer.BuildChecksum(dataJson)
            };
        }

        // Возвращает последнюю версию BusinessEntityData; без manifest-записи не ограничивает технические chunks.
        private async Task<int> GetLatestDocumentVersionAsync(Guid entityId, CancellationToken ct)
        {
            var versions = await _dataProviderConnector.GetDataVersionsAsync(entityId, ct);
            return versions.Count == 0
                ? int.MaxValue
                : versions.Max(x => x.Version <= 0 ? 1 : x.Version);
        }

        private async Task<int> ResolveDocumentVersionAsync(Guid entityId, int? documentVersion, CancellationToken ct)
        {
            return documentVersion.HasValue && documentVersion.Value > 0
                ? documentVersion.Value
                : await GetLatestDocumentVersionAsync(entityId, ct);
        }

        private async Task<int> GetChunkSortOrderUpperBoundAsync(
            Guid entityId,
            int resolvedDocumentVersion,
            CancellationToken ct)
        {
            var lastRows = await _businessEntityDataChunkRepository.GetPageAsync(
                d => d.BusinessEntityId == entityId &&
                     (d.Version <= 0 || d.Version <= resolvedDocumentVersion),
                d => d.SortOrder,
                descending: true,
                skip: 0,
                take: 1,
                ct: ct);

            var lastSortOrder = lastRows.FirstOrDefault()?.SortOrder;
            if (!lastSortOrder.HasValue)
            {
                return 0;
            }

            if (lastSortOrder.Value >= int.MaxValue - 1L)
            {
                return int.MaxValue;
            }

            return (int)lastSortOrder.Value + 1;
        }

        private async Task<IReadOnlyList<BusinessEntityDataChunkDto>> GetSelectedChunkWindowDtosAsync(
            Guid entityId,
            long startSortOrder,
            int take,
            int resolvedDocumentVersion,
            CancellationToken ct)
        {
            var normalizedTake = Math.Max(take, 0);
            if (normalizedTake <= 0)
            {
                return Array.Empty<BusinessEntityDataChunkDto>();
            }

            var endExclusive = startSortOrder > long.MaxValue - normalizedTake
                ? long.MaxValue
                : startSortOrder + normalizedTake;

            var chunkRows = await _businessEntityDataChunkRepository.GetPageAsync(
                d => d.BusinessEntityId == entityId &&
                     (d.Version <= 0 || d.Version <= resolvedDocumentVersion) &&
                     d.SortOrder >= startSortOrder &&
                     d.SortOrder < endExclusive,
                d => d.SortOrder,
                descending: false,
                skip: 0,
                take: null,
                ct: ct);

            return SelectChunkVersions(chunkRows, resolvedDocumentVersion)
                .Where(d => d.SortOrder >= startSortOrder && d.SortOrder < endExclusive)
                .OrderBy(d => d.SortOrder)
                .ToList();
        }

        // Выбирает по одному chunk DTO на каждый chunk Id в рамках версии документа.
        private static IReadOnlyList<BusinessEntityDataChunkDto> SelectChunkVersions(IEnumerable<BusinessEntityDataChunkDto> chunkDtos, int documentVersion)
        {
            return chunkDtos
                .Where(d => (d.Version <= 0 ? 1 : d.Version) <= documentVersion)
                .GroupBy(d => d.Id)
                .Select(group => group
                    .OrderByDescending(d => d.Version <= 0 ? 1 : d.Version)
                    .ThenByDescending(d => d.LastModifiedDate)
                    .First())
                .OrderBy(d => d.SortOrder)
                .ToList();
        }

        private static bool IsSameChunkData(BusinessEntityDataChunkDto chunkDto, string dataJson, string checksum)
        {
            if (!string.IsNullOrWhiteSpace(chunkDto.Checksum) &&
                string.Equals(chunkDto.Checksum, checksum, StringComparison.Ordinal))
            {
                return true;
            }

            return string.IsNullOrWhiteSpace(chunkDto.Checksum) &&
                   string.Equals(chunkDto.Data, dataJson, StringComparison.Ordinal);
        }

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

        private sealed class RichTextTableOfContentsPayload
        {
            [JsonPropertyName("schemaVersion")]
            public int SchemaVersion { get; set; } = 1;

            [JsonPropertyName("kind")]
            public string Kind { get; set; } = "RichDocChunkTableOfContents";

            [JsonPropertyName("entries")]
            public List<RichTextTableOfContentsPayloadEntry> Entries { get; set; } = new();
        }

        private sealed class RichTextTableOfContentsPayloadEntry
        {
            [JsonPropertyName("chunkId")]
            public Guid ChunkId { get; set; }

            [JsonPropertyName("chunkSortOrder")]
            public long ChunkSortOrder { get; set; }

            [JsonPropertyName("blockIndex")]
            public int BlockIndex { get; set; }

            [JsonPropertyName("level")]
            public int Level { get; set; }

            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("anchor")]
            public string? Anchor { get; set; }
        }

        private sealed class RichTextTableOfContentsMetadata
        {
            [JsonPropertyName("schemaVersion")]
            public int SchemaVersion { get; set; } = 1;

            [JsonPropertyName("kind")]
            public string Kind { get; set; } = "RichDocChunkTableOfContentsMetadata";

            [JsonPropertyName("entryCount")]
            public int EntryCount { get; set; }
        }
    }
}

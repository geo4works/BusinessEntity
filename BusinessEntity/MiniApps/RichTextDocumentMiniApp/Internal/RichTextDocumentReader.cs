using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.BaseClasses.Relations;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;

using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport;
using BusinessEntity.WebLogger.Services;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// RichTextDocumentReader: выделенный сценарий владельца статьи без общего смешанного helper.
public sealed class RichTextDocumentReader
{
        private readonly BusinessEntityHelper _businessEntityHelper;
        private readonly IDataProviderConnector _dataProviderConnector;
        private readonly IWebLoggerService? _webLogger;
        // Подключает только зависимости своего предметного сценария.
        public RichTextDocumentReader(BusinessEntityHelper businessEntityHelper, IDataProviderConnector dataProviderConnector, IWebLoggerService? webLogger)
        { _businessEntityHelper = businessEntityHelper; _dataProviderConnector = dataProviderConnector; _webLogger = webLogger; }
        // Читает выбранный снимок и окно содержимого.
        public async Task<RichTextDocumentShell?> GetRichTextDocumentShellAsync(Guid entityId, CancellationToken ct = default)
        {
            var entity = await _businessEntityHelper.GetBusinessEntityById(entityId);
            if (entity == null || entity.EntityType != global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id)
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

        // Читает выбранный снимок и окно содержимого.
        public Task<int> GetChunkCountAsync(Guid entityId, CancellationToken ct = default)
        {
            return GetChunkCountAsync(entityId, documentVersion: null, ct);
        }

        // Читает выбранный снимок и окно содержимого.
        public async Task<int> GetChunkCountAsync(Guid entityId, int? documentVersion, CancellationToken ct = default)
        {
            var resolvedDocumentVersion = await ResolveDocumentVersionAsync(entityId, documentVersion, ct);
            return await GetChunkSortOrderUpperBoundAsync(entityId, resolvedDocumentVersion, ct);
        }

        // Читает выбранный снимок и окно содержимого.
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

        // Читает выбранный снимок и окно содержимого.
        public Task<RichTextDocumentChunkWindow> GetChunkWindowAsync(
            Guid entityId,
            long startSortOrder,
            int take,
            CancellationToken ct = default)
        {
            return GetChunkWindowAsync(entityId, startSortOrder, take, documentVersion: null, ct);
        }

        // Читает выбранный снимок и окно содержимого.
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

        // Читает выбранный снимок и окно содержимого.
        public Task<RichTextDocumentChunkWindow> GetChunkWindowAroundAsync(
            Guid entityId,
            long centerSortOrder,
            int before,
            int after,
            CancellationToken ct = default)
        {
            return GetChunkWindowAroundAsync(entityId, centerSortOrder, before, after, documentVersion: null, ct);
        }

        // Читает выбранный снимок и окно содержимого.
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

        // Читает выбранный снимок и окно содержимого.
        private async Task LogChunkWindowReadAsync(
            Guid entityId,
            long normalizedStart,
            int requestedTake,
            int totalCount,
            IReadOnlyList<BusinessEntityDataChunk> chunkDtos)
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

        // Читает выбранный снимок и окно содержимого.
        internal async Task<int> GetLatestDocumentVersionAsync(Guid entityId, CancellationToken ct)
        {
            var versions = await _dataProviderConnector.GetDataVersionsAsync(entityId, ct);
            if (versions.Count == 0 || versions.Any(v => v.Version < 1)) throw new InvalidDataException("История статьи отсутствует или повреждена.");
            return versions.Max(v => v.Version);
        }

        // Читает выбранный снимок и окно содержимого.
        internal async Task<int> ResolveDocumentVersionAsync(Guid entityId, int? documentVersion, CancellationToken ct)
        {
            return documentVersion.HasValue && documentVersion.Value > 0
                ? documentVersion.Value
                : await GetLatestDocumentVersionAsync(entityId, ct);
        }

        // Читает выбранный снимок и окно содержимого.
        internal async Task<int> GetChunkSortOrderUpperBoundAsync(Guid entityId, int resolvedDocumentVersion, CancellationToken ct)
            => (await _dataProviderConnector.GetVersionChunkWindowAsync(entityId, resolvedDocumentVersion, 0, 0, cancellationToken: ct)).TotalChunkCount;

        // Читает выбранный снимок и окно содержимого.
        internal async Task<IReadOnlyList<BusinessEntityDataChunk>> GetSelectedChunkWindowAsync(Guid entityId,
            long startSortOrder, int take, int resolvedDocumentVersion, CancellationToken ct)
            => (await _dataProviderConnector.GetVersionChunkWindowAsync(entityId, resolvedDocumentVersion, startSortOrder,
                Math.Max(take, 0), cancellationToken: ct)).Chunks;
}

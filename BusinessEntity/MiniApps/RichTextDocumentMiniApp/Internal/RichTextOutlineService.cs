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

// RichTextOutlineService: выделенный сценарий владельца статьи без общего смешанного helper.
public sealed class RichTextOutlineService
{
        private readonly RichTextDocumentReader _reader;
        // Подключает только зависимости своего предметного сценария.
        public RichTextOutlineService(RichTextDocumentReader reader)
        { _reader = reader; }
        // Строит оглавление выбранной версии.
        public Task<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsAsync(
            Guid entityId,
            CancellationToken ct = default)
        {
            return GetTableOfContentsAsync(entityId, documentVersion: null, ct);
        }

        // Строит оглавление выбранной версии.
        public async Task<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsAsync(
            Guid entityId,
            int? documentVersion,
            CancellationToken ct = default)
        {
            var flatEntries = await GetTableOfContentsEntriesFromChunksAsync(entityId, documentVersion, maxChunkCount: null, ct);
            return BuildTableOfContentsTree(flatEntries);
        }

        // Строит оглавление выбранной версии.
        public IAsyncEnumerable<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsBatchesAsync(
            Guid entityId,
            int chunkBatchSize = 5,
            CancellationToken ct = default)
        {
            return GetTableOfContentsBatchesAsync(entityId, chunkBatchSize, documentVersion: null, ct);
        }

        // Строит оглавление выбранной версии.
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

        // Строит оглавление выбранной версии.
        public async IAsyncEnumerable<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsBatchesAsync(
            Guid entityId,
            int chunkBatchSize,
            int? documentVersion,
            int? maxChunkCount,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var normalizedBatchSize = Math.Max(chunkBatchSize, 1);
            var resolvedDocumentVersion = await _reader.ResolveDocumentVersionAsync(entityId, documentVersion, ct);
            var totalChunkCount = await _reader.GetChunkSortOrderUpperBoundAsync(entityId, resolvedDocumentVersion, ct);
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
                var chunkDtos = await _reader.GetSelectedChunkWindowAsync(
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

        // Строит оглавление выбранной версии.
        private async Task<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetTableOfContentsEntriesFromChunksAsync(
            Guid entityId,
            int? documentVersion,
            int? maxChunkCount,
            CancellationToken ct)
        {
            var resolvedDocumentVersion = await _reader.ResolveDocumentVersionAsync(entityId, documentVersion, ct);
            var totalChunkCount = await _reader.GetChunkSortOrderUpperBoundAsync(entityId, resolvedDocumentVersion, ct);
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
                var chunkDtos = await _reader.GetSelectedChunkWindowAsync(
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

        // Строит оглавление выбранной версии.
        private static IReadOnlyList<RichTextDocumentTableOfContentsEntry> BuildTableOfContentsEntries(
            BusinessEntityDataChunk chunkDto,
            IReadOnlyList<BusinessEntityDataBlock>? blocks)
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

        // Строит оглавление выбранной версии.
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

        // Строит оглавление выбранной версии.
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
}

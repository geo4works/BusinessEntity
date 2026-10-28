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

// RichTextSearchService: выделенный сценарий владельца статьи без общего смешанного helper.
public sealed class RichTextSearchService
{
        private readonly RichTextDocumentReader _reader;
        // Подключает только зависимости своего предметного сценария.
        public RichTextSearchService(RichTextDocumentReader reader)
        { _reader = reader; }
        // Ищет в содержимом выбранного снимка.
        public Task<RichTextDocumentSearchResult?> FindTextAsync(
            Guid entityId,
            string? query,
            RichTextDocumentViewportPosition? origin,
            bool searchDown,
            CancellationToken ct = default)
        {
            return FindTextAsync(entityId, query, origin, searchDown, documentVersion: null, ct);
        }

        // Ищет в содержимом выбранного снимка.
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

            var totalChunkCount = await _reader.GetChunkCountAsync(entityId, documentVersion, ct);
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

        // Ищет в содержимом выбранного снимка.
        private async Task<RichTextDocumentSearchResult?> FindInChunkAsync(
            Guid entityId,
            long sortOrder,
            string query,
            bool searchDown,
            int startBlockIndex,
            int? documentVersion,
            CancellationToken ct)
        {
            var window = await _reader.GetChunkWindowAsync(entityId, sortOrder, 1, documentVersion, ct);
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

        // Ищет в содержимом выбранного снимка.
        private static IEnumerable<int> BuildForwardBlockIndexes(int startBlockIndex, int blockCount)
        {
            if (blockCount <= 0 || startBlockIndex >= blockCount)
            {
                return Array.Empty<int>();
            }

            var start = Math.Max(startBlockIndex, 0);
            return Enumerable.Range(start, blockCount - start);
        }

        // Ищет в содержимом выбранного снимка.
        private static IEnumerable<int> BuildBackwardBlockIndexes(int startBlockIndex, int blockCount)
        {
            if (blockCount <= 0 || startBlockIndex < 0)
            {
                return Array.Empty<int>();
            }

            var start = Math.Min(startBlockIndex, blockCount - 1);
            return Enumerable.Range(0, start + 1).Reverse();
        }

        // Ищет в содержимом выбранного снимка.
        private static string BuildBlockSearchText(BusinessEntityDataBlock block)
        {
            return block.Kind switch
            {
                "heading" or "paragraph" or "code" or "list" => RichTextChunkStorageSerializer.BuildInlineText(block.Html),
                "image" => block.AltText ?? string.Empty,
                _ => string.Empty
            };
        }

        // Ищет в содержимом выбранного снимка.
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
}

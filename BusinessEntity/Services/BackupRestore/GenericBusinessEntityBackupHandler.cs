using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.RichText;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal;
using HtmlAgilityPack;

namespace BusinessEntity.Services.BackupRestore;

// Базовый backup-handler для entity, которым пока не нужен специализированный формат.
public sealed class GenericBusinessEntityBackupHandler : IBusinessEntityBackupHandler
{
    private const string EmbeddedFileMetadataName = "metadata.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public bool CanHandle(BusinessEntityDto entity) => true;

    public async Task WriteBackupAsync(BusinessEntityBackupWriteContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Entity);

        Directory.CreateDirectory(context.EntityFolderPath);

        await WriteJsonAsync(
            Path.Combine(context.EntityFolderPath, "entity.json"),
            new
            {
                SchemaVersion = 1,
                Kind = "BusinessEntity",
                context.Entity.Id,
                EntityType = ResolveEntityType(context.Entity).ToString(),
                BusinessEntityType = context.Entity.BusinessEntityType.ToString(),
                context.Entity.Name,
                context.Entity.CreatedDate,
                context.Entity.LastModifiedDate
            },
            ct);

        await WriteJsonAsync(
            Path.Combine(context.EntityFolderPath, "entity-properties.json"),
            new
            {
                SchemaVersion = 1,
                Kind = "BusinessEntityProperties",
                ParentEntityId = context.Entity.Id,
                Items = context.EntityProperties.Select(ToPropertyView).ToList()
            },
            ct);

        await WriteDataAsync(context, ct);
        await WriteHumanReadableAsync(context, ct);
        CopyEntityFiles(context);

        await WriteJsonAsync(
            Path.Combine(context.EntityFolderPath, "backup-metadata.json"),
            new
            {
                SchemaVersion = 1,
                Kind = "BusinessEntityBackupMetadata",
                EntityId = context.Entity.Id,
                EntityType = ResolveEntityType(context.Entity).ToString(),
                LastBackedUpUtc = DateTime.UtcNow,
                context.EntityWatermarkUtc
            },
            ct);
    }

    private static async Task WriteDataAsync(BusinessEntityBackupWriteContext context, CancellationToken ct)
    {
        var dataFolder = Path.Combine(context.EntityFolderPath, "data");
        var chunksFolder = Path.Combine(dataFolder, "chunks");
        Directory.CreateDirectory(dataFolder);
        Directory.CreateDirectory(chunksFolder);

        var dataIndex = new List<object>();
        foreach (var data in context.DataItems.OrderBy(x => x.Version).ThenBy(x => x.Id))
        {
            var dataFileName = $"business-entity-data--{data.Id:D}--v{data.Version}.json";
            dataIndex.Add(new
            {
                DataId = data.Id,
                data.Version,
                File = dataFileName
            });

            await WriteJsonAsync(
                Path.Combine(dataFolder, dataFileName),
                new
                {
                    SchemaVersion = 1,
                    Kind = "BusinessEntityData",
                    data.Id,
                    data.BusinessEntityId,
                    data.Version,
                    data.CreatedDate,
                    data.LastModifiedDate,
                    Data = JsonOrString(data.Data)
                },
                ct);

            var dataProperties = context.DataProperties
                .Where(x => x.ParentEntityId == data.Id)
                .OrderBy(x => x.PropertyType)
                .ThenBy(x => x.Id)
                .Select(ToPropertyView)
                .ToList();

            if (dataProperties.Count > 0)
            {
                await WriteJsonAsync(
                    Path.Combine(dataFolder, $"data-properties--{data.Id:D}--v{data.Version}.json"),
                    new
                    {
                        SchemaVersion = 1,
                        Kind = "BusinessEntityDataProperties",
                        ParentDataId = data.Id,
                        data.Version,
                        Items = dataProperties
                    },
                    ct);
            }
        }

        await WriteJsonAsync(
            Path.Combine(dataFolder, "data-manifest.json"),
            new
            {
                SchemaVersion = 1,
                Kind = "BusinessEntityDataManifest",
                BusinessEntityId = context.Entity.Id,
                Items = dataIndex
            },
            ct);

        if (context.ReadChunksPageAsync != null)
        {
            await WritePagedChunksAsync(context, chunksFolder, ct);
            return;
        }

        foreach (var chunk in context.Chunks.OrderBy(x => x.SortOrder).ThenBy(x => x.Version).ThenBy(x => x.Id))
        {
            await WriteChunkAsync(chunk, chunksFolder, ct);

            var chunkProperties = context.ChunkProperties
                .Where(x => x.ParentEntityId == chunk.Id)
                .OrderBy(x => x.PropertyType)
                .ThenBy(x => x.Id)
                .Select(ToPropertyView)
                .ToList();

            if (chunkProperties.Count > 0)
            {
                await WriteJsonAsync(
                    Path.Combine(chunksFolder, $"chunk-properties--{chunk.Id:D}--v{chunk.Version}.json"),
                    new
                    {
                        SchemaVersion = 1,
                        Kind = "BusinessEntityDataChunkProperties",
                        ParentChunkId = chunk.Id,
                        chunk.Version,
                        Items = chunkProperties
                    },
                    ct);
            }
        }
    }

    private static async Task WritePagedChunksAsync(BusinessEntityBackupWriteContext context, string chunksFolder, CancellationToken ct)
    {
        var skip = 0;
        var take = context.ChunkPageSize <= 0
            ? BusinessEntityBackupWriteContext.DefaultChunkPageSize
            : context.ChunkPageSize;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var page = await context.ReadChunksPageAsync!(skip, take, ct);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var chunk in page)
            {
                await WriteChunkAsync(chunk, chunksFolder, ct);
            }

            if (context.ReadChunkPropertiesAsync != null)
            {
                var chunkIds = page.Select(x => x.Id).Distinct().ToList();
                var chunkProperties = await context.ReadChunkPropertiesAsync(chunkIds, ct);
                foreach (var group in chunkProperties
                             .GroupBy(x => x.ParentEntityId)
                             .OrderBy(x => x.Key))
                {
                    var firstChunk = page.FirstOrDefault(x => x.Id == group.Key);
                    await WriteJsonAsync(
                        Path.Combine(chunksFolder, $"chunk-properties--{group.Key:D}--v{firstChunk?.Version ?? 1}.json"),
                        new
                        {
                            SchemaVersion = 1,
                            Kind = "BusinessEntityDataChunkProperties",
                            ParentChunkId = group.Key,
                            Version = firstChunk?.Version ?? 1,
                            Items = group
                                .OrderBy(x => x.PropertyType)
                                .ThenBy(x => x.Id)
                                .Select(ToPropertyView)
                                .ToList()
                        },
                        ct);
                }
            }

            if (page.Count < take)
            {
                break;
            }

            skip += page.Count;
        }
    }

    private static async Task WriteChunkAsync(BusinessEntityDataChunkDto chunk, string chunksFolder, CancellationToken ct)
    {
        var sortOrder = chunk.SortOrder.ToString("D10");
        var chunkFileName = $"chunk--{sortOrder}--{chunk.Id:D}--v{chunk.Version}.json";

        await WriteJsonAsync(
            Path.Combine(chunksFolder, chunkFileName),
            new
            {
                SchemaVersion = 1,
                Kind = "BusinessEntityDataChunk",
                chunk.Id,
                chunk.BusinessEntityId,
                chunk.SortOrder,
                chunk.Version,
                chunk.CreatedDate,
                chunk.LastModifiedDate,
                chunk.PlainText,
                chunk.HtmlCache,
                chunk.BlockCount,
                chunk.CharCount,
                chunk.DataSizeBytes,
                chunk.Checksum,
                Data = JsonOrString(chunk.Data)
            },
            ct);
    }

    private static async Task WriteHumanReadableAsync(BusinessEntityBackupWriteContext context, CancellationToken ct)
    {
        switch (ResolveEntityType(context.Entity))
        {
            case BusinessEntityTypeEnum.Document:
                await WriteDocumentHumanReadableAsync(context, ct);
                break;
            case BusinessEntityTypeEnum.RichTextDocument:
                await WriteRichDocumentHumanReadableAsync(context, ct);
                break;
        }
    }

    private static async Task WriteDocumentHumanReadableAsync(BusinessEntityBackupWriteContext context, CancellationToken ct)
    {
        var latestData = context.DataItems
            .OrderByDescending(x => NormalizeVersion(x.Version))
            .ThenByDescending(x => x.LastModifiedDate)
            .FirstOrDefault();
        if (latestData == null)
        {
            return;
        }

        var text = ExtractDocumentText(latestData.Data);
        var fileName = $"{ResolveHumanReadableName(context)}--human-readable.md";
        await File.WriteAllTextAsync(Path.Combine(context.EntityFolderPath, fileName), text, ct);
    }

    private static async Task WriteRichDocumentHumanReadableAsync(BusinessEntityBackupWriteContext context, CancellationToken ct)
    {
        var selectedChunks = await ReadCurrentRichDocumentChunksAsync(context, ct);
        var attachments = ExportReadableAttachments(context);
        var html = BuildRichDocumentHtml(context, selectedChunks, attachments);
        var fileName = $"{ResolveHumanReadableName(context)}--human-readable.html";
        await File.WriteAllTextAsync(Path.Combine(context.EntityFolderPath, fileName), html, ct);
    }

    private static async Task<IReadOnlyList<BusinessEntityDataChunkDto>> ReadCurrentRichDocumentChunksAsync(
        BusinessEntityBackupWriteContext context,
        CancellationToken ct)
    {
        var documentVersion = ResolveLatestDocumentVersion(context);
        var selectedChunks = new Dictionary<long, BusinessEntityDataChunkDto>();

        if (context.ReadChunksPageAsync != null)
        {
            var skip = 0;
            var take = context.ChunkPageSize <= 0
                ? BusinessEntityBackupWriteContext.DefaultChunkPageSize
                : context.ChunkPageSize;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var page = await context.ReadChunksPageAsync(skip, take, ct);
                if (page.Count == 0)
                {
                    break;
                }

                SelectCurrentChunkVersions(page, documentVersion, selectedChunks);
                if (page.Count < take)
                {
                    break;
                }

                skip += page.Count;
            }
        }
        else
        {
            SelectCurrentChunkVersions(context.Chunks, documentVersion, selectedChunks);
        }

        return selectedChunks.Values
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToList();
    }

    private static void SelectCurrentChunkVersions(
        IEnumerable<BusinessEntityDataChunkDto> chunks,
        int documentVersion,
        Dictionary<long, BusinessEntityDataChunkDto> selectedChunks)
    {
        foreach (var chunk in chunks)
        {
            if (NormalizeVersion(chunk.Version) > documentVersion)
            {
                continue;
            }

            if (!selectedChunks.TryGetValue(chunk.SortOrder, out var current)
                || NormalizeVersion(chunk.Version) > NormalizeVersion(current.Version)
                || (NormalizeVersion(chunk.Version) == NormalizeVersion(current.Version)
                    && chunk.LastModifiedDate > current.LastModifiedDate))
            {
                selectedChunks[chunk.SortOrder] = chunk;
            }
        }
    }

    private static int ResolveLatestDocumentVersion(BusinessEntityBackupWriteContext context)
    {
        if (context.DataItems.Count > 0)
        {
            return context.DataItems.Max(x => NormalizeVersion(x.Version));
        }

        return int.MaxValue;
    }

    private static string BuildRichDocumentHtml(
        BusinessEntityBackupWriteContext context,
        IReadOnlyList<BusinessEntityDataChunkDto> chunks,
        IReadOnlyDictionary<string, string> attachments)
    {
        var title = context.Entity.Name ?? string.Empty;
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"ru\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine($"  <title>{WebUtility.HtmlEncode(title)}</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { font-family: Arial, sans-serif; line-height: 1.55; margin: 32px auto; max-width: 980px; padding: 0 24px; color: #102033; }");
        builder.AppendLine("    h1, h2, h3, h4, h5, h6 { line-height: 1.2; margin: 1.2em 0 0.45em; }");
        builder.AppendLine("    p { margin: 0.75em 0; }");
        builder.AppendLine("    table { border-collapse: collapse; margin: 1em 0; table-layout: fixed; width: 100%; }");
        builder.AppendLine("    td, th { border: 1px solid #b9c6d5; padding: 0.35rem 0.45rem; vertical-align: top; }");
        builder.AppendLine("    th { background: #f1effa; font-weight: 700; }");
        builder.AppendLine("    td[data-rich-table-row-number='true'], th[data-rich-table-row-number='true'] { background: #f8f6ff; color: #536075; font-weight: 700; text-align: center; width: 3.25rem; }");
        builder.AppendLine("    img { max-width: 100%; height: auto; cursor: zoom-in; }");
        builder.AppendLine("    .rich-text-image { margin: 1em 0; }");
        builder.AppendLine("    .rich-text-inline-image { display: inline-block; vertical-align: top; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"  <h1>{WebUtility.HtmlEncode(title)}</h1>");

        foreach (var chunk in chunks.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            builder.Append(BuildRichChunkHtml(chunk, attachments));
        }

        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static string BuildRichChunkHtml(
        BusinessEntityDataChunkDto chunk,
        IReadOnlyDictionary<string, string> attachments)
    {
        try
        {
            var blocks = RichTextChunkStorageSerializer.DeserializeChunkData(chunk.Data);
            if (blocks.Count > 0)
            {
                return BuildRichBlocksHtml(blocks, attachments);
            }
        }
        catch
        {
            // Human-readable backup should not block the canonical JSON backup.
        }

        return string.IsNullOrWhiteSpace(chunk.PlainText)
            ? string.Empty
            : $"<p>{WebUtility.HtmlEncode(chunk.PlainText).Replace(Environment.NewLine, "<br />")}</p>{Environment.NewLine}";
    }

    private static string BuildRichBlocksHtml(
        IReadOnlyList<RichTextBlock> blocks,
        IReadOnlyDictionary<string, string> attachments)
    {
        var builder = new StringBuilder();
        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case "heading":
                    var level = Math.Clamp(block.Level <= 0 ? 1 : block.Level, 1, 6);
                    builder.Append("<h").Append(level).Append('>');
                    builder.Append(BuildInlineHtmlForExport(block.Html, attachments));
                    builder.Append("</h").Append(level).AppendLine(">");
                    break;

                case "image":
                    builder.AppendLine(BuildImageHtml(block, attachments, "p"));
                    break;

                case "table":
                    builder.AppendLine(BuildTableHtmlForExport(block.Html, attachments));
                    break;

                case "paragraph":
                default:
                    builder.Append("<p>");
                    builder.Append(BuildInlineHtmlForExport(block.Html, attachments));
                    builder.AppendLine("</p>");
                    break;
            }
        }

        return builder.ToString();
    }

    private static string BuildTableHtmlForExport(
        string? html,
        IReadOnlyDictionary<string, string> attachments)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = new HtmlDocument
        {
            OptionFixNestedTags = true
        };
        document.LoadHtml($"<root>{html}</root>");

        var root = document.DocumentNode.SelectSingleNode("//root") ?? document.DocumentNode;
        var builder = new StringBuilder();
        foreach (var child in root.ChildNodes)
        {
            AppendTableHtmlForExport(child, attachments, builder);
        }

        return builder.ToString();
    }

    private static void AppendTableHtmlForExport(
        HtmlNode node,
        IReadOnlyDictionary<string, string> attachments,
        StringBuilder builder)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            builder.Append(WebUtility.HtmlEncode(HtmlEntity.DeEntitize(node.InnerText ?? string.Empty)));
            return;
        }

        if (node.NodeType != HtmlNodeType.Element)
        {
            return;
        }

        if (TryReadInlineImage(node, out var image))
        {
            builder.Append(BuildInlineImageHtml(image, attachments));
            return;
        }

        var nodeName = node.Name.ToLowerInvariant();
        if (nodeName is "table" or "thead" or "tbody" or "tfoot" or "tr" or "td" or "th")
        {
            AppendTableTagStart(builder, node);
            foreach (var child in node.ChildNodes)
            {
                AppendTableHtmlForExport(child, attachments, builder);
            }

            builder.Append("</").Append(nodeName).Append('>');
            return;
        }

        if (nodeName == "br")
        {
            builder.Append("<br />");
            return;
        }

        if (nodeName == "p")
        {
            builder.Append("<p>");
            foreach (var child in node.ChildNodes)
            {
                AppendTableHtmlForExport(child, attachments, builder);
            }

            builder.Append("</p>");
            return;
        }

        if (nodeName is "strong" or "b" or "em" or "i" or "u")
        {
            var normalizedTag = nodeName switch
            {
                "b" => "strong",
                "i" => "em",
                _ => nodeName
            };

            builder.Append('<').Append(normalizedTag).Append('>');
            foreach (var child in node.ChildNodes)
            {
                AppendTableHtmlForExport(child, attachments, builder);
            }

            builder.Append("</").Append(normalizedTag).Append('>');
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendTableHtmlForExport(child, attachments, builder);
        }
    }

    private static void AppendTableTagStart(StringBuilder builder, HtmlNode node)
    {
        var nodeName = node.Name.ToLowerInvariant();
        builder.Append('<').Append(nodeName);

        if (nodeName == "table" && IsTruthyAttribute(node, "data-rich-table-row-numbers"))
        {
            builder.Append(" data-rich-table-row-numbers=\"true\"");
        }

        if (nodeName is "td" or "th")
        {
            AppendPositiveIntAttribute(builder, node, "colspan");
            AppendPositiveIntAttribute(builder, node, "rowspan");
            var cellBackground = NormalizeCellBackgroundColor(node.GetAttributeValue("data-rich-cell-background", string.Empty));
            if (!string.IsNullOrWhiteSpace(cellBackground))
            {
                builder.Append(" data-rich-cell-background=\"")
                    .Append(cellBackground)
                    .Append("\" style=\"background-color: ")
                    .Append(cellBackground)
                    .Append('"');
            }

            if (IsTruthyAttribute(node, "data-rich-table-row-number"))
            {
                builder.Append(" data-rich-table-row-number=\"true\"");
            }
        }

        builder.Append('>');
    }

    private static void AppendPositiveIntAttribute(StringBuilder builder, HtmlNode node, string name)
    {
        var value = ReadPositiveInt(node.GetAttributeValue(name, string.Empty));
        if (value > 0)
        {
            builder.Append(' ')
                .Append(name)
                .Append("=\"")
                .Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('"');
        }
    }

    private static bool IsTruthyAttribute(HtmlNode node, string name)
    {
        var value = node.GetAttributeValue(name, string.Empty);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, name, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCellBackgroundColor(string? rawValue)
    {
        var value = (rawValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (value.Length == 4 &&
            value[0] == '#' &&
            IsHex(value[1]) &&
            IsHex(value[2]) &&
            IsHex(value[3]))
        {
            return $"#{char.ToLowerInvariant(value[1])}{char.ToLowerInvariant(value[1])}{char.ToLowerInvariant(value[2])}{char.ToLowerInvariant(value[2])}{char.ToLowerInvariant(value[3])}{char.ToLowerInvariant(value[3])}";
        }

        if (value.Length == 7 &&
            value[0] == '#' &&
            value.Skip(1).All(IsHex))
        {
            return value.ToLowerInvariant();
        }

        return string.Empty;
    }

    private static bool IsHex(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static string BuildImageHtml(
        RichTextBlock block,
        IReadOnlyDictionary<string, string> attachments,
        string wrapperTag)
    {
        var imageId = (block.ImageId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(imageId))
        {
            return string.Empty;
        }

        var variant = string.IsNullOrWhiteSpace(block.DisplayVariant)
            ? "original"
            : block.DisplayVariant.Trim();
        var relativePath = ResolveAttachmentPath(attachments, imageId, variant);
        var encodedAlt = WebUtility.HtmlEncode(block.AltText ?? string.Empty);
        var attributes = new StringBuilder();
        attributes.Append($" src=\"{WebUtility.HtmlEncode(relativePath)}\"");
        attributes.Append($" alt=\"{encodedAlt}\"");
        if (block.Width > 0)
        {
            attributes.Append($" width=\"{block.Width}\"");
        }

        if (block.Height > 0)
        {
            attributes.Append($" height=\"{block.Height}\"");
        }

        return $"<{wrapperTag} class=\"rich-text-image\"><img{attributes} /></{wrapperTag}>";
    }

    private static string BuildInlineHtmlForExport(
        string? html,
        IReadOnlyDictionary<string, string> attachments)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = new HtmlDocument
        {
            OptionFixNestedTags = true
        };
        document.LoadHtml($"<root>{html}</root>");

        var root = document.DocumentNode.SelectSingleNode("//root") ?? document.DocumentNode;
        var builder = new StringBuilder();
        foreach (var child in root.ChildNodes)
        {
            AppendInlineHtmlForExport(child, attachments, builder);
        }

        return builder.ToString();
    }

    private static void AppendInlineHtmlForExport(
        HtmlNode node,
        IReadOnlyDictionary<string, string> attachments,
        StringBuilder builder)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            builder.Append(WebUtility.HtmlEncode(HtmlEntity.DeEntitize(node.InnerText ?? string.Empty)));
            return;
        }

        if (node.NodeType != HtmlNodeType.Element)
        {
            return;
        }

        if (TryReadInlineImage(node, out var image))
        {
            builder.Append(BuildInlineImageHtml(image, attachments));
            return;
        }

        var nodeName = node.Name.ToLowerInvariant();
        if (nodeName == "br")
        {
            builder.Append("<br />");
            return;
        }

        if (nodeName is "strong" or "b" or "em" or "i" or "u")
        {
            var normalizedTag = nodeName switch
            {
                "b" => "strong",
                "i" => "em",
                _ => nodeName
            };

            builder.Append('<').Append(normalizedTag).Append('>');
            foreach (var child in node.ChildNodes)
            {
                AppendInlineHtmlForExport(child, attachments, builder);
            }
            builder.Append("</").Append(normalizedTag).Append('>');
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendInlineHtmlForExport(child, attachments, builder);
        }
    }

    private static string BuildInlineImageHtml(
        InlineImageDescriptor image,
        IReadOnlyDictionary<string, string> attachments)
    {
        var imageId = (image.ImageId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(imageId))
        {
            return string.Empty;
        }

        var variant = string.IsNullOrWhiteSpace(image.DisplayVariant)
            ? "original"
            : image.DisplayVariant.Trim();
        var relativePath = ResolveAttachmentPath(attachments, imageId, variant);
        var encodedAlt = WebUtility.HtmlEncode(image.AltText ?? string.Empty);
        var attributes = new StringBuilder();
        attributes.Append($" src=\"{WebUtility.HtmlEncode(relativePath)}\"");
        attributes.Append($" alt=\"{encodedAlt}\"");
        attributes.Append($" data-rich-image-id=\"{WebUtility.HtmlEncode(imageId)}\"");
        attributes.Append($" data-display-variant=\"{WebUtility.HtmlEncode(variant)}\"");
        if (image.Width > 0)
        {
            attributes.Append($" width=\"{image.Width}\"");
        }

        if (image.Height > 0)
        {
            attributes.Append($" height=\"{image.Height}\"");
        }

        return $"<span class=\"rich-text-inline-image\"><img{attributes} /></span>";
    }

    private static bool TryReadInlineImage(HtmlNode node, out InlineImageDescriptor image)
    {
        image = new InlineImageDescriptor();
        if (node.NodeType != HtmlNodeType.Element)
        {
            return false;
        }

        var nodeName = node.Name.ToLowerInvariant();
        if (nodeName != "img" &&
            !(nodeName == "span" && HasCssClass(node, "rich-text-inline-image")))
        {
            return false;
        }

        var imageNode = nodeName == "img"
            ? node
            : node.Descendants("img").FirstOrDefault();

        var parsedVariant = "original";
        var imageId = ReadAttribute(node, "data-rich-image-id");
        if (string.IsNullOrWhiteSpace(imageId) && imageNode != null)
        {
            imageId = ReadAttribute(imageNode, "data-rich-image-id");
        }

        if (string.IsNullOrWhiteSpace(imageId) && imageNode != null)
        {
            var src = ReadAttribute(imageNode, "src");
            if (!TryParseRichDocumentImageUrl(src, out imageId, out parsedVariant))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(imageId))
        {
            return false;
        }

        var variant = ReadAttribute(node, "data-display-variant");
        if (string.IsNullOrWhiteSpace(variant) && imageNode != null)
        {
            variant = ReadAttribute(imageNode, "data-display-variant");
        }

        image = new InlineImageDescriptor
        {
            ImageId = imageId,
            DisplayVariant = string.IsNullOrWhiteSpace(variant) ? parsedVariant : variant,
            AltText = ReadFirstAttribute(node, imageNode, "data-alt-text", "alt"),
            Width = ReadPositivePixelValue(node, imageNode, "width"),
            Height = ReadPositivePixelValue(node, imageNode, "height")
        };

        return true;
    }

    private static IReadOnlyDictionary<string, string> ExportReadableAttachments(BusinessEntityBackupWriteContext context)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceRoot = Path.Combine(context.StorageRootPath, "business-entities", context.Entity.Id.ToString("D"));
        var sourceImagesRoot = Path.Combine(sourceRoot, "images");
        if (!Directory.Exists(sourceImagesRoot))
        {
            return result;
        }

        var targetImagesRoot = Path.Combine(context.EntityFolderPath, "attachments", "images");
        foreach (var imageDirectory in Directory.EnumerateDirectories(sourceImagesRoot))
        {
            var imageId = Path.GetFileName(imageDirectory);
            if (string.IsNullOrWhiteSpace(imageId))
            {
                continue;
            }

            var metadata = ReadEmbeddedFileMetadata(imageDirectory);
            foreach (var sourceFilePath in Directory.EnumerateFiles(imageDirectory))
            {
                if (string.Equals(Path.GetFileName(sourceFilePath), EmbeddedFileMetadataName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var variant = Path.GetFileNameWithoutExtension(sourceFilePath);
                if (string.IsNullOrWhiteSpace(variant))
                {
                    variant = "original";
                }

                var extension = Path.GetExtension(sourceFilePath);
                if (string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase))
                {
                    extension = ResolveReadableFileExtension(metadata.FileName, metadata.ContentType);
                }

                var targetDirectory = Path.Combine(targetImagesRoot, SanitizePathSegment(imageId));
                Directory.CreateDirectory(targetDirectory);

                var targetFileName = $"{SanitizePathSegment(variant)}{extension.ToLowerInvariant()}";
                var targetFilePath = Path.Combine(targetDirectory, targetFileName);
                File.Copy(sourceFilePath, targetFilePath, overwrite: true);

                result[AttachmentKey(imageId, variant)] = ToRelativeWebPath(context.EntityFolderPath, targetFilePath);
            }
        }

        return result;
    }

    private static EmbeddedFileMetadata ReadEmbeddedFileMetadata(string imageDirectory)
    {
        var metadataPath = Path.Combine(imageDirectory, EmbeddedFileMetadataName);
        if (!File.Exists(metadataPath))
        {
            return new EmbeddedFileMetadata();
        }

        try
        {
            var json = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize<EmbeddedFileMetadata>(json, StorageJsonOptions.Default)
                ?? new EmbeddedFileMetadata();
        }
        catch
        {
            return new EmbeddedFileMetadata();
        }
    }

    private static string ResolveAttachmentPath(
        IReadOnlyDictionary<string, string> attachments,
        string imageId,
        string variant)
    {
        if (attachments.TryGetValue(AttachmentKey(imageId, variant), out var path))
        {
            return path;
        }

        if (!string.Equals(variant, "original", StringComparison.OrdinalIgnoreCase)
            && attachments.TryGetValue(AttachmentKey(imageId, "original"), out var originalPath))
        {
            return originalPath;
        }

        return string.Empty;
    }

    private static string AttachmentKey(string imageId, string variant)
    {
        return $"{imageId}\u001f{variant}";
    }

    private static string ToRelativeWebPath(string rootPath, string filePath)
    {
        return Path.GetRelativePath(rootPath, filePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string ResolveReadableFileExtension(string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (IsReadableFileExtension(extension))
        {
            return extension.ToLowerInvariant();
        }

        return (contentType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => ".dat"
        };
    }

    private static bool IsReadableFileExtension(string? extension)
    {
        return (extension ?? string.Empty).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".svg" => true,
            _ => false
        };
    }

    private static string ReadFirstAttribute(HtmlNode node, HtmlNode? fallbackNode, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadAttribute(node, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (fallbackNode == null)
        {
            return string.Empty;
        }

        foreach (var name in names)
        {
            var value = ReadAttribute(fallbackNode, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ReadAttribute(HtmlNode node, string name)
    {
        return HtmlEntity.DeEntitize(node.GetAttributeValue(name, string.Empty)).Trim();
    }

    private static int ReadPositivePixelValue(HtmlNode node, HtmlNode? fallbackNode, string name)
    {
        var value = ReadPositiveInt(ReadFirstAttribute(node, fallbackNode, $"data-{name}", name));
        if (value > 0)
        {
            return value;
        }

        value = ReadStylePixelValue(node, name);
        if (value > 0)
        {
            return value;
        }

        return fallbackNode == null
            ? 0
            : ReadStylePixelValue(fallbackNode, name);
    }

    private static int ReadStylePixelValue(HtmlNode node, string name)
    {
        var style = ReadAttribute(node, "style");
        if (string.IsNullOrWhiteSpace(style))
        {
            return 0;
        }

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = declaration.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var propertyName = declaration[..separatorIndex].Trim();
            if (!string.Equals(propertyName, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var propertyValue = declaration[(separatorIndex + 1)..].Trim();
            var pixelSuffixIndex = propertyValue.IndexOf("px", StringComparison.OrdinalIgnoreCase);
            if (pixelSuffixIndex >= 0)
            {
                propertyValue = propertyValue[..pixelSuffixIndex];
            }

            return ReadPositiveInt(propertyValue);
        }

        return 0;
    }

    private static int ReadPositiveInt(string? rawValue)
    {
        return int.TryParse(
            rawValue?.Trim(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value) && value > 0
            ? value
            : 0;
    }

    private static bool HasCssClass(HtmlNode node, string className)
    {
        var classAttribute = node.GetAttributeValue("class", string.Empty);
        return classAttribute
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => string.Equals(x, className, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseRichDocumentImageUrl(string? src, out string imageId, out string variant)
    {
        imageId = string.Empty;
        variant = "original";
        if (string.IsNullOrWhiteSpace(src))
        {
            return false;
        }

        const string marker = "/rich-document-files/";
        var markerIndex = src.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var tail = src[(markerIndex + marker.Length)..];
        var queryIndex = tail.IndexOfAny(new[] { '?', '#' });
        if (queryIndex >= 0)
        {
            tail = tail[..queryIndex];
        }

        var parts = tail.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || !string.Equals(parts[1], "images", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        imageId = Uri.UnescapeDataString(parts[2]);
        variant = Uri.UnescapeDataString(parts[3]);
        return !string.IsNullOrWhiteSpace(imageId);
    }

    private static string ExtractDocumentText(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.TryGetProperty("payload", out var payload)
                && payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("text", out var payloadText))
            {
                return payloadText.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("text", out var directText))
            {
                return directText.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            return data;
        }

        return string.Empty;
    }

    private static int NormalizeVersion(int version)
    {
        return version <= 0 ? 1 : version;
    }

    private static string ResolveHumanReadableName(BusinessEntityBackupWriteContext context)
    {
        return SanitizePathSegment(string.IsNullOrWhiteSpace(context.EntityNamePathSegment)
            ? context.Entity.Name
            : context.EntityNamePathSegment);
    }

    private static void CopyEntityFiles(BusinessEntityBackupWriteContext context)
    {
        var source = Path.Combine(context.StorageRootPath, "business-entities", context.Entity.Id.ToString("D"));
        if (!Directory.Exists(source))
        {
            return;
        }

        var target = Path.Combine(context.EntityFolderPath, "files");
        CopyDirectory(source, target);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            var targetPath = Path.Combine(targetDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, targetPath, overwrite: true);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory))
        {
            var targetPath = Path.Combine(targetDirectory, Path.GetFileName(directoryPath));
            CopyDirectory(directoryPath, targetPath);
        }
    }

    private static object ToPropertyView(IPropertyDto property)
    {
        return new
        {
            property.Id,
            property.ParentEntityId,
            property.PropertyType,
            property.CreatedDate,
            property.LastModifiedDate,
            Data = JsonOrString(property.Data),
            property.Metadata
        };
    }

    private static object JsonOrString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private static BusinessEntityTypeEnum ResolveEntityType(BusinessEntityDto entity)
    {
        return entity.EntityType == BusinessEntity.Core.Classes.BusinessEntityTypeEnum.Undefined
            ? entity.BusinessEntityType
            : entity.EntityType;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
            .ToHashSet();
        var chars = (string.IsNullOrWhiteSpace(value) ? "Unnamed" : value)
            .Select(x => invalidChars.Contains(x) || char.IsControl(x) ? '_' : x)
            .ToArray();
        var sanitized = new string(chars).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Unnamed";
        }

        return sanitized.Length <= 120
            ? sanitized
            : sanitized[..120].Trim();
    }

    private sealed class EmbeddedFileMetadata
    {
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public string? StoredFileName { get; set; }
    }

    private sealed class InlineImageDescriptor
    {
        public string ImageId { get; set; } = string.Empty;
        public string DisplayVariant { get; set; } = "original";
        public string AltText { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BusinessEntity.Core.RichText;
using HtmlAgilityPack;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal;

// Общая low-level логика сериализации чанков rich-text документа.
// Здесь нет доступа к репозиториям: только envelope, html-cache и plain-text derivation.
internal static class RichTextChunkStorageSerializer
{
    private const string ChunkStorageKind = "RichTextDocumentChunk";

    // Сериализует набор блоков чанка в versioned envelope.
    public static string SerializeChunkData(IReadOnlyList<RichTextBlock> blocks)
    {
        var payloadJson = JsonSerializer.Serialize(
            new RichTextChunkPayloadBody
            {
                Blocks = blocks?.ToList() ?? new List<RichTextBlock>()
            },
            StorageJsonOptions.Default);

        return DataPayloadEnvelopeSerializer.CreateEnvelopeJson(ChunkStorageKind, payloadJson);
    }

    // Десериализует versioned envelope чанка в набор блоков.
    public static List<RichTextBlock> DeserializeChunkData(string envelopeJson)
    {
        var envelope = DataPayloadEnvelopeSerializer.ReadEnvelope(envelopeJson);
        if (!string.Equals(envelope.Kind, ChunkStorageKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported rich-text chunk kind '{envelope.Kind}'.");
        }

        var body = JsonSerializer.Deserialize<RichTextChunkPayloadBody>(envelope.PayloadJson, StorageJsonOptions.Default)
            ?? new RichTextChunkPayloadBody();

        return body.Blocks ?? new List<RichTextBlock>();
    }

    // Извлекает plain-text содержимое чанка для будущего поиска.
    public static string BuildPlainText(IReadOnlyList<RichTextBlock> blocks)
    {
        if (blocks == null || blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var block in blocks)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            switch (block.Kind)
            {
                case "heading":
                case "paragraph":
                    builder.Append(BuildInlineText(block.Html));
                    break;
                case "code":
                    builder.Append(BuildCodeText(block.Html));
                    break;
                case "list":
                    builder.Append(BuildListPlainText(block.Html));
                    break;
                case "image":
                    if (!string.IsNullOrWhiteSpace(block.AltText))
                    {
                        builder.Append(block.AltText);
                    }
                    break;
                case "video":
                    if (!string.IsNullOrWhiteSpace(block.VideoTitle))
                    {
                        builder.Append(block.VideoTitle);
                    }
                    break;
                case "table":
                    builder.Append(BuildTablePlainText(block.Html));
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    // Собирает готовый readonly HTML чанка.
    public static string BuildHtmlCache(Guid businessEntityId, Guid chunkId, IReadOnlyList<RichTextBlock> blocks)
    {
        if (blocks == null || blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var block = blocks[blockIndex];
            switch (block.Kind)
            {
                case "heading":
                    var level = Math.Clamp(block.Level <= 0 ? 2 : block.Level, 1, 6);
                    var anchorAttributes = level <= 3
                        ? $" id=\"{BuildBlockAnchor(chunkId, blockIndex)}\" data-chunk-id=\"{chunkId:D}\" data-block-index=\"{blockIndex}\""
                        : string.Empty;
                    builder.Append($"<h{level}{anchorAttributes}>{BuildInlineHtmlCache(businessEntityId, block.Html)}</h{level}>");
                    break;
                case "paragraph":
                    builder.Append($"<p>{BuildInlineHtmlCache(businessEntityId, block.Html)}</p>");
                    break;
                case "code":
                    builder.Append($"<pre><code>{BuildCodeHtmlCache(block.Html)}</code></pre>");
                    break;
                case "list":
                    builder.Append(BuildListHtmlCache(businessEntityId, block.Html));
                    break;
                case "image":
                    var encodedAlt = WebUtility.HtmlEncode(block.AltText ?? string.Empty);
                    var imageId = Uri.EscapeDataString(block.ImageId ?? string.Empty);
                    var variant = Uri.EscapeDataString(string.IsNullOrWhiteSpace(block.DisplayVariant) ? "original" : block.DisplayVariant);
                    var imageAttributes = BuildImageAttributes(block, encodedAlt, imageId, variant);
                    builder.Append(
                        $"<p class=\"rich-text-image\"><img src=\"/rich-document-files/{businessEntityId:D}/images/{imageId}/{variant}\"{imageAttributes} /></p>");
                    break;
                case "video":
                    AppendRenderedVideoBlock(block, builder);
                    break;
                case "table":
                    builder.Append(BuildTableHtmlCache(businessEntityId, block.Html));
                    break;
            }
        }

        return builder.ToString();
    }

    private static string BuildCodeText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return HtmlEntity.DeEntitize(StripTags(html));
    }

    private static string BuildCodeHtmlCache(string? html)
    {
        return WebUtility.HtmlEncode(BuildCodeText(html));
    }

    private static string BuildListPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = LoadInlineHtmlDocument(html);
        var root = document.DocumentNode.SelectSingleNode("//root") ?? document.DocumentNode;
        var items = root.Descendants("li")
            .Select(x => BuildInlineText(x.InnerHtml))
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return string.Join(Environment.NewLine, items).Trim();
    }

    private static string BuildListHtmlCache(Guid businessEntityId, string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = LoadInlineHtmlDocument(html);
        var root = document.DocumentNode.SelectSingleNode("//root") ?? document.DocumentNode;
        var builder = new StringBuilder();

        foreach (var child in root.ChildNodes)
        {
            AppendListHtmlCache(businessEntityId, child, builder);
        }

        return builder.ToString();
    }

    // Builds a stable DOM anchor for a block inside a persisted rich-text chunk.
    public static string BuildBlockAnchor(Guid chunkId, int blockIndex)
    {
        return $"rt-chunk-{chunkId:N}-block-{blockIndex}";
    }

    // Extracts readable text from sanitized inline HTML.
    public static string BuildInlineText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = LoadInlineHtmlDocument(html);
        var root = document.DocumentNode.SelectSingleNode("//root") ?? document.DocumentNode;
        var builder = new StringBuilder();

        foreach (var child in root.ChildNodes)
        {
            AppendInlineText(child, builder);
        }

        return builder.ToString().Trim();
    }

    // Считает количество текстовых символов чанка.
    public static int BuildCharCount(IReadOnlyList<RichTextBlock> blocks)
    {
        return BuildPlainText(blocks).Length;
    }

    // Считает checksum сериализованного envelope JSON.
    public static string BuildChecksum(string envelopeJson)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(envelopeJson ?? string.Empty);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    // Грубое, но достаточное для MVP удаление HTML-тегов из inline-html.
    private static string StripTags(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(html.Length);
        var insideTag = false;
        foreach (var ch in html)
        {
            if (ch == '<')
            {
                insideTag = true;
                continue;
            }

            if (ch == '>')
            {
                insideTag = false;
                continue;
            }

            if (!insideTag)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    // Собирает безопасный inline HTML для HtmlCache и заново строит URL embedded-картинок.
    private static string BuildInlineHtmlCache(Guid businessEntityId, string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = LoadInlineHtmlDocument(html);
        var root = document.DocumentNode.SelectSingleNode("//root") ?? document.DocumentNode;
        var builder = new StringBuilder();

        foreach (var child in root.ChildNodes)
        {
            AppendInlineHtmlCache(businessEntityId, child, builder);
        }

        return builder.ToString();
    }

    private static string BuildTablePlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = LoadInlineHtmlDocument(html);
        var root = document.DocumentNode.SelectSingleNode("//root") ?? document.DocumentNode;
        var builder = new StringBuilder();

        foreach (var row in root.Descendants("tr"))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            var cells = row.ChildNodes
                .Where(x => x.NodeType == HtmlNodeType.Element && x.Name.ToLowerInvariant() is "td" or "th")
                .Select(x => BuildInlineText(x.InnerHtml))
                .Where(x => !string.IsNullOrWhiteSpace(x));

            builder.Append(string.Join(" ", cells));
        }

        return builder.ToString().Trim();
    }

    private static string BuildTableHtmlCache(Guid businessEntityId, string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = LoadInlineHtmlDocument(html);
        var root = document.DocumentNode.SelectSingleNode("//root") ?? document.DocumentNode;
        var builder = new StringBuilder();

        foreach (var child in root.ChildNodes)
        {
            AppendTableHtmlCache(businessEntityId, child, builder);
        }

        return builder.ToString();
    }

    // Загружает fragment inline-разметки в искусственный root для безопасного обхода.
    private static HtmlDocument LoadInlineHtmlDocument(string? html)
    {
        var document = new HtmlDocument
        {
            OptionFixNestedTags = true
        };

        document.LoadHtml($"<root>{html ?? string.Empty}</root>");
        return document;
    }

    private static void AppendTableHtmlCache(Guid businessEntityId, HtmlNode node, StringBuilder builder)
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
            AppendRenderedInlineImage(businessEntityId, image, builder);
            return;
        }

        if (TryReadInlineVideo(node, out var video))
        {
            AppendRenderedInlineVideo(video, builder);
            return;
        }

        var nodeName = node.Name.ToLowerInvariant();
        if (nodeName is "table" or "thead" or "tbody" or "tfoot" or "tr" or "td" or "th")
        {
            AppendTableTagStart(builder, node);
            foreach (var child in node.ChildNodes)
            {
                AppendTableHtmlCache(businessEntityId, child, builder);
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
                AppendTableHtmlCache(businessEntityId, child, builder);
            }

            builder.Append("</p>");
            return;
        }

        if (nodeName is "strong" or "b" or "em" or "i" or "u" or "code")
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
                AppendTableHtmlCache(businessEntityId, child, builder);
            }

            builder.Append("</").Append(normalizedTag).Append('>');
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendTableHtmlCache(businessEntityId, child, builder);
        }
    }

    private static void AppendListHtmlCache(Guid businessEntityId, HtmlNode node, StringBuilder builder)
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
            AppendRenderedInlineImage(businessEntityId, image, builder);
            return;
        }

        if (TryReadInlineVideo(node, out var video))
        {
            AppendRenderedInlineVideo(video, builder);
            return;
        }

        var nodeName = node.Name.ToLowerInvariant();
        if (nodeName is "ul" or "ol" or "li" or "p")
        {
            builder.Append('<').Append(nodeName).Append('>');
            foreach (var child in node.ChildNodes)
            {
                AppendListHtmlCache(businessEntityId, child, builder);
            }

            builder.Append("</").Append(nodeName).Append('>');
            return;
        }

        if (nodeName == "br")
        {
            builder.Append("<br />");
            return;
        }

        if (nodeName is "strong" or "b" or "em" or "i" or "u" or "code")
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
                AppendListHtmlCache(businessEntityId, child, builder);
            }

            builder.Append("</").Append(normalizedTag).Append('>');
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendListHtmlCache(businessEntityId, child, builder);
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
            var styleParts = new List<string>();
            AppendPositiveIntAttribute(builder, node, "colspan");
            AppendPositiveIntAttribute(builder, node, "rowspan");
            var columnWidths = NormalizePositiveIntList(node.GetAttributeValue("data-colwidth", string.Empty));
            if (string.IsNullOrWhiteSpace(columnWidths))
            {
                columnWidths = NormalizePositiveIntList(node.GetAttributeValue("colwidth", string.Empty));
            }

            if (!string.IsNullOrWhiteSpace(columnWidths))
            {
                builder.Append(" data-colwidth=\"").Append(columnWidths).Append('"');
                var renderedWidth = SumPositiveIntList(columnWidths);
                if (renderedWidth > 0)
                {
                    styleParts.Add($"width: {renderedWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}px");
                }
            }

            var cellBackground = NormalizeCellBackgroundColor(node.GetAttributeValue("data-rich-cell-background", string.Empty));
            if (!string.IsNullOrWhiteSpace(cellBackground))
            {
                builder.Append(" data-rich-cell-background=\"").Append(cellBackground).Append('"');
                styleParts.Add($"background-color: {cellBackground}");
            }

            if (IsTruthyAttribute(node, "data-rich-table-row-number"))
            {
                builder.Append(" data-rich-table-row-number=\"true\"");
            }

            if (styleParts.Count > 0)
            {
                builder.Append(" style=\"")
                    .Append(string.Join("; ", styleParts))
                    .Append('"');
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

    private static string NormalizePositiveIntList(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var values = rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ReadPositiveInt)
            .Where(x => x > 0)
            .Select(x => Math.Min(x, 5000).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        return values.Length > 0 ? string.Join(",", values) : string.Empty;
    }

    private static int SumPositiveIntList(string normalizedValue)
    {
        var total = 0;
        foreach (var item in normalizedValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            total += ReadPositiveInt(item);
        }

        return total;
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

    private static bool IsTruthyAttribute(HtmlNode node, string name)
    {
        var value = node.GetAttributeValue(name, string.Empty);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, name, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    // Добавляет текстовое представление inline-узла, включая alt для inline-картинок.
    private static void AppendInlineText(HtmlNode node, StringBuilder builder)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            builder.Append(HtmlEntity.DeEntitize(node.InnerText ?? string.Empty));
            return;
        }

        if (node.NodeType != HtmlNodeType.Element)
        {
            return;
        }

        if (TryReadInlineImage(node, out var image) && !string.IsNullOrWhiteSpace(image.AltText))
        {
            builder.Append(image.AltText);
            return;
        }

        if (TryReadInlineVideo(node, out var video) && !string.IsNullOrWhiteSpace(video.Title))
        {
            builder.Append(video.Title);
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendInlineText(child, builder);
        }
    }

    // Добавляет sanitized HTML-представление inline-узла в HtmlCache.
    private static void AppendInlineHtmlCache(Guid businessEntityId, HtmlNode node, StringBuilder builder)
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
            AppendRenderedInlineImage(businessEntityId, image, builder);
            return;
        }

        if (TryReadInlineVideo(node, out var video))
        {
            AppendRenderedInlineVideo(video, builder);
            return;
        }

        var nodeName = node.Name.ToLowerInvariant();
        if (nodeName == "br")
        {
            builder.Append("<br />");
            return;
        }

        if (nodeName is "strong" or "b" or "em" or "i" or "u" or "code")
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
                AppendInlineHtmlCache(businessEntityId, child, builder);
            }
            builder.Append("</").Append(normalizedTag).Append('>');
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendInlineHtmlCache(businessEntityId, child, builder);
        }
    }

    // Пытается прочитать inline image marker из span/img без доверия к произвольным атрибутам.
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

        if (string.IsNullOrWhiteSpace(image.DisplayVariant))
        {
            image.DisplayVariant = "original";
        }

        return true;
    }

    // Рендерит inline image marker как span + img с URL, построенным из documentId и imageId.
    private static void AppendRenderedInlineImage(Guid businessEntityId, InlineImageDescriptor image, StringBuilder builder)
    {
        var encodedImageId = WebUtility.HtmlEncode(image.ImageId ?? string.Empty);
        var encodedVariant = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(image.DisplayVariant) ? "original" : image.DisplayVariant);
        var encodedAlt = WebUtility.HtmlEncode(image.AltText ?? string.Empty);
        var urlImageId = Uri.EscapeDataString(image.ImageId ?? string.Empty);
        var urlVariant = Uri.EscapeDataString(string.IsNullOrWhiteSpace(image.DisplayVariant) ? "original" : image.DisplayVariant);
        var block = new RichTextBlock
        {
            ImageId = image.ImageId ?? string.Empty,
            DisplayVariant = string.IsNullOrWhiteSpace(image.DisplayVariant) ? "original" : image.DisplayVariant,
            AltText = image.AltText ?? string.Empty,
            Width = image.Width,
            Height = image.Height
        };

        builder.Append("<span class=\"rich-text-inline-image\"");
        builder.Append($" data-rich-image-id=\"{encodedImageId}\"");
        builder.Append($" data-display-variant=\"{encodedVariant}\"");
        builder.Append($" data-alt-text=\"{encodedAlt}\"");
        if (image.Width > 0)
        {
            builder.Append($" data-width=\"{image.Width}\"");
        }

        if (image.Height > 0)
        {
            builder.Append($" data-height=\"{image.Height}\"");
        }

        builder.Append(">");
        builder.Append(
            $"<img src=\"/rich-document-files/{businessEntityId:D}/images/{urlImageId}/{urlVariant}\"{BuildImageAttributes(block, encodedAlt, urlImageId, urlVariant)} />");
        builder.Append("</span>");
    }

    // Пытается прочитать inline video marker из span/video без доверия к произвольным атрибутам.
    private static bool TryReadInlineVideo(HtmlNode node, out InlineVideoDescriptor video)
    {
        video = new InlineVideoDescriptor();
        if (node.NodeType != HtmlNodeType.Element)
        {
            return false;
        }

        var nodeName = node.Name.ToLowerInvariant();
        if (nodeName != "video" &&
            !(nodeName == "span" && HasCssClass(node, "rich-text-inline-video")))
        {
            return false;
        }

        var videoNode = nodeName == "video"
            ? node
            : node.Descendants("video").FirstOrDefault();

        var videoId = ReadAttribute(node, "data-rich-video-id");
        if (string.IsNullOrWhiteSpace(videoId) && videoNode != null)
        {
            videoId = ReadAttribute(videoNode, "data-rich-video-id");
        }

        if (string.IsNullOrWhiteSpace(videoId) && videoNode != null)
        {
            var src = ReadAttribute(videoNode, "src");
            if (!TryParseMediaServerVideoUrl(src, out videoId))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(videoId))
        {
            return false;
        }

        video = new InlineVideoDescriptor
        {
            VideoId = videoId,
            Title = ReadFirstAttribute(node, videoNode, "data-video-title", "title", "aria-label")
        };

        return true;
    }

    // Рендерит inline video marker как span + video с фиксированной высотой просмотра.
    private static void AppendRenderedInlineVideo(InlineVideoDescriptor video, StringBuilder builder)
    {
        var encodedVideoId = WebUtility.HtmlEncode(video.VideoId ?? string.Empty);
        var encodedTitle = WebUtility.HtmlEncode(video.Title ?? string.Empty);
        var urlVideoId = Uri.EscapeDataString(video.VideoId ?? string.Empty);

        builder.Append("<span class=\"rich-text-inline-video\"");
        builder.Append($" data-rich-video-id=\"{encodedVideoId}\"");
        builder.Append($" data-video-title=\"{encodedTitle}\"");
        builder.Append(">");
        builder.Append(
            $"<video src=\"/media-server-files/videos/{urlVideoId}/original\" data-rich-video-id=\"{encodedVideoId}\" title=\"{encodedTitle}\" controls preload=\"metadata\" style=\"height: 300px; max-width: 100%;\"></video>");
        builder.Append("</span>");
    }

    private static void AppendRenderedVideoBlock(RichTextBlock block, StringBuilder builder)
    {
        var descriptor = new InlineVideoDescriptor
        {
            VideoId = block.VideoId ?? string.Empty,
            Title = block.VideoTitle ?? string.Empty
        };

        builder.Append("<p class=\"rich-text-video\">");
        AppendRenderedInlineVideo(descriptor, builder);
        builder.Append("</p>");
    }

    // Читает первый непустой атрибут из основного узла или fallback img-узла.
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

    // Читает HTML-атрибут с de-entity нормализацией.
    private static string ReadAttribute(HtmlNode node, string name)
    {
        return HtmlEntity.DeEntitize(node.GetAttributeValue(name, string.Empty)).Trim();
    }

    // Читает положительный pixel-размер из основного узла или fallback img-узла.
    private static int ReadPositivePixelValue(HtmlNode node, HtmlNode? fallbackNode, string name)
    {
        var value = ReadPositivePixelValue(node, name);
        if (value > 0 || fallbackNode == null || ReferenceEquals(node, fallbackNode))
        {
            return value;
        }

        return ReadPositivePixelValue(fallbackNode, name);
    }

    // Читает положительный pixel-размер из data-атрибута, прямого атрибута или style.
    private static int ReadPositivePixelValue(HtmlNode node, string name)
    {
        var dataAttribute = ReadPositiveInt(node.GetAttributeValue($"data-{name}", string.Empty));
        if (dataAttribute > 0)
        {
            return dataAttribute;
        }

        var directAttribute = ReadPositiveInt(node.GetAttributeValue(name, string.Empty));
        if (directAttribute > 0)
        {
            return directAttribute;
        }

        var style = node.GetAttributeValue("style", string.Empty);
        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = declaration.IndexOf(':');
            if (separatorIndex < 0)
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

    // Парсит положительное целое значение для размеров изображения.
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

    // Проверяет наличие CSS-класса у HTML-узла.
    private static bool HasCssClass(HtmlNode node, string className)
    {
        var classAttribute = node.GetAttributeValue("class", string.Empty);
        return classAttribute
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => string.Equals(x, className, StringComparison.OrdinalIgnoreCase));
    }

    // Извлекает imageId и variant из безопасного rich-document image URL.
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

    // Извлекает videoId из безопасного MediaServer URL.
    private static bool TryParseMediaServerVideoUrl(string? src, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(src))
        {
            return false;
        }

        const string marker = "/media-server-files/videos/";
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
        if (parts.Length < 2 || !string.Equals(parts[1], "original", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        videoId = Uri.UnescapeDataString(parts[0]);
        return !string.IsNullOrWhiteSpace(videoId);
    }

    private static string BuildImageAttributes(RichTextBlock block, string encodedAlt, string imageId, string variant)
    {
        var builder = new StringBuilder();
        builder.Append($" alt=\"{encodedAlt}\"");
        builder.Append($" data-rich-image-id=\"{imageId}\"");
        builder.Append($" data-display-variant=\"{variant}\"");

        if (block.Width > 0)
        {
            builder.Append($" width=\"{block.Width}\"");
        }

        if (block.Height > 0)
        {
            builder.Append($" height=\"{block.Height}\"");
        }

        var styleParts = new List<string>();
        if (block.Width > 0)
        {
            styleParts.Add($"width: {block.Width}px");
        }

        if (block.Height > 0)
        {
            styleParts.Add($"height: {block.Height}px");
        }

        if (styleParts.Count > 0)
        {
            builder.Append($" style=\"{string.Join("; ", styleParts)}\"");
        }

        builder.Append(" loading=\"lazy\"");
        return builder.ToString();
    }

    // JSON-body чанка внутри envelope.
    private sealed class RichTextChunkPayloadBody
    {
        [JsonPropertyName("blocks")]
        public List<RichTextBlock>? Blocks { get; set; }
    }

    // Техническое описание inline-картинки внутри paragraph/heading HTML.
    private sealed class InlineImageDescriptor
    {
        public string ImageId { get; set; } = string.Empty;

        public string DisplayVariant { get; set; } = "original";

        public string AltText { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }
    }

    // Техническое описание inline-видео внутри paragraph/heading HTML.
    private sealed class InlineVideoDescriptor
    {
        public string VideoId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
    }
}

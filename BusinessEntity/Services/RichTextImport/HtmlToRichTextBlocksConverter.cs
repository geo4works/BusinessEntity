using System.Net;
using System.Text;
using BusinessEntity.Core.RichText;
using HtmlAgilityPack;

namespace BusinessEntity.Services.RichTextImport
{
    // Общий low-level конвертер HTML -> rich-text blocks/files.
    // Используется и для прямого HTML-импорта, и для Markdown после промежуточного рендера в HTML.
    public class HtmlToRichTextBlocksConverter
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public HtmlToRichTextBlocksConverter(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<RichTextImportContent> ConvertHtmlAsync(
            string html,
            CancellationToken cancellationToken = default)
        {
            var document = new HtmlDocument();
            document.LoadHtml(html ?? string.Empty);

            var root = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
            var blocks = new List<RichTextBlock>();
            var files = new List<RichTextEmbeddedFile>();

            foreach (var child in root.ChildNodes)
            {
                await ConvertNodeToBlocksAsync(child, blocks, files, cancellationToken);
            }

            return new RichTextImportContent
            {
                Blocks = blocks,
                Files = files
            };
        }

        // Рекурсивно преобразует HTML-узел в один или несколько поддерживаемых блоков.
        private async Task ConvertNodeToBlocksAsync(
            HtmlNode node,
            List<RichTextBlock> blocks,
            List<RichTextEmbeddedFile> files,
            CancellationToken cancellationToken)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    blocks.Add(new RichTextBlock
                    {
                        Kind = "paragraph",
                        Html = WebUtility.HtmlEncode(text)
                    });
                }
                return;
            }

            if (node.NodeType != HtmlNodeType.Element)
            {
                return;
            }

            var nodeName = node.Name.ToLowerInvariant();
            if (nodeName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                var level = int.Parse(nodeName.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
                var headingHtml = await SanitizeInlineHtmlAsync(node, files, cancellationToken);
                if (!string.IsNullOrWhiteSpace(headingHtml))
                {
                    blocks.Add(new RichTextBlock
                    {
                        Kind = "heading",
                        Level = level,
                        Html = headingHtml
                    });
                }
                return;
            }

            if (nodeName == "img")
            {
                var existingImageBlock = TryBuildExistingEmbeddedImageBlock(node);
                if (existingImageBlock != null)
                {
                    blocks.Add(existingImageBlock);
                    return;
                }

                var imageFile = await TryImportImageAsync(node, cancellationToken);
                if (imageFile != null)
                {
                    files.Add(imageFile);
                    blocks.Add(new RichTextBlock
                    {
                        Kind = "image",
                        ImageId = imageFile.ImageId,
                        DisplayVariant = imageFile.Variant,
                        AltText = node.GetAttributeValue("alt", string.Empty),
                        Width = ReadPositivePixelValue(node, "width"),
                        Height = ReadPositivePixelValue(node, "height")
                    });
                }
                return;
            }

            if (nodeName == "video" || (nodeName == "span" && HasCssClass(node, "rich-text-inline-video")))
            {
                var existingVideoBlock = TryBuildExistingVideoBlock(node);
                if (existingVideoBlock != null)
                {
                    blocks.Add(existingVideoBlock);
                }
                return;
            }

            if (nodeName == "table")
            {
                var tableHtml = await SanitizeTableHtmlAsync(node, files, cancellationToken);
                if (!string.IsNullOrWhiteSpace(tableHtml))
                {
                    blocks.Add(new RichTextBlock
                    {
                        Kind = "table",
                        Html = tableHtml
                    });
                }

                return;
            }

            if (nodeName is "ul" or "ol")
            {
                var listHtml = await SanitizeListHtmlAsync(node, files, cancellationToken);
                if (!string.IsNullOrWhiteSpace(listHtml))
                {
                    blocks.Add(new RichTextBlock
                    {
                        Kind = "list",
                        Html = listHtml
                    });
                }

                return;
            }

            if (nodeName == "pre")
            {
                var codeHtml = SanitizeCodeBlockHtml(node);
                if (!string.IsNullOrWhiteSpace(codeHtml))
                {
                    blocks.Add(new RichTextBlock
                    {
                        Kind = "code",
                        Html = codeHtml
                    });
                }

                return;
            }

            if (nodeName is "p" or "div")
            {
                // Если контейнер содержит block-level потомков, обрабатываем их как самостоятельные блоки.
                if (node.ChildNodes.Any(IsBlockLikeNode))
                {
                    foreach (var child in node.ChildNodes)
                    {
                        await ConvertNodeToBlocksAsync(child, blocks, files, cancellationToken);
                    }
                    return;
                }

                var paragraphHtml = await SanitizeInlineHtmlAsync(node, files, cancellationToken);
                if (!string.IsNullOrWhiteSpace(paragraphHtml))
                {
                    blocks.Add(new RichTextBlock
                    {
                        Kind = "paragraph",
                        Html = paragraphHtml
                    });
                }
                return;
            }

            if (nodeName == "br")
            {
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                await ConvertNodeToBlocksAsync(child, blocks, files, cancellationToken);
            }
        }

        private static bool IsBlockLikeNode(HtmlNode node)
        {
            if (node.NodeType != HtmlNodeType.Element)
            {
                return false;
            }

            var nodeName = node.Name.ToLowerInvariant();
            return nodeName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "p" or "div" or "table" or "ul" or "ol" or "pre";
        }

        private static string SanitizeCodeBlockHtml(HtmlNode preNode)
        {
            var codeNode = preNode.Descendants("code").FirstOrDefault();
            var rawText = HtmlEntity.DeEntitize((codeNode ?? preNode).InnerText ?? string.Empty)
                .TrimEnd('\r', '\n');

            return WebUtility.HtmlEncode(rawText);
        }

        private async Task<string> SanitizeListHtmlAsync(
            HtmlNode listNode,
            List<RichTextEmbeddedFile> files,
            CancellationToken cancellationToken)
        {
            var builder = new StringBuilder();
            await AppendSanitizedListHtmlAsync(listNode, builder, files, cancellationToken);
            return builder.ToString().Trim();
        }

        private async Task AppendSanitizedListHtmlAsync(
            HtmlNode node,
            StringBuilder builder,
            List<RichTextEmbeddedFile> files,
            CancellationToken cancellationToken)
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

            var nodeName = node.Name.ToLowerInvariant();
            if (TryReadExistingVideo(node, out var existingVideo))
            {
                AppendInlineVideoMarker(builder, existingVideo);
                return;
            }

            if (TryReadExistingEmbeddedImage(node, out var existingImage))
            {
                AppendInlineImageMarker(builder, existingImage);
                return;
            }

            if (nodeName == "img")
            {
                var imageFile = await TryImportImageAsync(node, cancellationToken);
                if (imageFile != null)
                {
                    files.Add(imageFile);
                    AppendInlineImageMarker(
                        builder,
                        new InlineImageDescriptor
                        {
                            ImageId = imageFile.ImageId,
                            DisplayVariant = imageFile.Variant,
                            AltText = node.GetAttributeValue("alt", string.Empty),
                            Width = ReadPositivePixelValue(node, "width"),
                            Height = ReadPositivePixelValue(node, "height")
                        });
                }

                return;
            }

            if (nodeName is "ul" or "ol" or "li" or "p")
            {
                builder.Append('<').Append(nodeName).Append('>');
                foreach (var child in node.ChildNodes)
                {
                    await AppendSanitizedListHtmlAsync(child, builder, files, cancellationToken);
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
                    await AppendSanitizedListHtmlAsync(child, builder, files, cancellationToken);
                }

                builder.Append("</").Append(normalizedTag).Append('>');
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                await AppendSanitizedListHtmlAsync(child, builder, files, cancellationToken);
            }
        }

        // Санитизирует table-html как отдельный блочный объект rich-документа.
        private async Task<string> SanitizeTableHtmlAsync(
            HtmlNode tableNode,
            List<RichTextEmbeddedFile> files,
            CancellationToken cancellationToken)
        {
            var builder = new StringBuilder();
            await AppendSanitizedTableHtmlAsync(tableNode, builder, files, cancellationToken);
            return builder.ToString().Trim();
        }

        // Рекурсивно собирает безопасную table-разметку. Разрешены только структура таблицы,
        // простые абзацы/форматирование внутри ячеек и inline media markers.
        private async Task AppendSanitizedTableHtmlAsync(
            HtmlNode node,
            StringBuilder builder,
            List<RichTextEmbeddedFile> files,
            CancellationToken cancellationToken)
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

            var nodeName = node.Name.ToLowerInvariant();
            if (TryReadExistingVideo(node, out var existingVideo))
            {
                AppendInlineVideoMarker(builder, existingVideo);
                return;
            }

            if (TryReadExistingEmbeddedImage(node, out var existingImage))
            {
                AppendInlineImageMarker(builder, existingImage);
                return;
            }

            if (nodeName == "img")
            {
                var imageFile = await TryImportImageAsync(node, cancellationToken);
                if (imageFile != null)
                {
                    files.Add(imageFile);
                    AppendInlineImageMarker(
                        builder,
                        new InlineImageDescriptor
                        {
                            ImageId = imageFile.ImageId,
                            DisplayVariant = imageFile.Variant,
                            AltText = node.GetAttributeValue("alt", string.Empty),
                            Width = ReadPositivePixelValue(node, "width"),
                            Height = ReadPositivePixelValue(node, "height")
                        });
                }

                return;
            }

            if (nodeName is "table" or "thead" or "tbody" or "tfoot" or "tr" or "td" or "th")
            {
                AppendTableTagStart(builder, node);
                foreach (var child in node.ChildNodes)
                {
                    await AppendSanitizedTableHtmlAsync(child, builder, files, cancellationToken);
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
                    await AppendSanitizedTableHtmlAsync(child, builder, files, cancellationToken);
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
                    await AppendSanitizedTableHtmlAsync(child, builder, files, cancellationToken);
                }

                builder.Append("</").Append(normalizedTag).Append('>');
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                await AppendSanitizedTableHtmlAsync(child, builder, files, cancellationToken);
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
                AppendColumnWidthsAttribute(builder, node);
                AppendCellBackgroundAttribute(builder, node);
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

        private static void AppendColumnWidthsAttribute(StringBuilder builder, HtmlNode node)
        {
            var value = NormalizePositiveIntList(node.GetAttributeValue("data-colwidth", string.Empty));
            if (string.IsNullOrWhiteSpace(value))
            {
                value = NormalizePositiveIntList(node.GetAttributeValue("colwidth", string.Empty));
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.Append(" data-colwidth=\"")
                    .Append(value)
                    .Append('"');
            }
        }

        private static void AppendCellBackgroundAttribute(StringBuilder builder, HtmlNode node)
        {
            var value = NormalizeCellBackgroundColor(node.GetAttributeValue("data-rich-cell-background", string.Empty));
            if (string.IsNullOrWhiteSpace(value))
            {
                var style = node.GetAttributeValue("style", string.Empty);
                value = NormalizeCellBackgroundColor(ReadCssDeclaration(style, "background-color"));
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = NormalizeCellBackgroundColor(ReadCssDeclaration(style, "background"));
                }
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                value = NormalizeCellBackgroundColor(node.GetAttributeValue("bgcolor", string.Empty));
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.Append(" data-rich-cell-background=\"")
                    .Append(value)
                    .Append('"');
            }
        }

        private static string ReadCssDeclaration(string? style, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(style))
            {
                return string.Empty;
            }

            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = declaration.IndexOf(':');
                if (separatorIndex < 0)
                {
                    continue;
                }

                var name = declaration[..separatorIndex].Trim();
                if (!string.Equals(name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return declaration[(separatorIndex + 1)..].Trim();
            }

            return string.Empty;
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

        private static bool IsTruthyAttribute(HtmlNode node, string name)
        {
            var value = node.GetAttributeValue(name, string.Empty);
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, name, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static RichTextBlock? TryBuildExistingEmbeddedImageBlock(HtmlNode imageNode)
        {
            if (!TryReadExistingEmbeddedImage(imageNode, out var image))
            {
                return null;
            }

            return new RichTextBlock
            {
                Kind = "image",
                ImageId = image.ImageId,
                DisplayVariant = image.DisplayVariant,
                AltText = image.AltText,
                Width = image.Width,
                Height = image.Height
            };
        }

        private static RichTextBlock? TryBuildExistingVideoBlock(HtmlNode node)
        {
            if (!TryReadExistingVideo(node, out var video))
            {
                return null;
            }

            return new RichTextBlock
            {
                Kind = "video",
                VideoId = video.VideoId,
                VideoTitle = video.Title
            };
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
            var dataAttributeValue = ReadPositiveInt(node.GetAttributeValue($"data-{name}", string.Empty));
            if (dataAttributeValue > 0)
            {
                return dataAttributeValue;
            }

            var attributeValue = ReadPositiveInt(node.GetAttributeValue(name, string.Empty));
            if (attributeValue > 0)
            {
                return attributeValue;
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

        // Санитизирует inline-html так, чтобы в документ попали только разрешенные теги форматирования и inline image markers.
        private async Task<string> SanitizeInlineHtmlAsync(
            HtmlNode node,
            List<RichTextEmbeddedFile> files,
            CancellationToken cancellationToken)
        {
            var builder = new StringBuilder();
            foreach (var child in node.ChildNodes)
            {
                await AppendSanitizedInlineHtmlAsync(child, builder, files, cancellationToken);
            }
            return builder.ToString().Trim();
        }

        // Рекурсивно собирает безопасный inline-html для paragraph/heading блока.
        private async Task AppendSanitizedInlineHtmlAsync(
            HtmlNode node,
            StringBuilder builder,
            List<RichTextEmbeddedFile> files,
            CancellationToken cancellationToken)
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

            var nodeName = node.Name.ToLowerInvariant();
            if (TryReadExistingVideo(node, out var existingVideo))
            {
                AppendInlineVideoMarker(builder, existingVideo);
                return;
            }

            if (TryReadExistingEmbeddedImage(node, out var existingImage))
            {
                AppendInlineImageMarker(builder, existingImage);
                return;
            }

            if (nodeName == "img")
            {
                var imageFile = await TryImportImageAsync(node, cancellationToken);
                if (imageFile != null)
                {
                    files.Add(imageFile);
                    AppendInlineImageMarker(
                        builder,
                        new InlineImageDescriptor
                        {
                            ImageId = imageFile.ImageId,
                            DisplayVariant = imageFile.Variant,
                            AltText = node.GetAttributeValue("alt", string.Empty),
                            Width = ReadPositivePixelValue(node, "width"),
                            Height = ReadPositivePixelValue(node, "height")
                        });
                }

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
                    await AppendSanitizedInlineHtmlAsync(child, builder, files, cancellationToken);
                }
                builder.Append("</").Append(normalizedTag).Append('>');
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                await AppendSanitizedInlineHtmlAsync(child, builder, files, cancellationToken);
            }
        }

        // Пытается прочитать уже сохраненную embedded-картинку из inline span/img marker.
        private static bool TryReadExistingEmbeddedImage(HtmlNode node, out InlineImageDescriptor image)
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

            var imageId = ReadAttribute(node, "data-rich-image-id");
            var variant = ReadAttribute(node, "data-display-variant");
            if (string.IsNullOrWhiteSpace(imageId) && imageNode != null)
            {
                imageId = ReadAttribute(imageNode, "data-rich-image-id");
            }

            if (string.IsNullOrWhiteSpace(variant) && imageNode != null)
            {
                variant = ReadAttribute(imageNode, "data-display-variant");
            }

            if (string.IsNullOrWhiteSpace(imageId) && imageNode != null)
            {
                var src = ReadAttribute(imageNode, "src");
                if (!TryParseRichDocumentImageUrl(src, out imageId, out variant))
                {
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(imageId))
            {
                return false;
            }

            image = new InlineImageDescriptor
            {
                ImageId = imageId,
                DisplayVariant = string.IsNullOrWhiteSpace(variant) ? "original" : variant,
                AltText = ReadFirstAttribute(node, imageNode, "data-alt-text", "alt"),
                Width = ReadPositivePixelValue(node, imageNode, "width"),
                Height = ReadPositivePixelValue(node, imageNode, "height")
            };

            return true;
        }

        // Пытается прочитать уже сохраненное видео из inline span/video marker.
        private static bool TryReadExistingVideo(HtmlNode node, out InlineVideoDescriptor video)
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

        // Добавляет canonical inline image marker в HTML блока без сохранения src.
        private static void AppendInlineImageMarker(StringBuilder builder, InlineImageDescriptor image)
        {
            builder.Append("<span class=\"rich-text-inline-image\"");
            builder.Append(" data-rich-image-id=\"").Append(WebUtility.HtmlEncode(image.ImageId ?? string.Empty)).Append('"');
            builder.Append(" data-display-variant=\"").Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(image.DisplayVariant) ? "original" : image.DisplayVariant)).Append('"');
            builder.Append(" data-alt-text=\"").Append(WebUtility.HtmlEncode(image.AltText ?? string.Empty)).Append('"');

            if (image.Width > 0)
            {
                builder.Append(" data-width=\"").Append(image.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
            }

            if (image.Height > 0)
            {
                builder.Append(" data-height=\"").Append(image.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
            }

            builder.Append("></span>");
        }

        // Добавляет canonical inline video marker в HTML блока без сохранения src.
        private static void AppendInlineVideoMarker(StringBuilder builder, InlineVideoDescriptor video)
        {
            builder.Append("<span class=\"rich-text-inline-video\"");
            builder.Append(" data-rich-video-id=\"").Append(WebUtility.HtmlEncode(video.VideoId ?? string.Empty)).Append('"');
            builder.Append(" data-video-title=\"").Append(WebUtility.HtmlEncode(video.Title ?? string.Empty)).Append('"');
            builder.Append("></span>");
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

        // Проверяет наличие CSS-класса у HTML-узла.
        private static bool HasCssClass(HtmlNode node, string className)
        {
            var classAttribute = node.GetAttributeValue("class", string.Empty);
            return classAttribute
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(x => string.Equals(x, className, StringComparison.OrdinalIgnoreCase));
        }

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

        // Импортирует картинку из data-uri или внешнего http/https URL.
        private async Task<RichTextEmbeddedFile?> TryImportImageAsync(HtmlNode imageNode, CancellationToken cancellationToken)
        {
            var src = imageNode.GetAttributeValue("src", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(src))
            {
                return null;
            }

            byte[] content;
            string contentType;
            string fileName;

            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var dataUri = ParseDataUri(src);
                content = dataUri.content;
                contentType = dataUri.contentType;
                fileName = $"embedded.{GetExtensionFromContentType(contentType)}";
            }
            else if (Uri.TryCreate(src, UriKind.Absolute, out var absoluteUri) &&
                     (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
            {
                using var client = _httpClientFactory.CreateClient();
                using var response = await client.GetAsync(absoluteUri, cancellationToken);
                response.EnsureSuccessStatusCode();
                contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                fileName = Path.GetFileName(absoluteUri.LocalPath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = $"external.{GetExtensionFromContentType(contentType)}";
                }
            }
            else
            {
                return null;
            }

            return new RichTextEmbeddedFile
            {
                ImageId = Guid.NewGuid().ToString("N"),
                Variant = "original",
                FileName = fileName,
                ContentType = contentType,
                Content = content
            };
        }

        private static (byte[] content, string contentType) ParseDataUri(string dataUri)
        {
            var commaIndex = dataUri.IndexOf(',');
            if (commaIndex < 0)
            {
                throw new InvalidOperationException("Invalid data URI.");
            }

            var header = dataUri.Substring(5, commaIndex - 5);
            var payload = dataUri[(commaIndex + 1)..];
            var contentType = header.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "application/octet-stream";
            var content = Convert.FromBase64String(payload);
            return (content, contentType);
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

        // Техническое описание inline-картинки при HTML import/save roundtrip.
        private sealed class InlineImageDescriptor
        {
            public string ImageId { get; set; } = string.Empty;

            public string DisplayVariant { get; set; } = "original";

            public string AltText { get; set; } = string.Empty;

            public int Width { get; set; }

            public int Height { get; set; }
        }

        // Техническое описание inline-видео при HTML save roundtrip.
        private sealed class InlineVideoDescriptor
        {
            public string VideoId { get; set; } = string.Empty;

            public string Title { get; set; } = string.Empty;
        }
    }
}

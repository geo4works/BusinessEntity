using BusinessEntity.Core.Saving;
using System.Net;
using System.Text;
using BusinessEntity.Core.RichText;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.Services.RichTextImport;

namespace BusinessEntity.Services.RichTextPaste
{
    // Server-side helper для paste-as-import: детектирует буфер, конвертирует его и возвращает HTML-fragment для editor.
    public class RichTextClipboardImportHelper
    {
        private readonly IRichTextClipboardPasteDetector _detector;
        private readonly IRichDocFormatConverterFactory _converterFactory;
        private readonly IDataProviderConnector _dataProviderConnector;

        public RichTextClipboardImportHelper(
            IRichTextClipboardPasteDetector detector,
            IRichDocFormatConverterFactory converterFactory,
            IDataProviderConnector dataProviderConnector)
        {
            _detector = detector;
            _converterFactory = converterFactory;
            _dataProviderConnector = dataProviderConnector;
        }

        // Конвертирует clipboard-буфер через существующий rich-text import pipeline.
        public async Task<RichTextClipboardPasteResult> ConvertAsync(
            Guid businessEntityId,
            RichTextClipboardPasteRequest request,
            CancellationToken cancellationToken = default)
        {
            if (businessEntityId == Guid.Empty)
            {
                return new RichTextClipboardPasteResult { Handled = false };
            }

            var source = _detector.Detect(request);
            if (source == null)
            {
                return new RichTextClipboardPasteResult { Handled = false };
            }

            var converter = _converterFactory.GetRequiredConverter(source.FileExtension);
            var converted = await converter.ConvertAsync(
                source.VirtualFileName,
                source.GetBytes(),
                cancellationToken);

            if (converted.Files.Count > 0)
            {
                await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
                {
                    Files = converted.Files.Select(file => new EntityFileChange(businessEntityId, file.ImageId,
                        file.Variant, file.FileName, file.ContentType, file.Content)).ToArray()
                }, cancellationToken);
            }

            return new RichTextClipboardPasteResult
            {
                Handled = true,
                Format = source.Format,
                Html = BuildEditorHtmlFragment(businessEntityId, converted.Blocks)
            };
        }

        // Собирает HTML-fragment, который Tiptap умеет распарсить в свои nodes.
        private static string BuildEditorHtmlFragment(Guid businessEntityId, IReadOnlyList<RichTextBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var block in blocks)
            {
                var kind = (block.Kind ?? string.Empty).ToLowerInvariant();
                switch (kind)
                {
                    case "heading":
                        var level = Math.Clamp(block.Level <= 0 ? 2 : block.Level, 1, 3);
                        builder.Append("<h").Append(level).Append('>')
                            .Append(block.Html ?? string.Empty)
                            .Append("</h").Append(level).Append('>');
                        break;
                    case "table":
                        builder.Append(block.Html ?? string.Empty);
                        break;
                    case "code":
                        builder.Append("<pre><code>")
                            .Append(BuildCodeHtml(block.Html))
                            .Append("</code></pre>");
                        break;
                    case "list":
                        builder.Append(block.Html ?? string.Empty);
                        break;
                    case "image":
                        AppendImageBlock(builder, block);
                        break;
                    case "video":
                        AppendVideoBlock(builder, block);
                        break;
                    case "paragraph":
                    default:
                        builder.Append("<p>")
                            .Append(block.Html ?? string.Empty)
                            .Append("</p>");
                        break;
                }
            }

            return builder.ToString();
        }

        private static void AppendImageBlock(StringBuilder builder, RichTextBlock block)
        {
            builder.Append("<p><span class=\"rich-text-inline-image\"");
            builder.Append(" data-rich-image-id=\"").Append(HtmlAttr(block.ImageId)).Append('"');
            builder.Append(" data-display-variant=\"").Append(HtmlAttr(string.IsNullOrWhiteSpace(block.DisplayVariant) ? "original" : block.DisplayVariant)).Append('"');
            builder.Append(" data-alt-text=\"").Append(HtmlAttr(block.AltText)).Append('"');

            if (block.Width > 0)
            {
                builder.Append(" data-width=\"").Append(block.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
            }

            if (block.Height > 0)
            {
                builder.Append(" data-height=\"").Append(block.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('"');
            }

            builder.Append("></span></p>");
        }

        private static string BuildCodeHtml(string? html)
        {
            return WebUtility.HtmlEncode(WebUtility.HtmlDecode(StripTags(html ?? string.Empty)) ?? string.Empty);
        }

        private static string StripTags(string html)
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

        private static void AppendVideoBlock(StringBuilder builder, RichTextBlock block)
        {
            builder.Append("<p><span class=\"rich-text-inline-video\"");
            builder.Append(" data-rich-video-id=\"").Append(HtmlAttr(block.VideoId)).Append('"');
            builder.Append(" data-video-title=\"").Append(HtmlAttr(block.VideoTitle)).Append('"');
            builder.Append("></span></p>");
        }

        private static string HtmlAttr(string? value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }
    }
}

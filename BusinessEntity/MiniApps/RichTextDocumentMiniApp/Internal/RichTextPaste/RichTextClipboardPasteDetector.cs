using System.Text.RegularExpressions;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextPaste
{
    // Content-based detector для paste: Markdown распознается по синтаксису, HTML - по markup.
    public class RichTextClipboardPasteDetector : IRichTextClipboardPasteDetector
    {
        private static readonly Regex MarkdownTableSeparatorRegex = new(
            @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$",
            RegexOptions.Compiled);

        private static readonly Regex MarkdownHeadingRegex = new(
            @"^\s{0,3}#{1,6}\s+\S",
            RegexOptions.Compiled);

        private static readonly Regex MarkdownFenceRegex = new(
            @"^\s{0,3}(```|~~~)",
            RegexOptions.Compiled);

        private static readonly Regex MarkdownListRegex = new(
            @"^\s{0,3}([-*+]|\d+[.)])\s+\S",
            RegexOptions.Compiled);

        private static readonly Regex MarkdownLinkRegex = new(
            @"\[[^\]]+\]\([^)]+\)",
            RegexOptions.Compiled);

        private static readonly Regex MarkdownInlineCodeRegex = new(
            @"`[^`\r\n]+`",
            RegexOptions.Compiled);

        private static readonly Regex SupportedHtmlTagRegex = new(
            @"<\s*(table|thead|tbody|tfoot|tr|td|th|h[1-6]|p|div|ul|ol|li|blockquote|pre|code|img|span|strong|b|em|i|u|br)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public RichTextClipboardPasteSource? Detect(RichTextClipboardPasteRequest request)
        {
            if (request == null)
            {
                return null;
            }

            var plainText = NormalizeNewlines(request.PlainText);
            if (LooksLikeMarkdownTable(plainText) || LooksLikeMarkdown(plainText))
            {
                return new RichTextClipboardPasteSource
                {
                    Format = "markdown",
                    FileExtension = ".md",
                    VirtualFileName = "clipboard.md",
                    Content = plainText
                };
            }

            var html = request.Html ?? string.Empty;
            if (LooksLikeSupportedHtml(html))
            {
                return new RichTextClipboardPasteSource
                {
                    Format = "html",
                    FileExtension = ".html",
                    VirtualFileName = "clipboard.html",
                    Content = html
                };
            }

            return null;
        }

        // Определяет pipe-table Markdown по строке-разделителю и соседней строке с колонками.
        private static bool LooksLikeMarkdownTable(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                if (!MarkdownTableSeparatorRegex.IsMatch(lines[i]))
                {
                    continue;
                }

                var header = lines[i - 1];
                if (CountUnescapedPipes(header) >= 1)
                {
                    return true;
                }
            }

            return false;
        }

        // Определяет Markdown-блоки, которые стоит прогонять через import pipeline.
        private static bool LooksLikeMarkdown(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Split('\n');
            if (lines.Any(line => MarkdownHeadingRegex.IsMatch(line) || MarkdownFenceRegex.IsMatch(line)))
            {
                return true;
            }

            if (lines.Count(line => MarkdownListRegex.IsMatch(line)) >= 2)
            {
                return true;
            }

            return MarkdownLinkRegex.IsMatch(text) || MarkdownInlineCodeRegex.IsMatch(text);
        }

        // Проверяет, что clipboard HTML содержит поддерживаемую rich-text структуру.
        private static bool LooksLikeSupportedHtml(string html)
        {
            return !string.IsNullOrWhiteSpace(html) && SupportedHtmlTagRegex.IsMatch(html);
        }

        private static string NormalizeNewlines(string? text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        }

        private static int CountUnescapedPipes(string value)
        {
            var count = 0;
            var escaped = false;
            foreach (var ch in value ?? string.Empty)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '|')
                {
                    count++;
                }
            }

            return count;
        }
    }
}

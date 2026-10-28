using System.Text.RegularExpressions;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport
{
    // Нормализует распространенные Markdown-фрагменты перед Markdig.
    internal static class MarkdownImportPreprocessor
    {
        private static readonly Regex MarkdownTableSeparatorRegex = new(
            @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$",
            RegexOptions.Compiled);

        private static readonly Regex MarkdownFenceRegex = new(
            @"^\s{0,3}(```|~~~)",
            RegexOptions.Compiled);

        public static string Normalize(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return string.Empty;
            }

            var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var result = new List<string>(lines.Length + 4);
            var isInsideFence = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (!isInsideFence &&
                    i > 0 &&
                    i + 1 < lines.Length &&
                    LooksLikeMarkdownTableHeader(line) &&
                    MarkdownTableSeparatorRegex.IsMatch(lines[i + 1]) &&
                    !string.IsNullOrWhiteSpace(lines[i - 1]))
                {
                    result.Add(string.Empty);
                }

                result.Add(line);

                if (MarkdownFenceRegex.IsMatch(line))
                {
                    isInsideFence = !isInsideFence;
                }
            }

            return string.Join('\n', result);
        }

        private static bool LooksLikeMarkdownTableHeader(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            return CountUnescapedPipes(line) >= 1;
        }

        private static int CountUnescapedPipes(string value)
        {
            var count = 0;
            var escaped = false;
            foreach (var ch in value)
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

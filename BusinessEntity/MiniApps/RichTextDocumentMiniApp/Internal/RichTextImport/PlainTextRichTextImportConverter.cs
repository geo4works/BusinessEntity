using System.Net;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport
{
    // Конвертирует plain text в paragraph-блоки rich-text документа.
    public class PlainTextRichTextImportConverter : IRichDocFormatConverter
    {
        public bool CanHandle(string fileExtension)
        {
            return string.Equals(fileExtension, ".txt", StringComparison.OrdinalIgnoreCase);
        }

        public Task<RichTextImportContent> ConvertAsync(
            string fileName,
            byte[] fileBytes,
            CancellationToken cancellationToken = default)
        {
            var paragraphs = DecodeText(fileBytes)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split("\n\n", StringSplitOptions.None)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var blocks = paragraphs
                .Select(paragraph => new BusinessEntityDataBlock
                {
                    Kind = "paragraph",
                    Html = WebUtility.HtmlEncode(paragraph).Replace("\n", "<br />", StringComparison.Ordinal)
                })
                .ToList();

            return Task.FromResult(new RichTextImportContent
            {
                Blocks = blocks,
                Files = Array.Empty<RichTextEmbeddedFile>()
            });
        }

        private static string DecodeText(byte[] bytes)
        {
            return System.Text.Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>());
        }
    }
}

using System.Text;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport
{
    // Импортирует исходный HTML-файл во внутренние rich-text blocks/files.
    public class HtmlRichTextImportConverter : IRichDocFormatConverter
    {
        private readonly HtmlToRichTextBlocksConverter _htmlToBlocksConverter;

        public HtmlRichTextImportConverter(HtmlToRichTextBlocksConverter htmlToBlocksConverter)
        {
            _htmlToBlocksConverter = htmlToBlocksConverter;
        }

        public bool CanHandle(string fileExtension)
        {
            return string.Equals(fileExtension, ".html", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileExtension, ".htm", StringComparison.OrdinalIgnoreCase);
        }

        public Task<RichTextImportContent> ConvertAsync(
            string fileName,
            byte[] fileBytes,
            CancellationToken cancellationToken = default)
        {
            var html = Encoding.UTF8.GetString(fileBytes ?? Array.Empty<byte>());
            return _htmlToBlocksConverter.ConvertHtmlAsync(html, cancellationToken);
        }
    }
}

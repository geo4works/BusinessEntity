using System.Text;
using Markdig;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport
{
    // Импортирует Markdown через промежуточный HTML-рендер и затем отдает его общему HTML-конвертеру.
    public class MarkdownRichTextImportConverter : IRichDocFormatConverter
    {
        private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        private readonly HtmlToRichTextBlocksConverter _htmlToBlocksConverter;

        public MarkdownRichTextImportConverter(HtmlToRichTextBlocksConverter htmlToBlocksConverter)
        {
            _htmlToBlocksConverter = htmlToBlocksConverter;
        }

        public bool CanHandle(string fileExtension)
        {
            return string.Equals(fileExtension, ".md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileExtension, ".markdown", StringComparison.OrdinalIgnoreCase);
        }

        public Task<RichTextImportContent> ConvertAsync(
            string fileName,
            byte[] fileBytes,
            CancellationToken cancellationToken = default)
        {
            var markdown = MarkdownImportPreprocessor.Normalize(Encoding.UTF8.GetString(fileBytes ?? Array.Empty<byte>()));
            var html = Markdown.ToHtml(markdown, MarkdownPipeline);
            return _htmlToBlocksConverter.ConvertHtmlAsync(html, cancellationToken);
        }
    }
}

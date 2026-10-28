using System.Text;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextPaste
{
    // Нормализованный источник clipboard-импорта после content-based detection.
    public class RichTextClipboardPasteSource
    {
        public string Format { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public string VirtualFileName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;

        // Возвращает UTF-8 bytes, чтобы дальше использовать обычный import converter.
        public byte[] GetBytes()
        {
            return Encoding.UTF8.GetBytes(Content ?? string.Empty);
        }
    }
}

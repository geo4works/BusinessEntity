namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextPaste
{
    // Результат server-side обработки clipboard-буфера для вставки в editor.
    public class RichTextClipboardPasteResult
    {
        public bool Handled { get; set; }
        public string Html { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }
}

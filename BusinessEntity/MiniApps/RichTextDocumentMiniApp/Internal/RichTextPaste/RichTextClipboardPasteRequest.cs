namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextPaste
{
    // DTO clipboard-буфера, который приходит из Tiptap paste-hook.
    public class RichTextClipboardPasteRequest
    {
        public string? PlainText { get; set; }
        public string? Html { get; set; }
        public IReadOnlyList<string> Types { get; set; } = Array.Empty<string>();
    }
}

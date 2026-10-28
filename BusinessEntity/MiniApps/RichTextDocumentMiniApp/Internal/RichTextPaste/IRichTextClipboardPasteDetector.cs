namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextPaste
{
    // Детектор формата clipboard-буфера без опоры на расширение файла.
    public interface IRichTextClipboardPasteDetector
    {
        RichTextClipboardPasteSource? Detect(RichTextClipboardPasteRequest request);
    }
}

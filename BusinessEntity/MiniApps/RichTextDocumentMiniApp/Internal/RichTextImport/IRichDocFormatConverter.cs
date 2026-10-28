namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport
{
    // Канонический контракт конвертера внешнего формата rich-document.
    // Одна реализация = один входной формат: TXT, Markdown или HTML.
    public interface IRichDocFormatConverter
    {
        bool CanHandle(string fileExtension);

        Task<RichTextImportContent> ConvertAsync(
            string fileName,
            byte[] fileBytes,
            CancellationToken cancellationToken = default);
    }
}

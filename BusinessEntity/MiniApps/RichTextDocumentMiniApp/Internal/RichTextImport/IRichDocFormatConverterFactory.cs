namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport
{
    // Фабрика выбирает format-конвертер для импортируемого rich-document.
    public interface IRichDocFormatConverterFactory
    {
        IRichDocFormatConverter GetRequiredConverter(string fileExtension);
    }
}

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Результат чтения embedded-файла из storage.
    public class RichTextEmbeddedFileContent
    {
        // MIME-тип отдаваемого файла.
        public string ContentType { get; set; } = "application/octet-stream";

        // Имя файла для диагностики и расширения.
        public string FileName { get; set; } = string.Empty;

        // Бинарное содержимое файла.
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}

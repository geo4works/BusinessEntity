namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Embedded-файл rich-text документа.
    public class RichTextEmbeddedFile
    {
        // Идентификатор изображения, на который ссылается image-блок или inline image marker.
        public string ImageId { get; set; } = string.Empty;

        // Вариант изображения. В MVP используем только original.
        public string Variant { get; set; } = "original";

        // Оригинальное имя файла.
        public string FileName { get; set; } = string.Empty;

        // MIME-тип embedded-файла.
        public string ContentType { get; set; } = "application/octet-stream";

        // Содержимое файла.
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}

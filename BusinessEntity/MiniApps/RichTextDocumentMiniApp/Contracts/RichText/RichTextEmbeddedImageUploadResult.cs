namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Результат загрузки embedded-изображения rich-text документа.
    public sealed class RichTextEmbeddedImageUploadResult
    {
        public string ImageId { get; set; } = string.Empty;

        public string Variant { get; set; } = "original";

        public string Url { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;
    }
}

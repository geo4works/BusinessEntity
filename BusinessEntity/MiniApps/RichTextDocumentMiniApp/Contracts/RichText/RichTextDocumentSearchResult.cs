namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Результат поиска по чанкам rich-text документа.
    public sealed class RichTextDocumentSearchResult
    {
        public Guid DocumentId { get; set; }
        public string Query { get; set; } = string.Empty;
        public RichTextDocumentViewportPosition Position { get; set; } = new();
        public string Preview { get; set; } = string.Empty;
    }
}

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Текстовое выделение в rich-text viewport вместе с грубой позицией.
    public sealed class RichTextDocumentTextSelection
    {
        public string Text { get; set; } = string.Empty;
        public RichTextDocumentViewportPosition? Position { get; set; }
    }
}

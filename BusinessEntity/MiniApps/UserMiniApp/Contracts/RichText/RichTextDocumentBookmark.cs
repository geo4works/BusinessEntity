using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
namespace BusinessEntity.MiniApps.UserMiniApp.Contracts.RichText
{
    // Пользовательская закладка внутри rich-text документа.
    public sealed class RichTextDocumentBookmark
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid DocumentId { get; set; }
        public long ChunkSortOrder { get; set; }
        public int BlockIndex { get; set; }
        public string SelectedText { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public RichTextDocumentViewportPosition Position => new()
        {
            ChunkSortOrder = ChunkSortOrder,
            BlockIndex = BlockIndex
        };
    }
}

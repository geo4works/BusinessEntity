namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    /// <summary>
    /// Node of a rich-text document table of contents built from persisted chunk properties.
    /// </summary>
    public class RichTextDocumentTableOfContentsEntry
    {
        // Identifier of the chunk that contains the target heading block.
        public Guid ChunkId { get; set; }

        // Sort order of the chunk inside the rich-text document.
        public long ChunkSortOrder { get; set; }

        // Zero-based block index inside the chunk.
        public int BlockIndex { get; set; }

        // Heading level. Only levels 1..3 are currently used for the table of contents.
        public int Level { get; set; }

        // Plain heading title shown in the table of contents.
        public string Title { get; set; } = string.Empty;

        // Stable DOM anchor built from chunk id and block index.
        public string Anchor { get; set; } = string.Empty;

        // Nested heading entries for UI tree rendering.
        public List<RichTextDocumentTableOfContentsEntry> Children { get; set; } = new();
    }
}

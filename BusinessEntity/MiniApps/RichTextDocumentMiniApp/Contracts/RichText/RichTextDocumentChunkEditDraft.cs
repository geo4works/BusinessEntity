namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Draft одного измененного rich-text чанка, пришедший из editor viewport.
    public class RichTextDocumentChunkEditDraft
    {
        public Guid ChunkId { get; set; }

        public long SortOrder { get; set; }

        public string Html { get; set; } = string.Empty;
    }
}

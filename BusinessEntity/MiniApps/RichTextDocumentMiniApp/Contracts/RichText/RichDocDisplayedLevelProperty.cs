namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // User-level preference for the visible depth of one rich-text document outline.
    public sealed class RichDocDisplayedLevelProperty
    {
        public int SchemaVersion { get; set; } = 1;

        public string Kind { get; set; } = "RichDocDisplayedLevelProperty";

        public Guid DocumentId { get; set; }

        public int DisplayLevelCount { get; set; } = 1;
    }
}

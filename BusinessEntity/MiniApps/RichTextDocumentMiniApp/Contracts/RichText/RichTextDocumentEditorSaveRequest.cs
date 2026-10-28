namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    public sealed class RichTextDocumentEditorSaveRequest
    {
        public int SavedVersion { get; set; }

        public bool TitleChanged { get; set; }

        public int SavedChunkCount { get; set; }

        public string Title { get; set; } = string.Empty;

        public string VersionDescription { get; set; } = string.Empty;

        public bool RefreshReadWindow { get; set; }

        public RichTextDocumentViewportPosition? ViewportPosition { get; set; }
    }
}

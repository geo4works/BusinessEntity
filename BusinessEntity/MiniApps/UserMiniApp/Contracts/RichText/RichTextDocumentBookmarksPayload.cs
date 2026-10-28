namespace BusinessEntity.MiniApps.UserMiniApp.Contracts.RichText
{
    // JSON payload пользовательской property RichDocBookmarks.
    public sealed class RichTextDocumentBookmarksPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public string Kind { get; set; } = "RichDocBookmarks";
        public List<RichTextDocumentBookmark> Bookmarks { get; set; } = new();
    }
}

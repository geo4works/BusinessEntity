namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Aggregated chunk metrics for the selected rich-text document version.
    public class RichTextDocumentChunkStatistics
    {
        public int TotalChunkCount { get; set; }

        public double AverageCharCount { get; set; }

        public int MinCharCount { get; set; }

        public int MaxCharCount { get; set; }

        public bool IsEmpty => TotalChunkCount <= 0;
    }
}

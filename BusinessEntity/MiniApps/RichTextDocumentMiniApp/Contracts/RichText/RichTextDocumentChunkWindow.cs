using BusinessEntity.Core.Chunks;
namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Window of rich-text chunks loaded for virtualized document viewing.
    public class RichTextDocumentChunkWindow
    {
        // Фактически выбранная версия фиксирует базу редактора и последующих окон чтения.
        public int DocumentVersion { get; set; }

        public Guid BusinessEntityId { get; set; }

        public long StartSortOrder { get; set; }

        public int TotalChunkCount { get; set; }

        public IReadOnlyList<BusinessEntityDataChunk> Chunks { get; set; } = Array.Empty<BusinessEntityDataChunk>();

        public long EndSortOrder => Chunks.Count == 0 ? StartSortOrder - 1 : Chunks[^1].SortOrder;

        public bool HasPrevious => Chunks.Count > 0 && StartSortOrder > 0;

        public bool HasNext => Chunks.Count > 0 && EndSortOrder < TotalChunkCount - 1;
    }
}

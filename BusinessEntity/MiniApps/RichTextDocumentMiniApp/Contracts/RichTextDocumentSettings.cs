using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts
{
    public class RichTextDocumentSettings
    {
        public int InitialChunkCount { get; set; } = 2;

        public int TableOfContentsBeforeBuffer { get; set; } = 2;

        public int TableOfContentsAfterBuffer { get; set; } = 5;

        public int ScrollPreviousChunkCount { get; set; } = 1;

        public bool HideTableOfContentsScrollbar { get; set; } = true;

        public int EditChunksBeforeFocused { get; set; } = 1;

        public int EditChunksAfterFocused { get; set; } = 1;

        public int EditChunksOnOpen { get; set; } = 2;

        public int GetInitialChunkCount()
        {
            return Math.Max(InitialChunkCount, 1);
        }

        public int GetTableOfContentsBeforeBuffer()
        {
            return Math.Max(TableOfContentsBeforeBuffer, 0);
        }

        public int GetTableOfContentsWindowChunkCount()
        {
            return GetTableOfContentsBeforeBuffer() + 1 + Math.Max(TableOfContentsAfterBuffer, 0);
        }

        public int GetScrollPreviousChunkCount()
        {
            return Math.Max(ScrollPreviousChunkCount, 0);
        }

        public int GetScrollWindowChunkCount()
        {
            return GetScrollPreviousChunkCount() + 1 + Math.Max(TableOfContentsAfterBuffer, 0);
        }

        public int GetEditChunksBeforeFocused()
        {
            return Math.Max(EditChunksBeforeFocused, 0);
        }

        public int GetEditChunksAfterFocused()
        {
            return Math.Max(EditChunksAfterFocused, 0);
        }

        public int GetEditWindowChunkCount()
        {
            return GetEditChunksBeforeFocused() + 1 + GetEditChunksAfterFocused();
        }

        public int GetEditChunksOnOpen()
        {
            return Math.Max(EditChunksOnOpen, 1);
        }
    }
}

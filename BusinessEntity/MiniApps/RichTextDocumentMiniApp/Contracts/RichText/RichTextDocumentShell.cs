using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Lightweight rich-text document metadata without loading body chunks.
    public class RichTextDocumentShell
    {
        public BusinessEntity.Core.Classes.BusinessEntity Entity { get; set; } = new();

        public RichTextDocument Manifest { get; set; } = new();
    }
}

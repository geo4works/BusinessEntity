using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport
{
    // Промежуточный результат import-конвертера.
    // На этом этапе документ уже разложен на блоки и embedded-файлы,
    // но еще не порезан на технические чанки хранения.
    public class RichTextImportContent
    {
        public IReadOnlyList<BusinessEntityDataBlock> Blocks { get; init; } = Array.Empty<BusinessEntityDataBlock>();
        public IReadOnlyList<RichTextEmbeddedFile> Files { get; init; } = Array.Empty<RichTextEmbeddedFile>();
    }
}

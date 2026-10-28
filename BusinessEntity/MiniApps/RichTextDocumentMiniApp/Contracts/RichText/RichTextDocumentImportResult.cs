using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText
{
    // Результат импорта внешнего файла во внутренний rich-text формат.
    public class RichTextDocumentImportResult
    {
        // Manifest, который нужно сохранить в BusinessEntityDataDto.Data.
        public RichTextDocument Manifest { get; set; } = new();

        // Нормализованные чанки документа.
        public IReadOnlyList<BusinessEntityDataChunk> Chunks { get; set; } = Array.Empty<BusinessEntityDataChunk>();

        // Embedded-файлы, которые должны быть сохранены в локальное storage.
        public IReadOnlyList<RichTextEmbeddedFile> Files { get; set; } = Array.Empty<RichTextEmbeddedFile>();
    }
}

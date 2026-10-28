using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;

// Typed payload rich-text документа.
namespace BusinessEntity.Core.DomainEntities
{
    // Хранит manifest rich-text документа.
    // Сам текст документа лежит не здесь, а в технических чанках storage-слоя.
    public class RichTextDocument : BusinessEntityData, IBusinessEntityData, IBusinessEntityPublication
    {
        // Тип business-объекта rich-text документа.
        public override BusinessEntityTypeEnum EntityType { get; set; } = BusinessEntityTypeEnum.RichTextDocument;

        // Rich-text manifest и chunk-body сохраняются версиями.
        public override bool HasVersions => true;

        // Тело rich-text документа хранится текстовыми чанками.
        public override BusinessEntityDataChunkStorageType ChunkStorageType => BusinessEntityDataChunkStorageType.TextChunks;

        // Способ физического хранения содержимого документа.
        public string ContentStorage { get; set; } = "ChunkedBlocks";

        // Логический формат редакторного представления документа.
        public string EditorFormat { get; set; } = "BlockJsonWithInlineHtml";

        // Имя policy, по которой документ режется на чанки.
        public string ChunkPolicy { get; set; } = "RichTextMvpV1";

        // Способ хранения embedded-файлов документа.
        public string EmbeddedFileStorage { get; set; } = "LocalDocumentFiles";

        // Поддержка картинок в текущем manifest-е.
        public bool SupportsImages { get; set; } = true;

        // Проекция указателя из карточки; в manifest истории не сериализуется.
        public int PublishedVersion { get; set; }
    }
}

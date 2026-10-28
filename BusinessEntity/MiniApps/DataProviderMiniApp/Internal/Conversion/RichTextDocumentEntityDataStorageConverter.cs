using System.Text.Json.Serialization;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;

// Конвертер manifest-а rich-text документа.
// Тело документа здесь не хранится: только storage-настройки и признаки документа.
internal sealed class RichTextDocumentEntityDataStorageConverter : EntityDataStorageConverterBase<RichTextDocument>
{
    // Rich-text manifest привязан к типу RichTextDocument.
    public override BusinessEntityTypeEnum SupportedType => BusinessEntityTypeEnum.RichTextDocument;

    // Формирует компактное body manifest-а rich-text документа.
    public override string SerializePayload(RichTextDocument data)
    {
        Validate(data.ContentStorage, data.EditorFormat, data.ChunkPolicy, data.EmbeddedFileStorage);
        return SerializeBody(new RichTextDocumentPayloadBody
        {
            Tag = data.Tag ?? string.Empty,
            ContentStorage = data.ContentStorage ?? "ChunkedBlocks",
            EditorFormat = data.EditorFormat ?? "BlockJsonWithInlineHtml",
            ChunkPolicy = data.ChunkPolicy ?? "RichTextMvpV1",
            EmbeddedFileStorage = data.EmbeddedFileStorage ?? "LocalDocumentFiles",
            SupportsImages = data.SupportsImages
        });
    }

    // Восстанавливает typed manifest из storage-body.
    public override RichTextDocument DeserializePayload(string payloadJson)
    {
        var body = DeserializeBody<RichTextDocumentPayloadBody>(payloadJson);
        Validate(body.ContentStorage, body.EditorFormat, body.ChunkPolicy, body.EmbeddedFileStorage);
        return new RichTextDocument
        {
            Tag = body.Tag,
            ContentStorage = body.ContentStorage,
            EditorFormat = body.EditorFormat,
            ChunkPolicy = body.ChunkPolicy,
            EmbeddedFileStorage = body.EmbeddedFileStorage,
            SupportsImages = body.SupportsImages
        };
    }

    private static void Validate(string storage, string editor, string chunk, string files)
    {
        if (storage != "ChunkedBlocks" || editor != "BlockJsonWithInlineHtml" || chunk != "RichTextMvpV1" || files != "LocalDocumentFiles")
            throw new NotSupportedException("Неизвестный формат статьи.");
    }

    // Внутренний JSON-контракт manifest-а rich-text документа.
    private sealed class RichTextDocumentPayloadBody
    {
        [JsonPropertyName("tag")]
        [JsonRequired]
        public string Tag { get; set; } = string.Empty;

        [JsonPropertyName("contentStorage")]
        [JsonRequired]
        public string ContentStorage { get; set; } = string.Empty;

        [JsonPropertyName("editorFormat")]
        [JsonRequired]
        public string EditorFormat { get; set; } = string.Empty;

        [JsonPropertyName("chunkPolicy")]
        [JsonRequired]
        public string ChunkPolicy { get; set; } = string.Empty;

        [JsonPropertyName("embeddedFileStorage")]
        [JsonRequired]
        public string EmbeddedFileStorage { get; set; } = string.Empty;

        [JsonPropertyName("supportsImages")]
        [JsonRequired]
        public bool SupportsImages { get; set; }
    }
}

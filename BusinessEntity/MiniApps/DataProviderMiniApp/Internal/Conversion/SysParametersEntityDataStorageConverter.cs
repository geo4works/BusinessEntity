using System.Text.Json.Serialization;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;

// Конвертер payload системных параметров: хранит прикладные поля настроек и tag.
internal sealed class SysParametersEntityDataStorageConverter : EntityDataStorageConverterBase<SysParameters>
{
    // Payload системных параметров привязан к типу SysParametersTp.
    public override BusinessEntityTypeEnum SupportedType => BusinessEntityTypeEnum.SysParametersTp;

    // Сериализует полезные поля системных параметров без entity-метаданных.
    public override string SerializePayload(SysParameters data)
    {
        var body = new SysParametersPayloadBody
        {
            CompanyName = data.CompanyName ?? string.Empty,
            RichTextChunkCharLimit = data.RichTextChunkCharLimit,
            RichTextInitialChunkCount = data.RichTextInitialChunkCount,
            RichTextTableOfContentsBeforeBuffer = data.RichTextTableOfContentsBeforeBuffer,
            RichTextTableOfContentsAfterBuffer = data.RichTextTableOfContentsAfterBuffer,
            RichTextScrollPreviousChunkCount = data.RichTextScrollPreviousChunkCount,
            RichTextHideTableOfContentsScrollbar = data.RichTextHideTableOfContentsScrollbar,
            RichTextEditChunksBeforeFocused = data.RichTextEditChunksBeforeFocused,
            RichTextEditChunksAfterFocused = data.RichTextEditChunksAfterFocused,
            RichTextEditChunksOnOpen = data.RichTextEditChunksOnOpen,
            Tag = data.Tag
        };
        Validate(body);
        return SerializeBody(body);
    }

    // Восстанавливает typed payload системных параметров.
    public override SysParameters DeserializePayload(string payloadJson)
    {
        var body = DeserializeBody<SysParametersPayloadBody>(payloadJson);
        Validate(body);
        return new SysParameters
        {
            CompanyName = body.CompanyName,
            RichTextChunkCharLimit = body.RichTextChunkCharLimit,
            RichTextInitialChunkCount = body.RichTextInitialChunkCount,
            RichTextTableOfContentsBeforeBuffer = body.RichTextTableOfContentsBeforeBuffer,
            RichTextTableOfContentsAfterBuffer = body.RichTextTableOfContentsAfterBuffer,
            RichTextScrollPreviousChunkCount = body.RichTextScrollPreviousChunkCount,
            RichTextHideTableOfContentsScrollbar = body.RichTextHideTableOfContentsScrollbar,
            RichTextEditChunksBeforeFocused = body.RichTextEditChunksBeforeFocused,
            RichTextEditChunksAfterFocused = body.RichTextEditChunksAfterFocused,
            RichTextEditChunksOnOpen = body.RichTextEditChunksOnOpen,
            Tag = body.Tag
        };
    }

    // Некорректная запись или новый ввод не заменяются значениями по умолчанию.
    private static void Validate(SysParametersPayloadBody body)
    {
        if (body.RichTextChunkCharLimit < 1 || body.RichTextInitialChunkCount < 1 || body.RichTextEditChunksOnOpen < 1 ||
            body.RichTextTableOfContentsBeforeBuffer < 0 || body.RichTextTableOfContentsAfterBuffer < 0 ||
            body.RichTextScrollPreviousChunkCount < 0 || body.RichTextEditChunksBeforeFocused < 0 || body.RichTextEditChunksAfterFocused < 0)
            throw new InvalidDataException("Повреждены числовые настройки rich-text.");
    }

    // Внутренний storage-контракт payload тела системных параметров.
    private sealed class SysParametersPayloadBody
    {
        [JsonPropertyName("companyName")]
        [JsonRequired]
        public string CompanyName { get; set; } = string.Empty;

        [JsonPropertyName("tag")]
        [JsonRequired]
        public string Tag { get; set; } = string.Empty;

        [JsonPropertyName("richTextChunkCharLimit")]
        [JsonRequired]
        public int RichTextChunkCharLimit { get; set; } = 12000;

        [JsonPropertyName("richTextInitialChunkCount")]
        [JsonRequired]
        public int RichTextInitialChunkCount { get; set; } = 2;

        [JsonPropertyName("richTextTableOfContentsBeforeBuffer")]
        [JsonRequired]
        public int RichTextTableOfContentsBeforeBuffer { get; set; } = 2;

        [JsonPropertyName("richTextTableOfContentsAfterBuffer")]
        [JsonRequired]
        public int RichTextTableOfContentsAfterBuffer { get; set; } = 5;

        [JsonPropertyName("richTextScrollPreviousChunkCount")]
        [JsonRequired]
        public int RichTextScrollPreviousChunkCount { get; set; } = 1;

        [JsonPropertyName("richTextHideTableOfContentsScrollbar")]
        [JsonRequired]
        public bool RichTextHideTableOfContentsScrollbar { get; set; } = true;

        [JsonPropertyName("richTextEditChunksBeforeFocused")]
        [JsonRequired]
        public int RichTextEditChunksBeforeFocused { get; set; } = 1;

        [JsonPropertyName("richTextEditChunksAfterFocused")]
        [JsonRequired]
        public int RichTextEditChunksAfterFocused { get; set; } = 1;

        [JsonPropertyName("richTextEditChunksOnOpen")]
        [JsonRequired]
        public int RichTextEditChunksOnOpen { get; set; } = 2;
    }
}

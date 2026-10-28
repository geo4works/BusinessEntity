using System.Text.Json.Serialization;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;

// Конвертер payload папки: сейчас хранит только tag как формализованное data-представление.
internal sealed class FolderEntityDataStorageConverter : EntityDataStorageConverterBase<Folder>
{
    // Папочный payload привязан к типу Folder.
    public override BusinessEntityTypeEnum SupportedType => BusinessEntityTypeEnum.Folder;

    // Сериализует минимальное payload-тело папки.
    public override string SerializePayload(Folder data)
    {
        return SerializeBody(new SimpleTaggedPayloadBody
        {
            Tag = data.Tag
        });
    }

    // Восстанавливает минимальный typed payload папки.
    public override Folder DeserializePayload(string payloadJson)
    {
        var body = DeserializeBody<SimpleTaggedPayloadBody>(payloadJson);
        return new Folder
        {
            Tag = body.Tag
        };
    }

    // Унифицированное тело payload для простых tagged-объектов.
    private sealed class SimpleTaggedPayloadBody
    {
        [JsonPropertyName("tag")]
        [JsonRequired]
        public string Tag { get; set; } = string.Empty;
    }
}

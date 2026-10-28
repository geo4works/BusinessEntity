using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using System.Text.Json.Serialization;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Internal.Serialization;

// Конвертер payload пространства: сейчас хранит только tag как формализованное data-представление.
internal sealed class SpaceEntityDataStorageConverter : EntityDataStorageConverterBase<Space>
{
    // Пространственный payload привязан к типу Space.
    public override BusinessEntityTypeEnum SupportedType => global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id;
    public override string StorageKind => global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Key;

    // Сериализует минимальное payload-тело пространства.
    public override string SerializePayload(Space data)
    {
        return SerializeBody(new SimpleTaggedPayloadBody
        {
            Tag = data.Tag
        });
    }

    // Восстанавливает минимальный typed payload пространства.
    public override Space DeserializePayload(string payloadJson)
    {
        var body = DeserializeBody<SimpleTaggedPayloadBody>(payloadJson);
        return new Space
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

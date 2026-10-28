using System.Text.Json.Nodes;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Internal;

// Правило перепривязки ссылок в обычном тексте принадлежит владельцу документа.
internal sealed class DocumentArchivePolicy : IEntityArchivePolicy
{
    private readonly EntityUrlRewriter _links;
    public BusinessEntityTypeEnum EntityType => global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id;
    public bool RequiresPayload => true;
    // Получает общий алгоритм и объявления маршрутов через DI.
    public DocumentArchivePolicy(EntityUrlRewriter links) => _links = links;
    // Переписывает только текст тела; метаданные авторства остаются нетронутыми.
    public string RemapPayload(string envelopeJson, GraphRestoreIdentities identities)
    {
        var root = JsonNode.Parse(envelopeJson) ?? throw new InvalidDataException("Пустой документ архива.");
        var payload = root["payload"] ?? throw new InvalidDataException("Отсутствует тело документа архива.");
        var text = payload["text"]?.GetValue<string>() ?? throw new InvalidDataException("Отсутствует текст документа архива.");
        payload["text"] = _links.Rewrite(text, identities.Entities);
        return root.ToJsonString();
    }
    // Обычный документ не объявляет чанков.
    public string RemapChunk(string envelopeJson, GraphRestoreIdentities identities)
        => throw new NotSupportedException("Обычный документ не хранит чанки.");
}

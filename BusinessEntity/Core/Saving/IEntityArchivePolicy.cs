using BusinessEntity.Core.Classes;

namespace BusinessEntity.Core.Saving;

// Владелец типа объявляет, как перепривязать его содержимое при клонировании подграфа.
public interface IEntityArchivePolicy
{
    BusinessEntityTypeEnum EntityType { get; }
    bool RequiresPayload { get; }
    // Служебные авторы и время остаются историческими; меняются только объявленные предметные ссылки.
    string RemapPayload(string envelopeJson, GraphRestoreIdentities identities);
    // Тип без чанков явно отказывает в неожиданном чанковом содержимом.
    string RemapChunk(string envelopeJson, GraphRestoreIdentities identities);
}
// Идентичности разделены по назначению и не угадываются по совпадению JSON-поля.
public sealed record GraphRestoreIdentities(IReadOnlyDictionary<Guid, Guid> Entities,
    IReadOnlyDictionary<Guid, Guid> Chunks, IReadOnlyDictionary<Guid, Guid> Properties,
    IReadOnlyDictionary<Guid, Guid> Comments, IReadOnlyDictionary<Guid, Guid> Relations);
// Явное объявление владельца для типа, чьё тело не содержит предметных ссылок.
public sealed class NoStoredReferencesArchivePolicy : IEntityArchivePolicy
{
    public BusinessEntityTypeEnum EntityType { get; }
    public bool RequiresPayload { get; }
    // Не регистрирует конкретные типы из Core; они приходят от владельцев.
    public NoStoredReferencesArchivePolicy(BusinessEntityTypeEnum type, bool requiresPayload)
    { EntityType = type; RequiresPayload = requiresPayload; }
    // Каноническое тело без ссылок переносится как есть и затем проверяется storage codec.
    public string RemapPayload(string envelopeJson, GraphRestoreIdentities identities) => envelopeJson;
    // Не превращает неожиданный чанк в пустое содержимое.
    public string RemapChunk(string envelopeJson, GraphRestoreIdentities identities)
        => throw new NotSupportedException("Этот тип не объявлял чанковое содержимое.");
}

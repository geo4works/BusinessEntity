using System.Text.Json;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Chunks;

namespace BusinessEntity.Core.Types;

// Начальный payload/chunks одного типа объявляет владелец, не общий helper.
public sealed record EntityInitialContent(BusinessEntityData? Data, IReadOnlyList<BusinessEntityDataChunk>? Chunks = null,
    IReadOnlyList<Guid>? ChunkOrder = null);
// Общий описатель предметного типа: Core не содержит его CLR-класс или ветвление.
public interface IEntityTypeDescriptor
{
    BusinessEntityTypeEnum EntityType { get; }
    string MiniAppId { get; }
    string TypeKey { get; }
    Type DataType { get; }
    bool DefaultIsPublic { get; }
    bool PayloadRequired { get; }
    bool IsSingleton { get; }
    string DefaultNamePrefix { get; }
    string? PageRoute { get; }
    IReadOnlyList<BusinessEntityTypeEnum> AllowedParentTypes { get; }
    // Создаёт начальное состояние своего типа без записи в storage.
    EntityInitialContent CreateInitial(Guid entityId, string name, string? initialContent);
    // Фиксирует независимый payload перед первым await сохранения.
    BusinessEntityData Clone(BusinessEntityData data);
}
// Типизированная декларация владельца переиспользует общую механику копирования без дублирования helper.
public sealed class EntityTypeDescriptor<TData> : IEntityTypeDescriptor where TData : BusinessEntityData, new()
{
    private readonly Func<Guid, string, string?, EntityInitialContent> _create;
    public BusinessEntityTypeEnum EntityType { get; }
    public string MiniAppId { get; }
    public string TypeKey { get; }
    public Type DataType => typeof(TData);
    public bool DefaultIsPublic { get; }
    public bool PayloadRequired { get; }
    public bool IsSingleton { get; }
    public string DefaultNamePrefix { get; }
    public string? PageRoute { get; }
    public IReadOnlyList<BusinessEntityTypeEnum> AllowedParentTypes { get; }
    // Владелец передаёт значения и pure initializer через свою Registration.
    public EntityTypeDescriptor(BusinessEntityTypeEnum entityType, string miniAppId, string typeKey,
        bool defaultIsPublic, bool payloadRequired, string defaultNamePrefix,
        Func<Guid, string, string?, EntityInitialContent> create,
        IReadOnlyList<BusinessEntityTypeEnum>? allowedParents = null, string? pageRoute = null, bool singleton = false)
    {
        EntityType = entityType; MiniAppId = miniAppId; TypeKey = typeKey; DefaultIsPublic = defaultIsPublic;
        PayloadRequired = payloadRequired; DefaultNamePrefix = defaultNamePrefix; _create = create;
        AllowedParentTypes = Array.AsReadOnly(allowedParents?.ToArray() ?? []); PageRoute = pageRoute; IsSingleton = singleton;
    }
    // Pure initializer не получает репозиториев, DI scope или UI.
    public EntityInitialContent CreateInitial(Guid entityId, string name, string? initialContent) => _create(entityId, name, initialContent);
    // Runtime-копия своего типа не является адаптером формата storage.
    public BusinessEntityData Clone(BusinessEntityData data)
    {
        if (data is not TData typed) throw new ArgumentException("Payload не соответствует описателю типа.");
        return JsonSerializer.Deserialize<TData>(JsonSerializer.Serialize(typed))
            ?? throw new InvalidOperationException("Не удалось зафиксировать payload.");
    }
}

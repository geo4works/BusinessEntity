using BusinessEntity.Core.Classes;

namespace BusinessEntity.Core.Types;

// Singleton каталога технических metadata; конкретные типы приходят только от владельцев.
public sealed class EntityTypeRegistry
{
    private readonly IReadOnlyDictionary<BusinessEntityTypeEnum, IEntityTypeDescriptor> _byType;
    private readonly IReadOnlyDictionary<string, IEntityTypeDescriptor> _byKey;
    // Дубликат ID или TypeKey останавливает композицию приложения.
    public EntityTypeRegistry(IEnumerable<IEntityTypeDescriptor> descriptors)
    {
        var items = descriptors.ToArray(); _byType = items.ToDictionary(x => x.EntityType);
        _byKey = items.ToDictionary(x => x.TypeKey, StringComparer.Ordinal);
    }
    public IReadOnlyList<IEntityTypeDescriptor> All => _byType.Values.ToArray();
    // Неизвестный ID не подменяется базовым пустым payload.
    public IEntityTypeDescriptor Get(BusinessEntityTypeEnum type) => _byType.TryGetValue(type, out var descriptor) ? descriptor
        : throw new NotSupportedException($"Описатель типа {(int)type} не зарегистрирован.");
    // Имена UI/архива объявляются владельцем и не зависят от Enum.ToString.
    public IEntityTypeDescriptor Get(string key) => _byKey.TryGetValue(key, out var descriptor) ? descriptor
        : throw new NotSupportedException($"Описатель '{key}' не зарегистрирован.");
}

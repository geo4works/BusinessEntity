using BusinessEntity.Core.Classes;

namespace BusinessEntity.Core.Saving;

// Связывает тип с логическим владельцем; регистрация находится у миниаппа, Core конкретных типов не знает.
public sealed record EntityTypeOwnership(BusinessEntityTypeEnum EntityType, string MiniAppId);

// Общий способ формирования пакета движком, без дублирования хелперов у владельцев.
public sealed class EntitySaveBatchFactory
{
    private readonly IReadOnlyDictionary<BusinessEntityTypeEnum, string> _owners;
    // Проверяет уникальность владельца при построении контейнера.
    public EntitySaveBatchFactory(IEnumerable<EntityTypeOwnership> owners)
        => _owners = owners.ToDictionary(x => x.EntityType, x => x.MiniAppId);
    // Создаёт новый пакет для владельца типа; повторное сохранение использует уже созданный пакет.
    public EntitySaveBatch Create(BusinessEntityTypeEnum type, Guid? editSessionId = null)
        => new(Guid.NewGuid(), _owners.TryGetValue(type, out var owner) ? owner
            : throw new InvalidOperationException($"Владелец типа {type} не объявлен."), editSessionId);
}

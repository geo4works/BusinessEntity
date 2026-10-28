using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;

// Централизованно разрешает payload-конвертеры по entity type и строковому storage-kind.
internal sealed class EntityDataStorageConverterFactory : IEntityDataStorageConverterFactory
{
    // Быстрый индекс по enum-типу сущности.
    private readonly IReadOnlyDictionary<BusinessEntityTypeEnum, IEntityDataStorageConverter> _convertersByType;
    // Быстрый индекс по строковому kind из envelope.
    private readonly IReadOnlyDictionary<string, IEntityDataStorageConverter> _convertersByKind;

    // Собирает индексы конвертеров один раз при создании фабрики.
    public EntityDataStorageConverterFactory(IEnumerable<IEntityDataStorageConverter> converters)
    {
        var converterList = converters.ToList();
        _convertersByType = converterList.ToDictionary(x => x.SupportedType);
        _convertersByKind = converterList.ToDictionary(
            x => x.StorageKind,
            StringComparer.Ordinal);
    }

    // Возвращает обязательный конвертер по enum-типу сущности.
    public IEntityDataStorageConverter GetRequiredConverter(BusinessEntityTypeEnum entityType)
    {
        if (_convertersByType.TryGetValue(entityType, out var converter))
        {
            return converter;
        }

        throw new NotSupportedException($"No entity data storage converter is registered for entity type '{entityType}'.");
    }

    // Возвращает обязательный конвертер по строковому kind из envelope.
    public IEntityDataStorageConverter GetRequiredConverter(string storageKind)
    {
        if (_convertersByKind.TryGetValue(storageKind, out var converter))
        {
            return converter;
        }

        throw new NotSupportedException($"No entity data storage converter is registered for storage kind '{storageKind}'.");
    }
}

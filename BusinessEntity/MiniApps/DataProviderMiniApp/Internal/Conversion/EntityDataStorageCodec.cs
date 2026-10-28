using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;

// Переиспользуемый codec storage-слоя: объединяет factory конвертеров и общий envelope-serializer.
public sealed class EntityDataStorageCodec
{
    // Фабрика typed-конвертеров payload.
    private readonly IEntityDataStorageConverterFactory _converterFactory;

    // Подключает фабрику конвертеров для единообразного read/write пути.
    public EntityDataStorageCodec(IEntityDataStorageConverterFactory converterFactory)
    {
        _converterFactory = converterFactory;
    }

    // Kind сохраняется независимо от CLR namespace и значения Enum.ToString.
    public string GetStorageKind(BusinessEntityTypeEnum type) => _converterFactory.GetRequiredConverter(type).StorageKind;

    // Сериализует typed payload в raw JSON body без envelope.
    public string SerializePayload(IBusinessEntityData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var converter = _converterFactory.GetRequiredConverter(data.EntityType);
        return converter.SerializePayload(data);
    }

    // Десериализует уже извлеченное raw payload-body в typed payload.
    public TData DeserializePayloadBody<TData>(BusinessEntityTypeEnum entityType, string payloadJson)
        where TData : class, IBusinessEntityData
    {
        var converter = _converterFactory.GetRequiredConverter(entityType);
        var data = converter.DeserializePayload(payloadJson);
        if (data is not TData typedData)
        {
            throw new InvalidOperationException(
                $"Storage payload for '{entityType}' cannot be converted to requested type '{typeof(TData).Name}'.");
        }

        return typedData;
    }

    // Десериализует полный stored envelope в typed payload, разрешая конвертер по kind.
    public TData DeserializeEnvelope<TData>(string envelopeJson)
        where TData : class, IBusinessEntityData
    {
        var envelope = DataPayloadEnvelopeSerializer.ReadEnvelope(envelopeJson);
        var converter = _converterFactory.GetRequiredConverter(envelope.Kind);
        var data = converter.DeserializePayload(envelope.PayloadJson);
        if (data is not TData typedData)
        {
            throw new InvalidOperationException(
                $"Storage envelope kind '{envelope.Kind}' cannot be converted to requested type '{typeof(TData).Name}'.");
        }

        if (typedData is BusinessEntityData businessEntityData)
        {
            businessEntityData.CreatedByUserId = envelope.CreatedByUserId;
            businessEntityData.LastModifiedByUserId = envelope.LastModifiedByUserId;
        }

        return typedData;
    }
}

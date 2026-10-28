using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Classes;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;

// Конвертирует typed BusinessEntityData в storage-payload JSON и обратно.
public interface IEntityDataStorageConverter
{
    // Тип business-объекта, для которого предназначен конвертер.
    BusinessEntityTypeEnum SupportedType { get; }

    // Стабильное имя wire-формата объявляет владелец типа.
    string StorageKind { get; }

    // Сериализует typed payload-объект в raw JSON тела payload без envelope.
    string SerializePayload(IBusinessEntityData data);

    // Десериализует raw JSON тела payload в typed payload-объект.
    IBusinessEntityData DeserializePayload(string payloadJson);
}

// Типизированный вариант конвертера для конкретного payload-класса.
public interface IEntityDataStorageConverter<TData> : IEntityDataStorageConverter
    where TData : class, IBusinessEntityData
{
    // Сериализует конкретный typed payload.
    string SerializePayload(TData data);

    // Десериализует payload в конкретный typed класс.
    new TData DeserializePayload(string payloadJson);
}

using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Types;
namespace BusinessEntity.Core.Saving;

// Создаёт пакет движка от владельца, объявленного в едином описателе типа.
public sealed class EntitySaveBatchFactory(EntityTypeRegistry types)
{
    // Повтор операции использует уже захваченный пакет; новый получает новый OperationId.
    public EntitySaveBatch Create(BusinessEntityTypeEnum type, Guid? editSessionId = null)
        => new(Guid.NewGuid(), types.Get(type).MiniAppId, editSessionId);
}

using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;

namespace BusinessEntity.Core.Saving;

// Предметное правило истории регистрируется владельцем типа; DataProvider применяет механизм.
public interface IEntityVersionPolicy
{
    BusinessEntityTypeEnum EntityType { get; }
    bool UsesChunks { get; }
    // Выделяет только содержимое, изменение которого создаёт версию; название добавляет движок.
    string GetContent(IBusinessEntityData data);
    // Объявляет обязательные файловые ссылки всего авторского снимка.
    IReadOnlyList<EntityFileReference> GetFileReferences(Guid entityId, IReadOnlyList<Core.Chunks.BusinessEntityDataChunk> chunks);
}

using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using BusinessEntity.Core.Saving;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Internal;

// Владелец обычного документа определяет содержимое полной авторской версии.
internal sealed class DocumentVersionPolicy : IEntityVersionPolicy
{
    public BusinessEntityTypeEnum EntityType => global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id;
    public bool UsesChunks => false;
    // Текст и название образуют версию; служебные поля не создают её.
    public string GetContent(IBusinessEntityData data) => ((Document)data).Text ?? throw new InvalidDataException("Отсутствует текст документа.");
    // Текст обычного документа не встраивает постоянные файловые объекты.
    public IReadOnlyList<EntityFileReference> GetFileReferences(Guid entityId, IReadOnlyList<Core.Chunks.BusinessEntityDataChunk> chunks) => [];
}

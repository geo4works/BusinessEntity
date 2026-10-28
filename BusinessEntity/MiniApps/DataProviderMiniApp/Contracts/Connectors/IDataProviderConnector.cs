using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.RichText;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors
{
    /// <summary>
    /// Публичный connector mini-app хранения данных.
    /// Нужен сервисам как компактная точка доступа вместо прямых репозиториев.
    /// </summary>
    public interface IDataProviderConnector
    {
        // Структурная проекция одной ревизии; авторизацию выполняет владелец сценария.
        Task<Navigation.GraphProjection> QueryGraphAsync(Navigation.GraphQueryKind kind, Guid? rootId = null,
            string? searchText = null, int take = 100, CancellationToken cancellationToken = default);
        // Названия опубликованных снимков без материализации содержимого.
        Task<IReadOnlyDictionary<Guid, string>> GetPublishedEntityNamesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
        // Статистика кэша и реальных storage-команд DataProvider.
        Task<Navigation.DataProviderCacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default);
        // Экспорт полного снимка подграфа с временными потоковыми источниками файлов.
        Task<Archives.SubgraphExportResult> ExportSubgraphAsync(Archives.SubgraphExportScope scope, CancellationToken cancellationToken = default);
        // Освобождает только свой временный экспорт.
        Task ReleaseExportAsync(Guid exportId, CancellationToken cancellationToken = default);
        // Возвращает только состав версии без тяжёлого содержимого всех чанков.
        Task<IReadOnlyList<Transactions.VersionChunkMembership>> GetVersionChunkMembershipAsync(Guid entityId, int version, CancellationToken cancellationToken = default);
        // Читает комментарии через владельца storage.
        Task<IReadOnlyList<Transactions.StoredEntityComment>> GetStoredCommentsAsync(Guid entityId, CancellationToken cancellationToken = default);
        // Читает свойства объявленного уровня без доступа к generic repositories.
        Task<IReadOnlyList<Transactions.StoredEntityProperty>> GetStoredPropertiesAsync(Core.Saving.EntityPropertyOwner owner, Guid? parentId = null, CancellationToken cancellationToken = default);
        // Читает точное окно выбранной версии либо её конкретные чанки.
        Task<RichTextDocumentChunkWindow> GetVersionChunkWindowAsync(Guid entityId, int version, long start, int count,
            IReadOnlyList<Guid>? chunkIds = null, string? searchText = null, CancellationToken cancellationToken = default);
        // Получает серверный file-result; отсутствие файла является отдельным результатом.
        Task<Transactions.StoredEntityFile?> GetStoredFileAsync(Guid entityId, string fileId, string variant, CancellationToken cancellationToken = default);
        // Атомарно сохраняет весь снимок правок с устойчивым OperationId.
        Task<Transactions.SaveBatchResult> SaveAsync(Core.Saving.EntitySaveBatch batch, CancellationToken cancellationToken = default);
        // Получает подтверждённый исход после таймаута или потери ответа.
        Task<Transactions.StoredOperationOutcome?> GetOperationOutcomeAsync(Guid operationId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<BusinessEntity.Core.Classes.BusinessEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<TData?> GetDataAsync<TData>(Guid id, CancellationToken cancellationToken = default)
            where TData : class, IBusinessEntityData;
        Task<TData?> GetDataVersionAsync<TData>(Guid id, int version, CancellationToken cancellationToken = default)
            where TData : class, IBusinessEntityData;
        Task<IReadOnlyList<BusinessEntityDataVersionInfo>> GetDataVersionsAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BusinessEntityRelation>> GetAllRelationsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BusinessEntityRelation>> GetRelationsAsync(Guid objectAId, Guid objectBId, CancellationToken cancellationToken = default);
        Task<BusinessEntityRelation?> GetRelationByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<RichTextEmbeddedFileContent?> GetRichTextEmbeddedFileAsync(Guid businessEntityId, string imageId, string variant, CancellationToken cancellationToken = default);
    }
}

using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.RichText;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using ReactiveUI;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Connectors
{
    /// <summary>
    /// Connector mini-app хранения данных.
    /// Инкапсулирует только bus-roundtrip для типизированных storage-запросов.
    /// </summary>
    public sealed class DataProviderConnector : IDataProviderConnector
    {
        private readonly BusRequestClient _client;

        private readonly IUserConnector _userConnector;
        private readonly BusRequestScope _scope;

        // Получает общий клиент и доверенный серверный контекст записи.
        public DataProviderConnector(BusRequestClient client, IUserConnector userConnector, BusRequestScope scope)
        {
            _client = client;
            _scope = scope;
            _userConnector = userConnector;
        }

        // Получает целую навигационную проекцию одной ревизии через общий transport.
        public async Task<Contracts.Navigation.GraphProjection> QueryGraphAsync(Contracts.Navigation.GraphQueryKind kind,
            Guid? rootId = null, string? searchText = null, int take = 100, CancellationToken cancellationToken = default)
        {
            var response = await _client.SendAsync<QueryGraphRequest, QueryGraphResponse>(new(Guid.NewGuid(), kind, rootId, searchText, take), cancellationToken);
            return response.Projection ?? throw new BusRequestFailedException(new("bus.invalid_response", "Не получена проекция графа."));
        }

        // Возвращает metadata опубликованного названия вместо чтения тяжёлого payload.
        public async Task<IReadOnlyDictionary<Guid, string>> GetPublishedEntityNamesAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
        {
            var response = await _client.SendAsync<GetPublishedEntityNamesRequest, GetPublishedEntityNamesResponse>(new(Guid.NewGuid(), ids), cancellationToken);
            return response.Names ?? throw new BusRequestFailedException(new("bus.invalid_response", "Не получены названия публикаций."));
        }

        // Публичная диагностика не открывает внутренних cache/EF объектов другим миниаппам.
        public async Task<Contracts.Navigation.DataProviderCacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
        {
            var response = await _client.SendAsync<GetDataProviderCacheStatisticsRequest, GetDataProviderCacheStatisticsResponse>(new(Guid.NewGuid()), cancellationToken);
            return response.Statistics ?? throw new BusRequestFailedException(new("bus.invalid_response", "Не получена статистика кэша."));
        }

        // Большой архив формируется с явным длительным ожиданием; обычные запросы сохраняют короткий timeout.
        public async Task<Contracts.Archives.SubgraphExportResult> ExportSubgraphAsync(Contracts.Archives.SubgraphExportScope scope,
            CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var response = await _client.SendAsync<ExportSubgraphRequest, ExportSubgraphResponse>(new(Guid.NewGuid(), scope), cancellationToken, TimeSpan.FromMinutes(30));
            return response.Result ?? throw new BusRequestFailedException(new("bus.invalid_response", "Не получен экспорт."));
        }

        // Владелец storage проверяет адрес автора перед очисткой lease.
        public async Task ReleaseExportAsync(Guid exportId, CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            await _client.SendAsync<ReleaseExportRequest, ReleaseExportResponse>(new(Guid.NewGuid(), exportId), cancellationToken);
        }

        // Получает лёгкий полный порядок конкретного снимка.
        public async Task<IReadOnlyList<Contracts.Transactions.VersionChunkMembership>> GetVersionChunkMembershipAsync(Guid entityId, int version,
            CancellationToken cancellationToken = default)
        {
            var response = await _client.SendAsync<GetVersionChunkMembershipRequest, GetVersionChunkMembershipResponse>(
                new(Guid.NewGuid(), entityId, version), cancellationToken);
            return response.Records ?? throw new BusRequestFailedException(new("bus.invalid_response", "Не получен состав версии."));
        }

        // Запрашивает read-проекцию отдельной activity-таблицы.
        public async Task<IReadOnlyList<Contracts.Transactions.StoredEntityComment>> GetStoredCommentsAsync(Guid entityId,
            CancellationToken cancellationToken = default)
        {
            var response = await _client.SendAsync<GetStoredCommentsRequest, GetStoredCommentsResponse>(new(Guid.NewGuid(), entityId), cancellationToken);
            return response.Records ?? throw new BusRequestFailedException(new("bus.invalid_response", "Не получен список комментариев."));
        }

        // Запрашивает свойства нужного уровня, сохраняя законное отсутствие коллекции как пустой список.
        public async Task<IReadOnlyList<Contracts.Transactions.StoredEntityProperty>> GetStoredPropertiesAsync(Core.Saving.EntityPropertyOwner owner,
            Guid? parentId = null, CancellationToken cancellationToken = default)
        {
            var response = await _client.SendAsync<GetStoredPropertiesRequest, GetStoredPropertiesResponse>(new(Guid.NewGuid(), owner, parentId), cancellationToken);
            return response.Records ?? throw new BusRequestFailedException(new("bus.invalid_response", "Не получен список свойств."));
        }

        // Получает состав и порядок из references именно выбранной версии.
        public async Task<RichTextDocumentChunkWindow> GetVersionChunkWindowAsync(Guid entityId, int version, long start, int count,
            IReadOnlyList<Guid>? chunkIds = null, string? searchText = null, CancellationToken cancellationToken = default)
        {
            var response = await _client.SendAsync<GetVersionChunkWindowRequest, GetVersionChunkWindowResponse>(
                new(Guid.NewGuid(), entityId, version, start, count, chunkIds, searchText), cancellationToken);
            return response.Window ?? throw new BusRequestFailedException(new("bus.invalid_response", "Не получено окно содержимого."));
        }

        // Передаёт безопасно вычисленный DataProvider путь тонкому HTTP-адаптеру.
        public async Task<Contracts.Transactions.StoredEntityFile?> GetStoredFileAsync(Guid entityId, string fileId, string variant,
            CancellationToken cancellationToken = default)
        {
            var response = await _client.SendAsync<GetStoredFileRequest, GetStoredFileResponse>(new(Guid.NewGuid(), entityId, fileId, variant), cancellationToken);
            return response.File;
        }

        // Передаёт весь набор через единый типизированный маршрут транзакций.
        public async Task<Contracts.Transactions.SaveBatchResult> SaveAsync(Core.Saving.EntitySaveBatch batch,
            CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var response = await _client.SendAsync<SaveEntityBatchRequest, SaveEntityBatchResponse>(
                new(Guid.NewGuid(), batch), cancellationToken, batch.Files.Count > 0 || batch.ImportSnapshot != null ? TimeSpan.FromMinutes(30) : null);
            return response.Result ?? throw new BusRequestFailedException(new("bus.invalid_response", "Не получен результат сохранения."));
        }

        // Выясняет исход уже отправленного пакета, не создавая новый OperationId.
        public async Task<Contracts.Transactions.StoredOperationOutcome?> GetOperationOutcomeAsync(Guid operationId,
            CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var response = await _client.SendAsync<GetOperationOutcomeRequest, GetOperationOutcomeResponse>(
                new(Guid.NewGuid(), operationId), cancellationToken);
            return response.Outcome;
        }

        // Запрашивает полный список бизнес-сущностей через bus.
        public async Task<IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetBusinessEntitiesRequest, GetBusinessEntitiesResponse>(
                new GetBusinessEntitiesRequest(requestId), cancellationToken);

            return response.Records;
        }

        // Запрашивает одну бизнес-сущность по id через bus.
        public async Task<BusinessEntity.Core.Classes.BusinessEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetBusinessEntityByIdRequest, GetBusinessEntityByIdResponse>(
                new GetBusinessEntityByIdRequest(requestId, id), cancellationToken);

            return response.Record;
        }

        // Получает типизированный payload без доступа к кодеку хранения.

        public async Task<TData?> GetDataAsync<TData>(Guid id, CancellationToken cancellationToken = default)
            where TData : class, IBusinessEntityData
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetBusinessEntityDataRequest, GetBusinessEntityDataResponse>(
                new GetBusinessEntityDataRequest(requestId, id), cancellationToken);
            if (response.Record is null) return null;
            return response.Record as TData ?? throw new BusRequestFailedException(
                new BusError("payload.type_mismatch", "Тип данных не соответствует запрошенному контракту."));
        }


        // Получает типизированный payload конкретной версии из DataProvider.

        public async Task<TData?> GetDataVersionAsync<TData>(Guid id, int version, CancellationToken cancellationToken = default)
            where TData : class, IBusinessEntityData
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetBusinessEntityDataVersionRequest, GetBusinessEntityDataVersionResponse>(
                new GetBusinessEntityDataVersionRequest(requestId, id, version), cancellationToken);
            if (response.Record is null) return null;
            return response.Record as TData ?? throw new BusRequestFailedException(
                new BusError("payload.type_mismatch", "Тип данных не соответствует запрошенному контракту."));
        }


        // Возвращает список версий payload-записей из BusinessEntityDataItems.
        public async Task<IReadOnlyList<BusinessEntityDataVersionInfo>> GetDataVersionsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetBusinessEntityDataVersionsRequest, GetBusinessEntityDataVersionsResponse>(
                new GetBusinessEntityDataVersionsRequest(requestId, id), cancellationToken);

            return response.Records;
        }


        private async Task<Guid?> ResolveCurrentUserIdAsync(CancellationToken cancellationToken)
        {
            if (_scope.UserId.HasValue) return _scope.UserId;
            if (_scope.IsSystem) return null;
            var user = await _userConnector.EnsureCurrentUserAsync(cancellationToken);
            return user?.Id ?? throw new UnauthorizedAccessException("Для изменения данных нужно войти.");
        }


        // Запрашивает полный список связей между сущностями.
        public async Task<IReadOnlyList<BusinessEntityRelation>> GetAllRelationsAsync(CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetAllRelationsRequest, GetAllRelationsResponse>(
                new GetAllRelationsRequest(requestId), cancellationToken);

            return response.Records;
        }

        // Запрашивает связи между двумя бизнес-объектами.
        public async Task<IReadOnlyList<BusinessEntityRelation>> GetRelationsAsync(Guid objectAId, Guid objectBId, CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetRelationsRequest, GetRelationsResponse>(
                new GetRelationsRequest(requestId, objectAId, objectBId), cancellationToken);

            return response.Records;
        }

        // Запрашивает одну связь по её идентификатору.
        public async Task<BusinessEntityRelation?> GetRelationByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetRelationByIdRequest, GetRelationByIdResponse>(
                new GetRelationByIdRequest(requestId, id), cancellationToken);

            return response.Record;
        }


        // Читает embedded-файл rich-text документа.
        public async Task<RichTextEmbeddedFileContent?> GetRichTextEmbeddedFileAsync(
            Guid businessEntityId,
            string imageId,
            string variant,
            CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetRichTextEmbeddedFileRequest, GetRichTextEmbeddedFileResponse>(
                new GetRichTextEmbeddedFileRequest(requestId, businessEntityId, imageId, variant), cancellationToken);

            return response.Record;
        }


        // Выполняет общий request/response цикл поверх IMessageBus.




    }
}

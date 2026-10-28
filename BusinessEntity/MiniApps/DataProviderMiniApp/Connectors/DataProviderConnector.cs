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

        /// <summary>
        /// Инициализирует connector и гарантирует материализацию mini-app перед первым запросом.
        /// </summary>
        // Получает общий клиент и доверенный серверный контекст записи.
        public DataProviderConnector(BusRequestClient client, IUserConnector userConnector, BusRequestScope scope)
        {
            _client = client;
            _scope = scope;
            _userConnector = userConnector;
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

        // Обновляет человекочитаемое описание конкретной версии payload.
        public async Task UpdateDataVersionDescriptionAsync(
            Guid id,
            int version,
            string? versionDescription,
            CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<UpdateBusinessEntityDataVersionDescriptionRequest, UpdateBusinessEntityDataVersionDescriptionResponse>(
                new UpdateBusinessEntityDataVersionDescriptionRequest(requestId, id, version, versionDescription), cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to update version description for business entityData '{id}' version '{version}'.");
            }
        }

        // Сериализует payload в raw JSON и отправляет команду на сохранение envelope.
        public async Task UpdateDataAsync<TData>(Guid id, TData data, CancellationToken cancellationToken = default)
            where TData : class, IBusinessEntityData
        {
            var currentUserId = await ResolveCurrentUserIdAsync(cancellationToken);
            if (currentUserId.HasValue && data is BusinessEntityData businessEntityData)
            {
                var entity = await GetByIdAsync(id, cancellationToken);
                businessEntityData.CreatedByUserId ??= entity?.CreatedByUserId ?? currentUserId;
                businessEntityData.LastModifiedByUserId = currentUserId;
            }

            var requestId = Guid.NewGuid();

            var response = await _client.SendAsync<UpdateBusinessEntityDataRequest, UpdateBusinessEntityDataResponse>(
                new UpdateBusinessEntityDataRequest(requestId, id, data, data.HasVersions, currentUserId), cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to update data for business entityData '{id}'.");
            }
        }

        // Сохраняет payload в текущую storage-запись без создания новой версии.
        public async Task UpdateDataInPlaceAsync<TData>(Guid id, TData data, CancellationToken cancellationToken = default)
            where TData : class, IBusinessEntityData
        {
            var currentUserId = await ResolveCurrentUserIdAsync(cancellationToken);
            if (currentUserId.HasValue && data is BusinessEntityData businessEntityData)
            {
                var entity = await GetByIdAsync(id, cancellationToken);
                businessEntityData.CreatedByUserId ??= entity?.CreatedByUserId ?? currentUserId;
                businessEntityData.LastModifiedByUserId = currentUserId;
            }

            var requestId = Guid.NewGuid();

            var response = await _client.SendAsync<UpdateBusinessEntityDataRequest, UpdateBusinessEntityDataResponse>(
                new UpdateBusinessEntityDataRequest(requestId, id, data, HasVersions: false, LastModifiedByUserId: currentUserId), cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to update data in-place for business entityData '{id}'.");
            }
        }

        // Отправляет команду на создание бизнес-сущности.
        public async Task<BusinessEntity.Core.Classes.BusinessEntity> AddAsync(BusinessEntity.Core.Classes.BusinessEntity entityData, CancellationToken cancellationToken = default)
        {
            var currentUserId = await ResolveCurrentUserIdAsync(cancellationToken);
            if (currentUserId.HasValue)
            {
                entityData.CreatedByUserId ??= currentUserId;
                entityData.LastModifiedByUserId = currentUserId;
            }

            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<AddBusinessEntityRequest, AddBusinessEntityResponse>(
                new AddBusinessEntityRequest(requestId, entityData), cancellationToken);

            return response.Record ?? throw new InvalidOperationException("DataProvider returned null business entityData after AddAsync.");
        }

        // Отправляет команду на обновление бизнес-сущности.
        public async Task UpdateAsync(BusinessEntity.Core.Classes.BusinessEntity entityData, CancellationToken cancellationToken = default)
        {
            var currentUserId = await ResolveCurrentUserIdAsync(cancellationToken);
            if (currentUserId.HasValue)
            {
                entityData.LastModifiedByUserId = currentUserId;
            }

            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<UpdateBusinessEntityRequest, UpdateBusinessEntityResponse>(
                new UpdateBusinessEntityRequest(requestId, entityData), cancellationToken);


            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to update business entityData '{entityData.Id}'.");
            }
        }

        private async Task<Guid?> ResolveCurrentUserIdAsync(CancellationToken cancellationToken)
        {
            if (_scope.UserId.HasValue) return _scope.UserId;
            if (_scope.IsSystem) return null;
            var user = await _userConnector.EnsureCurrentUserAsync(cancellationToken);
            return user?.Id ?? throw new UnauthorizedAccessException("Для изменения данных нужно войти.");
        }




        // Отправляет команду на удаление бизнес-сущности.
        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<DeleteBusinessEntityRequest, DeleteBusinessEntityResponse>(
                new DeleteBusinessEntityRequest(requestId, id), cancellationToken);


            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to delete business entityData '{id}'.");
            }
        }

        // Отправляет debug-команду на полную очистку DTO-хранилища mini-app.
        public async Task ClearAllAsync(CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<ClearDataProviderStorageRequest, ClearDataProviderStorageResponse>(
                new ClearDataProviderStorageRequest(requestId), cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException("DataProvider failed to clear storage.");
            }
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

        // Отправляет команду на создание связи.
        public async Task<BusinessEntityRelation> CreateRelationAsync(BusinessEntityRelation relation, CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<CreateRelationRequest, CreateRelationResponse>(
                new CreateRelationRequest(requestId, relation), cancellationToken);

            return response.Record ?? throw new InvalidOperationException("DataProvider returned null relation after CreateRelationAsync.");
        }

        // Отправляет команду на обновление связи.
        public async Task UpdateRelationAsync(BusinessEntityRelation relation, CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<UpdateRelationRequest, UpdateRelationResponse>(
                new UpdateRelationRequest(requestId, relation), cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to update relation '{relation.Id}'.");
            }
        }

        // Отправляет команду на удаление связи.
        public async Task DeleteRelationAsync(Guid id, CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<DeleteRelationRequest, DeleteRelationResponse>(
                new DeleteRelationRequest(requestId, id), cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to delete relation '{id}'.");
            }
        }

        // Возвращает весь chunk-набор rich-text документа.
        public async Task<IReadOnlyList<RichTextDocumentChunk>> GetRichTextChunksAsync(Guid businessEntityId, CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetRichTextChunksRequest, GetRichTextChunksResponse>(
                new GetRichTextChunksRequest(requestId, businessEntityId), cancellationToken);

            return response.Records;
        }

        // Запрашивает сохранённые entries оглавления rich-text документа.
        public async Task<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> GetRichTextTableOfContentsEntriesAsync(Guid businessEntityId, CancellationToken cancellationToken = default)
        {
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<GetRichTextTableOfContentsEntriesRequest, GetRichTextTableOfContentsEntriesResponse>(
                new GetRichTextTableOfContentsEntriesRequest(requestId, businessEntityId), cancellationToken);

            return response.Records;
        }

        // Пересоздаёт сохранённые entries оглавления rich-text документа и возвращает свежий набор.
        public async Task<IReadOnlyList<RichTextDocumentTableOfContentsEntry>> RebuildRichTextTableOfContentsEntriesAsync(Guid businessEntityId, CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<RebuildRichTextTableOfContentsEntriesRequest, RebuildRichTextTableOfContentsEntriesResponse>(
                new RebuildRichTextTableOfContentsEntriesRequest(requestId, businessEntityId), cancellationToken);

            return response.Records;
        }

        // Полностью заменяет chunk-набор rich-text документа.
        public async Task ReplaceRichTextChunksAsync(Guid businessEntityId, IReadOnlyList<RichTextDocumentChunk> chunks, CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<ReplaceRichTextChunksRequest, ReplaceRichTextChunksResponse>(
                new ReplaceRichTextChunksRequest(requestId, businessEntityId, chunks), cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to replace rich-text chunks for business entity '{businessEntityId}'.");
            }
        }

        // Сохраняет embedded-файлы rich-text документа.
        public async Task SaveRichTextEmbeddedFilesAsync(
            Guid businessEntityId,
            IReadOnlyList<RichTextEmbeddedFile> files,
            bool replaceExistingFiles,
            CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<SaveRichTextEmbeddedFilesRequest, SaveRichTextEmbeddedFilesResponse>(
                new SaveRichTextEmbeddedFilesRequest(requestId, businessEntityId, files, replaceExistingFiles), cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to save rich-text embedded files for business entity '{businessEntityId}'.");
            }
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

        // Полностью удаляет техническое rich-text storage документа.
        public async Task DeleteRichTextStorageAsync(Guid businessEntityId, CancellationToken cancellationToken = default)
        {
            await ResolveCurrentUserIdAsync(cancellationToken);
            var requestId = Guid.NewGuid();
            var response = await _client.SendAsync<DeleteRichTextStorageRequest, DeleteRichTextStorageResponse>(
                new DeleteRichTextStorageRequest(requestId, businessEntityId), cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"DataProvider failed to delete rich-text storage for business entity '{businessEntityId}'.");
            }
        }

        // Выполняет общий request/response цикл поверх IMessageBus.




    }
}

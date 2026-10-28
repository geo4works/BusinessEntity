using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts;
using ReactiveUI;
using BusinessEntity.Core.Classes;
using BusinessEntity.WebLogger.Services;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal
{
    /// <summary>
    /// Централизованно подписывает mini-app на bus-запросы к хранилищу и публикует ответы.
    /// </summary>
    internal sealed class DataProviderMessageHandler : IDisposable, IBusEndpoint
    {
        private readonly IMessageBus _messageBus;
        private readonly EntityDataStorageCodec _codec;
        private readonly IDataProviderCrudService _dataProviderService;
        private readonly ILogger<DataProviderMessageHandler> _logger;
        private readonly IWebLoggerService? _webLogger;
        private readonly List<IDisposable> _subscriptions = new();
        private readonly object _subscriptionGate = new();

        public DataProviderMessageHandler(
            IMessageBus messageBus,
            IDataProviderCrudService dataProviderService,
            ILogger<DataProviderMessageHandler> logger,
            IWebLoggerService? webLogger, EntityDataStorageCodec codec)
        {
            _messageBus = messageBus;
            _codec = codec;
            _dataProviderService = dataProviderService;
            _logger = logger;
            _webLogger = webLogger;
        }

        // Один раз подписывает mini-app на все входящие bus-сообщения.
        public void EnsureSubscribed()
        {
            lock (_subscriptionGate)
            {
            if (_subscriptions.Count > 0)
            {
                return;
            }

            SubscribeBusinessEntityMessages();
            SubscribeRelationMessages();
            SubscribeBusinessEntityDataMessages();
            SubscribeRichTextMessages();

            _logger.LogInformation("DataProviderMiniApp subscribed to storage messages.");
            }
        }

        // Подписывает сообщения, которые работают с BusinessEntityData.
        private void SubscribeBusinessEntityMessages()
        {
            _subscriptions.Add(_messageBus.Listen<GetBusinessEntitiesRequest>().Subscribe(request => _ = HandleGetBusinessEntitiesAsync(request)));
            _subscriptions.Add(_messageBus.Listen<GetBusinessEntityByIdRequest>().Subscribe(request => _ = HandleGetBusinessEntityByIdAsync(request)));
            _subscriptions.Add(_messageBus.Listen<AddBusinessEntityRequest>().Subscribe(request => _ = HandleAddBusinessEntityAsync(request)));
            _subscriptions.Add(_messageBus.Listen<UpdateBusinessEntityRequest>().Subscribe(request => _ = HandleUpdateBusinessEntityAsync(request)));
            _subscriptions.Add(_messageBus.Listen<DeleteBusinessEntityRequest>().Subscribe(request => _ = HandleDeleteBusinessEntityAsync(request)));
            _subscriptions.Add(_messageBus.Listen<ClearDataProviderStorageRequest>().Subscribe(request => _ = HandleClearStorageAsync(request)));
        }

        // Подписывает сообщения, которые работают с BusinessEntityRelation.
        private void SubscribeRelationMessages()
        {
            _subscriptions.Add(_messageBus.Listen<GetAllRelationsRequest>().Subscribe(request => _ = HandleGetAllRelationsAsync(request)));
            _subscriptions.Add(_messageBus.Listen<GetRelationsRequest>().Subscribe(request => _ = HandleGetRelationsAsync(request)));
            _subscriptions.Add(_messageBus.Listen<GetRelationByIdRequest>().Subscribe(request => _ = HandleGetRelationByIdAsync(request)));
            _subscriptions.Add(_messageBus.Listen<CreateRelationRequest>().Subscribe(request => _ = HandleCreateRelationAsync(request)));
            _subscriptions.Add(_messageBus.Listen<UpdateRelationRequest>().Subscribe(request => _ = HandleUpdateRelationAsync(request)));
            _subscriptions.Add(_messageBus.Listen<DeleteRelationRequest>().Subscribe(request => _ = HandleDeleteRelationAsync(request)));
        }

        // Подписывает сообщения, которые работают с BusinessEntityData.
        private void SubscribeBusinessEntityDataMessages()
        {
            _subscriptions.Add(_messageBus.Listen<GetBusinessEntityDataRequest>().Subscribe(request => _ = HandleGetDataAsync(request)));
            _subscriptions.Add(_messageBus.Listen<GetBusinessEntityDataVersionRequest>().Subscribe(request => _ = HandleGetDataVersionAsync(request)));
            _subscriptions.Add(_messageBus.Listen<GetBusinessEntityDataVersionsRequest>().Subscribe(request => _ = HandleGetDataVersionsAsync(request)));
            _subscriptions.Add(_messageBus.Listen<UpdateBusinessEntityDataVersionDescriptionRequest>().Subscribe(request => _ = HandleUpdateVersionDescriptionAsync(request)));
            _subscriptions.Add(_messageBus.Listen<UpdateBusinessEntityDataRequest>().Subscribe(request => _ = HandleUpdateDataAsync(request)));
        }

        // Подписывает сообщения технического rich-text storage.
        private void SubscribeRichTextMessages()
        {
            _subscriptions.Add(_messageBus.Listen<GetRichTextChunksRequest>().Subscribe(request => _ = HandleGetRichTextChunksAsync(request)));
            _subscriptions.Add(_messageBus.Listen<GetRichTextTableOfContentsEntriesRequest>().Subscribe(request => _ = HandleGetRichTextTableOfContentsEntriesAsync(request)));
            _subscriptions.Add(_messageBus.Listen<RebuildRichTextTableOfContentsEntriesRequest>().Subscribe(request => _ = HandleRebuildRichTextTableOfContentsEntriesAsync(request)));
            _subscriptions.Add(_messageBus.Listen<ReplaceRichTextChunksRequest>().Subscribe(request => _ = HandleReplaceRichTextChunksAsync(request)));
            _subscriptions.Add(_messageBus.Listen<SaveRichTextEmbeddedFilesRequest>().Subscribe(request => _ = HandleSaveRichTextEmbeddedFilesAsync(request)));
            _subscriptions.Add(_messageBus.Listen<GetRichTextEmbeddedFileRequest>().Subscribe(request => _ = HandleGetRichTextEmbeddedFileAsync(request)));
            _subscriptions.Add(_messageBus.Listen<DeleteRichTextStorageRequest>().Subscribe(request => _ = HandleDeleteRichTextStorageAsync(request)));
        }

        // Обрабатывает запрос на чтение всех бизнес-сущностей.
        private async Task HandleGetBusinessEntitiesAsync(GetBusinessEntitiesRequest request)
        {
            try
            {
                var records = await _dataProviderService.GetAllAsync();
                Reply(request, new GetBusinessEntitiesResponse(request.RequestId, records));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load business entities.");
                Reply(request, new GetBusinessEntitiesResponse(request.RequestId, Array.Empty<BusinessEntity.Core.Classes.BusinessEntity>(), BusError.FromException(ex)));
            }
        }

        // Обрабатывает запрос на чтение одной сущности по id.
        private async Task HandleGetBusinessEntityByIdAsync(GetBusinessEntityByIdRequest request)
        {
            try
            {
                var record = await _dataProviderService.GetByIdAsync(request.Id);
                Reply(request, new GetBusinessEntityByIdResponse(request.RequestId, record));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load business entityData {RecordId}.", request.Id);
                Reply(request, new GetBusinessEntityByIdResponse(request.RequestId, null, BusError.FromException(ex)));
            }
        }

        // Обрабатывает запрос на чтение JSON-envelope payload сущности.

        private async Task HandleGetDataAsync(GetBusinessEntityDataRequest request)
        {
            try
            {
                var data = await ReadPayloadAsync(request.BusinessEntityId, null);
                Reply(request, new GetBusinessEntityDataResponse(request.RequestId, data));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to read payload {RequestId}", request.RequestId);
                Reply(request, new GetBusinessEntityDataResponse(request.RequestId, null, BusError.FromException(exception)));
            }
        }


        // Обрабатывает запрос на чтение JSON-envelope payload конкретной версии сущности.

        private async Task HandleGetDataVersionAsync(GetBusinessEntityDataVersionRequest request)
        {
            try
            {
                var data = await ReadPayloadAsync(request.BusinessEntityId, request.Version);
                Reply(request, new GetBusinessEntityDataVersionResponse(request.RequestId, data));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to read payload {RequestId}", request.RequestId);
                Reply(request, new GetBusinessEntityDataVersionResponse(request.RequestId, null, BusError.FromException(exception)));
            }
        }


        // Обрабатывает запрос на чтение списка версий payload сущности.
        private async Task HandleGetDataVersionsAsync(GetBusinessEntityDataVersionsRequest request)
        {
            try
            {
                var records = await _dataProviderService.GetDataVersionsAsync(request.BusinessEntityId);
                Reply(request, new GetBusinessEntityDataVersionsResponse(request.RequestId, records));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load business entityData versions {RecordId}.", request.BusinessEntityId);
                Reply(request, new GetBusinessEntityDataVersionsResponse(request.RequestId, Array.Empty<BusinessEntityDataVersionInfo>(), BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на изменение описания версии payload сущности.
        private async Task HandleUpdateVersionDescriptionAsync(UpdateBusinessEntityDataVersionDescriptionRequest request)
        {
            try
            {
                await _dataProviderService.UpdateDataVersionDescriptionAsync(
                    request.BusinessEntityId,
                    request.Version,
                    request.VersionDescription);
                Reply(request, new UpdateBusinessEntityDataVersionDescriptionResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update business entityData version description {RecordId} version {Version}.", request.BusinessEntityId, request.Version);
                Reply(request, new UpdateBusinessEntityDataVersionDescriptionResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на создание бизнес-сущности.
        private async Task HandleAddBusinessEntityAsync(AddBusinessEntityRequest request)
        {
            try
            {
                _webLogger?.Information($"[мини-апп:data-provider] [bus:received] [entity:add] Получено AddBusinessEntityRequest requestId={request.RequestId} entityId={request.Record.Id} type={request.Record.EntityType} name='{request.Record.Name}'");
                var record = await _dataProviderService.AddAsync(request.Record);
                Reply(request, new AddBusinessEntityResponse(request.RequestId, record));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add business entityData.");
                Reply(request, new AddBusinessEntityResponse(request.RequestId, null, BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на обновление бизнес-сущности.
        private async Task HandleUpdateBusinessEntityAsync(UpdateBusinessEntityRequest request)
        {
            try
            {
                await _dataProviderService.UpdateAsync(request.Record);
                Reply(request, new UpdateBusinessEntityResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update business entityData {RecordId}.", request.Record.Id);
                Reply(request, new UpdateBusinessEntityResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на удаление бизнес-сущности.
        private async Task HandleDeleteBusinessEntityAsync(DeleteBusinessEntityRequest request)
        {
            try
            {
                await _dataProviderService.DeleteAsync(request.Id);
                Reply(request, new DeleteBusinessEntityResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete business entityData {RecordId}.", request.Id);
                Reply(request, new DeleteBusinessEntityResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Обрабатывает debug-команду на полную очистку DTO-хранилища mini-app.
        private async Task HandleClearStorageAsync(ClearDataProviderStorageRequest request)
        {
            try
            {
                await _dataProviderService.ClearAllAsync();
                Reply(request, new ClearDataProviderStorageResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear DataProvider storage.");
                Reply(request, new ClearDataProviderStorageResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на сохранение JSON payload сущности.
        private async Task HandleUpdateDataAsync(UpdateBusinessEntityDataRequest request)
        {
            try
            {
                await _dataProviderService.UpdateDataPayloadAsync(
                    request.BusinessEntityId,
                    _codec.SerializePayload(request.Data),
                    request.HasVersions,
                    lastModifiedByUserId: request.LastModifiedByUserId);
                Reply(request, new UpdateBusinessEntityDataResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update business entityData data {RecordId}.", request.BusinessEntityId);
                Reply(request, new UpdateBusinessEntityDataResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Обрабатывает запрос на чтение всех связей.
        private async Task HandleGetAllRelationsAsync(GetAllRelationsRequest request)
        {
            try
            {
                var records = await _dataProviderService.GetAllRelationsAsync();
                Reply(request, new GetAllRelationsResponse(request.RequestId, records));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load relations.");
                Reply(request, new GetAllRelationsResponse(request.RequestId, Array.Empty<BusinessEntityRelation>(), BusError.FromException(ex)));
            }
        }

        // Обрабатывает запрос на чтение связей между двумя сущностями.
        private async Task HandleGetRelationsAsync(GetRelationsRequest request)
        {
            try
            {
                var records = await _dataProviderService.GetRelationsAsync(request.ObjectAId, request.ObjectBId);
                Reply(request, new GetRelationsResponse(request.RequestId, records));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load relations between {ObjectAId} and {ObjectBId}.", request.ObjectAId, request.ObjectBId);
                Reply(request, new GetRelationsResponse(request.RequestId, Array.Empty<BusinessEntityRelation>(), BusError.FromException(ex)));
            }
        }

        // Обрабатывает запрос на чтение одной связи по id.
        private async Task HandleGetRelationByIdAsync(GetRelationByIdRequest request)
        {
            try
            {
                var record = await _dataProviderService.GetRelationByIdAsync(request.Id);
                Reply(request, new GetRelationByIdResponse(request.RequestId, record));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load relation {RecordId}.", request.Id);
                Reply(request, new GetRelationByIdResponse(request.RequestId, null, BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на создание связи.
        private async Task HandleCreateRelationAsync(CreateRelationRequest request)
        {
            try
            {
                // _webLogger?.Information($"[мини-апп:data-provider] [bus:received] [relation:add] Получено CreateRelationRequest requestId={request.RequestId} relationId={request.Record.Id} objectA={request.Record.ObjectAId} objectB={request.Record.ObjectBId} type={request.Record.RelationType}");
                var record = await _dataProviderService.CreateRelationAsync(request.Record);
                Reply(request, new CreateRelationResponse(request.RequestId, record));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add relation.");
                Reply(request, new CreateRelationResponse(request.RequestId, null, BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на обновление связи.
        private async Task HandleUpdateRelationAsync(UpdateRelationRequest request)
        {
            try
            {
                await _dataProviderService.UpdateRelationAsync(request.Record);
                Reply(request, new UpdateRelationResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update relation {RecordId}.", request.Record.Id);
                Reply(request, new UpdateRelationResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на удаление связи.
        private async Task HandleDeleteRelationAsync(DeleteRelationRequest request)
        {
            try
            {
                await _dataProviderService.DeleteRelationAsync(request.Id);
                Reply(request, new DeleteRelationResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete relation {RecordId}.", request.Id);
                Reply(request, new DeleteRelationResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Обрабатывает запрос на чтение всех rich-text чанков документа.
        private async Task HandleGetRichTextChunksAsync(GetRichTextChunksRequest request)
        {
            try
            {
                var records = await _dataProviderService.GetRichTextChunksAsync(request.BusinessEntityId);
                Reply(request, new GetRichTextChunksResponse(request.RequestId, records));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load rich-text chunks for document {RecordId}.", request.BusinessEntityId);
                Reply(request, new GetRichTextChunksResponse(request.RequestId, Array.Empty<BusinessEntity.Core.RichText.RichTextDocumentChunk>(), BusError.FromException(ex)));
            }
        }

        // Обрабатывает запрос на чтение сохранённых entries оглавления rich-text документа.
        private async Task HandleGetRichTextTableOfContentsEntriesAsync(GetRichTextTableOfContentsEntriesRequest request)
        {
            try
            {
                var records = await _dataProviderService.GetRichTextTableOfContentsEntriesAsync(request.BusinessEntityId);
                Reply(request, new GetRichTextTableOfContentsEntriesResponse(request.RequestId, records));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load rich-text table of contents entries for document {RecordId}.", request.BusinessEntityId);
                Reply(request, new GetRichTextTableOfContentsEntriesResponse(request.RequestId, Array.Empty<BusinessEntity.Core.RichText.RichTextDocumentTableOfContentsEntry>(), BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на пересоздание сохранённых entries оглавления rich-text документа.
        private async Task HandleRebuildRichTextTableOfContentsEntriesAsync(RebuildRichTextTableOfContentsEntriesRequest request)
        {
            try
            {
                var records = await _dataProviderService.RebuildRichTextTableOfContentsEntriesAsync(request.BusinessEntityId);
                Reply(request, new RebuildRichTextTableOfContentsEntriesResponse(request.RequestId, records));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild rich-text table of contents entries for document {RecordId}.", request.BusinessEntityId);
                Reply(request, new RebuildRichTextTableOfContentsEntriesResponse(request.RequestId, Array.Empty<BusinessEntity.Core.RichText.RichTextDocumentTableOfContentsEntry>(), BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на полную замену rich-text chunk-набора документа.
        private async Task HandleReplaceRichTextChunksAsync(ReplaceRichTextChunksRequest request)
        {
            try
            {
                await _dataProviderService.ReplaceRichTextChunksAsync(request.BusinessEntityId, request.Records);
                Reply(request, new ReplaceRichTextChunksResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replace rich-text chunks for document {RecordId}.", request.BusinessEntityId);
                Reply(request, new ReplaceRichTextChunksResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на сохранение embedded-файлов rich-text документа.
        private async Task HandleSaveRichTextEmbeddedFilesAsync(SaveRichTextEmbeddedFilesRequest request)
        {
            try
            {
                await _dataProviderService.SaveRichTextEmbeddedFilesAsync(
                    request.BusinessEntityId,
                    request.Files,
                    request.ReplaceExistingFiles);
                Reply(request, new SaveRichTextEmbeddedFilesResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save rich-text embedded files for document {RecordId}.", request.BusinessEntityId);
                Reply(request, new SaveRichTextEmbeddedFilesResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Обрабатывает запрос на чтение embedded-файла rich-text документа.
        private async Task HandleGetRichTextEmbeddedFileAsync(GetRichTextEmbeddedFileRequest request)
        {
            try
            {
                var record = await _dataProviderService.GetRichTextEmbeddedFileAsync(
                    request.BusinessEntityId,
                    request.ImageId,
                    request.Variant);
                Reply(request, new GetRichTextEmbeddedFileResponse(request.RequestId, record));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load rich-text embedded file for document {RecordId}.", request.BusinessEntityId);
                Reply(request, new GetRichTextEmbeddedFileResponse(request.RequestId, null, BusError.FromException(ex)));
            }
        }

        // Обрабатывает команду на полную очистку технического rich-text storage документа.
        private async Task HandleDeleteRichTextStorageAsync(DeleteRichTextStorageRequest request)
        {
            try
            {
                await _dataProviderService.DeleteRichTextStorageAsync(request.BusinessEntityId);
                Reply(request, new DeleteRichTextStorageResponse(request.RequestId, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete rich-text storage for document {RecordId}.", request.BusinessEntityId);
                Reply(request, new DeleteRichTextStorageResponse(request.RequestId, false, BusError.FromException(ex)));
            }
        }

        // Возвращает предметный тип и полный адрес исходного запроса, включая ошибки.
        private void Reply<TResponse>(BusRequest request, TResponse response) where TResponse : BusResponse
            => _messageBus.SendMessage((TResponse)(response with { Context = request.Context }));

        // Читает и преобразует payload внутри владельца физического хранения.
        private async Task<IBusinessEntityData?> ReadPayloadAsync(Guid entityId, int? version)
        {
            var dto = version.HasValue
                ? await _dataProviderService.GetDataPayloadRecordAsync(entityId, version.Value)
                : await _dataProviderService.GetDataPayloadRecordAsync(entityId);
            if (dto is null) return null;
            if (dto.Version < 1 || string.IsNullOrWhiteSpace(dto.Data))
                throw new InvalidDataException("Повреждена обязательная версия payload.");
            var data = _codec.DeserializeEnvelope<IBusinessEntityData>(dto.Data);
            data.Version = dto.Version;
            if (data is BusinessEntityData typed)
            {
                var entity = await _dataProviderService.GetByIdAsync(entityId);
                if (entity is null) throw new InvalidDataException("Payload не имеет сущности-владельца.");
            }
            return data;
        }

        // Снимает все bus-подписки при остановке владельца хранения.
        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}

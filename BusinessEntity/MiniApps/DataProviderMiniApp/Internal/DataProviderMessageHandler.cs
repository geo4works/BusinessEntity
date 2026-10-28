using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using ReactiveUI;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal;

// Единственный read endpoint; записи обслуживает исключительно транзакционный endpoint.
internal sealed class DataProviderMessageHandler : BusEndpoint
{
    private readonly DataProviderReadService _reader;
    // Получает общий транспорт и собственные проекции чтения.
    public DataProviderMessageHandler(IMessageBus bus, DataProviderReadService reader, ILogger<DataProviderMessageHandler> logger) : base(bus, logger) => _reader = reader;
    // Подписывает только чтение; все изменения проходят транзакционный endpoint.
    protected override void SubscribeRequests()
    {
        Subscribe<QueryGraphRequest, QueryGraphResponse>(async r => new(r.RequestId, await _reader.QueryGraphAsync(r.Kind, r.RootId, r.SearchText, r.Take)), static (id, e) => new(id, null, e));
        Subscribe<GetPublishedEntityNamesRequest, GetPublishedEntityNamesResponse>(async r => new(r.RequestId, await _reader.GetPublishedNamesAsync(r.EntityIds)), static (id, e) => new(id, null, e));
        Subscribe<GetDataProviderCacheStatisticsRequest, GetDataProviderCacheStatisticsResponse>(async r => new(r.RequestId, await _reader.GetStatisticsAsync()), static (id, e) => new(id, null, e));
        Subscribe<GetBusinessEntitiesRequest, GetBusinessEntitiesResponse>(async r => new(r.RequestId, await _reader.GetAllAsync()), static (id, e) => new(id, [], e));
        Subscribe<GetBusinessEntityByIdRequest, GetBusinessEntityByIdResponse>(async r => new(r.RequestId, await _reader.GetByIdAsync(r.Id)), static (id, e) => new(id, null, e));
        Subscribe<GetBusinessEntityDataRequest, GetBusinessEntityDataResponse>(async r => new(r.RequestId, await _reader.GetPayloadAsync(r.BusinessEntityId, null)), static (id, e) => new(id, null, e));
        Subscribe<GetBusinessEntityDataVersionRequest, GetBusinessEntityDataVersionResponse>(async r => new(r.RequestId, await _reader.GetPayloadAsync(r.BusinessEntityId, r.Version)), static (id, e) => new(id, null, e));
        Subscribe<GetBusinessEntityDataVersionsRequest, GetBusinessEntityDataVersionsResponse>(async r => new(r.RequestId, await _reader.GetVersionsAsync(r.BusinessEntityId)), static (id, e) => new(id, [], e));
        Subscribe<GetAllRelationsRequest, GetAllRelationsResponse>(async r => new(r.RequestId, await _reader.GetRelationsAsync()), static (id, e) => new(id, [], e));
        Subscribe<GetRelationsRequest, GetRelationsResponse>(async r => new(r.RequestId, await _reader.GetRelationsAsync(r.ObjectAId, r.ObjectBId)), static (id, e) => new(id, [], e));
        Subscribe<GetRelationByIdRequest, GetRelationByIdResponse>(async r => new(r.RequestId, await _reader.GetRelationByIdAsync(r.Id)), static (id, e) => new(id, null, e));
        Subscribe<GetRichTextEmbeddedFileRequest, GetRichTextEmbeddedFileResponse>(async r => new(r.RequestId, await _reader.GetEmbeddedFileAsync(r.BusinessEntityId, r.ImageId, r.Variant)), static (id, e) => new(id, null, e));
    }
}

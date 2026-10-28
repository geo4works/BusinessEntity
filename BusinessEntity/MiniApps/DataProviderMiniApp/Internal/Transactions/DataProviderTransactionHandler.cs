using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using ReactiveUI;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Один singleton-владелец транзакций; автора получает из запроса, circuit не захватывает.
internal sealed class DataProviderTransactionHandler : BusEndpoint
{
    private readonly DataProviderTransactionService _writer;
    private readonly DataProviderReadService _reader;
    private readonly ExportFileLeases _leases;
    // Подключает общий транспорт и собственный атомарный storage-сервис.
    public DataProviderTransactionHandler(IMessageBus bus, DataProviderTransactionService writer, DataProviderReadService reader,
        ExportFileLeases leases, ILogger<DataProviderTransactionHandler> logger) : base(bus, logger) { _writer = writer; _reader = reader; _leases = leases; }
    // Подписывает транзакцию и получение исхода с единым контрактом отказов.
    protected override void SubscribeRequests()
    {
        Subscribe<ExportSubgraphRequest, ExportSubgraphResponse>(async request => new(request.RequestId,
            await _writer.ExportAsync(request.Scope, request.Context, _leases)), static (id, error) => new(id, null, error));
        Subscribe<ReleaseExportRequest, ReleaseExportResponse>(request =>
        {
            _leases.Release(request.ExportId, request.Context);
            return Task.FromResult(new ReleaseExportResponse(request.RequestId));
        }, static (id, error) => new(id, error));
        Subscribe<GetVersionChunkMembershipRequest, GetVersionChunkMembershipResponse>(async request => new(request.RequestId,
            await _reader.GetMembershipAsync(request.EntityId, request.Version)), static (id, error) => new(id, null, error));
        Subscribe<GetStoredCommentsRequest, GetStoredCommentsResponse>(async request => new(request.RequestId,
            await _reader.GetCommentsAsync(request.EntityId)), static (id, error) => new(id, null, error));
        Subscribe<GetStoredPropertiesRequest, GetStoredPropertiesResponse>(async request => new(request.RequestId,
            await _reader.GetPropertiesAsync(request.Owner, request.ParentEntityId)), static (id, error) => new(id, null, error));
        Subscribe<GetVersionChunkWindowRequest, GetVersionChunkWindowResponse>(async request => new(request.RequestId,
            await _reader.GetChunkWindowAsync(request.EntityId, request.Version, request.Start, request.Count, request.ChunkIds, request.SearchText)),
            static (id, error) => new(id, null, error));
        Subscribe<GetStoredFileRequest, GetStoredFileResponse>(async request => new(request.RequestId,
            await _reader.GetFileAsync(request.EntityId, request.FileId, request.Variant)), static (id, error) => new(id, null, error));
        Subscribe<SaveEntityBatchRequest, SaveEntityBatchResponse>(async request =>
            new(request.RequestId, await _writer.SaveAsync(request.Batch, request.Context)),
            static (id, error) => new(id, null, error));
        Subscribe<GetOperationOutcomeRequest, GetOperationOutcomeResponse>(async request =>
            new(request.RequestId, await _writer.GetOutcomeAsync(request.OperationId, request.Context)),
            static (id, error) => new(id, null, error));
    }
}

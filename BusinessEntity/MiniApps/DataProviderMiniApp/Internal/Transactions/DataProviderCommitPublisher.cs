using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using ReactiveUI;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Единственный издатель событий изменения графа; вызывается писателем строго после SQL commit.
internal sealed class DataProviderCommitPublisher
{
    private readonly IMessageBus _bus;
    private readonly ILogger<DataProviderCommitPublisher> _logger;
    // Получает общий транспорт без данных конкретного окна.
    public DataProviderCommitPublisher(IMessageBus bus, ILogger<DataProviderCommitPublisher> logger)
    { _bus = bus; _logger = logger; }
    // События содержат идентификаторы; чужое UI-состояние и изменяемые карточки не рассылаются.
    public void Publish(SaveBatchResult result)
    {
        foreach (var id in result.ChangedEntities) Send(new BusinessEntityChanged(result.OperationId, id, false));
        foreach (var id in result.DeletedEntities) Send(new BusinessEntityChanged(result.OperationId, id, true));
        foreach (var id in result.ChangedRelations) Send(new BusinessEntityRelationChanged(result.OperationId, id, false));
        foreach (var id in result.DeletedRelations) Send(new BusinessEntityRelationChanged(result.OperationId, id, true));
        if (result.ChangedEntities.Count + result.DeletedEntities.Count + result.ChangedRelations.Count + result.DeletedRelations.Count > 0)
            Send(new EntityBatchCommitted(result.OperationId, result.ChangedEntities, result.DeletedEntities,
                result.ChangedRelations, result.DeletedRelations));
    }
    // Ошибка отдельного потребителя не превращает уже совершённый коммит в объявленный откат.
    private void Send<T>(T message)
    {
        try { _bus.SendMessage(message); }
        catch (Exception exception) { _logger.LogError(exception, "Committed data event {EventType} consumer failed", typeof(T).Name); }
    }
}

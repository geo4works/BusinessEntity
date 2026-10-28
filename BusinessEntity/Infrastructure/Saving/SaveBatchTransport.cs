using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.Infrastructure.Saving;

// Общая механика сохранения использует только публичный connector DataProvider.
public sealed class SaveBatchTransport : ISaveBatchTransport
{
    private readonly IDataProviderConnector _data;
    // Принимает scoped transport, который устанавливает доверенного пользователя.
    public SaveBatchTransport(IDataProviderConnector data) => _data = data;
    // DataProvider получает один пакет, а не независимые записи участников.
    public Task<SaveBatchResult> SaveAsync(EntitySaveBatch batch, CancellationToken ct) => _data.SaveAsync(batch, ct);
    // Durable outcome читается через ту же шину и проверку автора.
    public Task<StoredOperationOutcome?> GetOutcomeAsync(Guid operationId, CancellationToken ct) => _data.GetOperationOutcomeAsync(operationId, ct);
}

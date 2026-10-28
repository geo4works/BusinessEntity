using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.Infrastructure.Saving;

// Участник владеет предметным черновиком нескольких сущностей; общая механика не знает его типов.
public interface IMiniAppSaveParticipant
{
    // Синхронно фиксирует независимый полный пакет до первого await сохранения.
    EntitySaveBatch Capture(Guid operationId, Guid editSessionId, long revision);
    // При необходимости асинхронно готовит уже фиксированный снимок (HTML, файлы), не читая живой UI.
    Task<EntitySaveBatch> PrepareAsync(EntitySaveBatch captured, CancellationToken ct) => Task.FromResult(captured);
    // Подтверждает отправленный снимок; новые изменения не подменяются сохранёнными данными.
    Task AcknowledgeAsync(EntitySaveBatch sent, SaveBatchResult result, long revision, CancellationToken ct);
    // Сбрасывает локальный черновик и перечитывает сохранённое состояние.
    Task DiscardAsync(CancellationToken ct);
}
// Транспорт подтверждает durable outcome; null не означает доказанного отката.
public interface ISaveBatchTransport
{
    // Отправляет весь снимок рабочего экземпляра одной транзакцией.
    Task<SaveBatchResult> SaveAsync(EntitySaveBatch batch, CancellationToken ct);
    // Выясняет подтверждённый исход повторно по исходному OperationId.
    Task<StoredOperationOutcome?> GetOutcomeAsync(Guid operationId, CancellationToken ct);
}
// Состояние общей операции; Unknown сохраняет тот же пакет для безопасного повтора.
public enum MiniAppSaveStatus { Idle, Saving, Saved, Failed, Unknown }
// Ответ guard/команды, не объявляющий неизвестный исход успешным.
public sealed record MiniAppSaveAttempt(bool Success, MiniAppSaveStatus Status, string? Error = null);

// Адресный immutable статус рабочего экземпляра на единственной шине приложения.
public sealed record MiniAppSaveStateChanged(Guid LogicalWindowId, Guid? UserId, string MiniAppId,
    Guid EditSessionId, Guid? ContextId, long StateRevision, long EditRevision,
    bool IsDirty, MiniAppSaveStatus Status, Guid? OperationId);

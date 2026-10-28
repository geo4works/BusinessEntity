using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;

// Передаёт полный снимок сохранения рабочего экземпляра своему владельцу хранения.
public sealed record SaveEntityBatchRequest(Guid RequestId, EntitySaveBatch Batch) : BusRequest(RequestId);
// Подтверждает один SQL-коммит и согласованные номера версий.
public sealed record SaveEntityBatchResponse(Guid RequestId, SaveBatchResult? Result, BusError? Error = null) : BusResponse(RequestId, Error);
// Выясняет достоверный исход независимо от потерянного предыдущего ответа.
public sealed record GetOperationOutcomeRequest(Guid RequestId, Guid OperationId) : BusRequest(RequestId);
// Null означает отсутствие зафиксированного исхода, а не доказанный откат незавершённой операции.
public sealed record GetOperationOutcomeResponse(Guid RequestId, StoredOperationOutcome? Outcome, BusError? Error = null) : BusResponse(RequestId, Error);

// Экспортирует полный согласованный подграф во временный серверный lease.
public sealed record ExportSubgraphRequest(Guid RequestId, BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives.SubgraphExportScope Scope) : BusRequest(RequestId);
// Manifest и временные источники файлов принадлежат DataProvider до освобождения lease.
public sealed record ExportSubgraphResponse(Guid RequestId, BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives.SubgraphExportResult? Result, BusError? Error = null) : BusResponse(RequestId, Error);
// Явно освобождает временную копию своего запроса экспорта.
public sealed record ReleaseExportRequest(Guid RequestId, Guid ExportId) : BusRequest(RequestId);
// Подтверждает освобождение временных ресурсов.
public sealed record ReleaseExportResponse(Guid RequestId, BusError? Error = null) : BusResponse(RequestId, Error);

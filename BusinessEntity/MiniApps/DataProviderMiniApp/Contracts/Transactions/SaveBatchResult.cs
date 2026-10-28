namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

// Достоверный результат зафиксированного пакета; тот же результат возвращается при повторе OperationId.
public sealed record SaveBatchResult(Guid OperationId, DateTime CommittedAt,
    IReadOnlyList<SavedEntityVersion> Versions, IReadOnlyList<Guid> ChangedEntities,
    IReadOnlyList<Guid> DeletedEntities, IReadOnlyList<Guid> ChangedRelations,
    IReadOnlyList<Guid> DeletedRelations);
// Номер согласованного снимка после применения конкретной сущности.
public sealed record SavedEntityVersion(Guid EntityId, int Version, bool CreatedVersion);
// Найденный в БД исход; null ответа означает отсутствие подтверждённого исхода.
public sealed record StoredOperationOutcome(Guid OperationId, Guid? AuthorUserId,
    DateTime CommittedAt, SaveBatchResult Result);

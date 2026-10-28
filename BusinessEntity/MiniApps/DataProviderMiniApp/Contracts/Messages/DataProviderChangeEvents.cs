namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;

// Общее событие карточки; получатель сам проверяет свой контекст и право чтения.
public sealed record BusinessEntityChanged(Guid OperationId, Guid EntityId, bool Deleted);
// Общее событие ребра после подтверждённого коммита.
public sealed record BusinessEntityRelationChanged(Guid OperationId, Guid RelationId, bool Deleted);
// Одно уведомление всего пакета позволяет дереву перечитать свою проекцию без многократных перерисовок.
public sealed record EntityBatchCommitted(Guid OperationId, IReadOnlyList<Guid> ChangedEntities,
    IReadOnlyList<Guid> DeletedEntities, IReadOnlyList<Guid> ChangedRelations, IReadOnlyList<Guid> DeletedRelations);

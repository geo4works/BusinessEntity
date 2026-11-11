using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

// Публичный состав удаления не раскрывает IDs объектов внешнего пространства без его ACL.
public sealed record ApiDeletionPlan(Guid PlanId,IReadOnlyList<Guid> RootIds,IReadOnlyList<Guid> EntityIds,bool HasProtectedDependencies,DateTime ExpiresAt);
public sealed record PrepareApiDeletionPlanRequest(Guid RequestId,Guid PlanId,IReadOnlyList<Guid> RootIds,Guid SpaceId,IReadOnlyList<ApiPermissionCheck> Checks):BusRequest(RequestId);
public sealed record PrepareApiDeletionPlanResponse(Guid RequestId,ApiDeletionPlan? Result,BusError? Error=null):BusResponse(RequestId,Error);

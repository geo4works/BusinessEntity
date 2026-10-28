using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;

namespace BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages
{
    // Снимок узла дерева для передачи через bus.
    public sealed record TreeNodeSnapshot(
        BusinessEntity.Core.Classes.BusinessEntity Entity,
        IReadOnlyList<TreeNodeSnapshot> Children);

    // Снимок дерева выбранного пространства.
    public sealed record TreeSpaceSnapshot(
        BusinessEntity.Core.Classes.BusinessEntity Space,
        IReadOnlyList<TreeNodeSnapshot> Children);

    // Запрос на полную загрузку дерева пространства.
    public sealed record GetTreeForSpaceRequest(Guid RequestId, Guid SpaceId) : BusRequest(RequestId);

    // Ответ со снимком дерева пространства.
    public sealed record GetTreeForSpaceResponse(Guid RequestId, TreeSpaceSnapshot? Snapshot, BusError? Error = null) : BusResponse(RequestId, Error);
    // Завершённая операция удаления всего выбранного набора.
    public sealed record DeleteTreeEntitiesRequest(Guid RequestId, IReadOnlyList<Guid> EntityIds) : BusRequest(RequestId);
    // Подтверждение атомарного удаления набора.
    public sealed record DeleteTreeEntitiesResponse(Guid RequestId, BusError? Error = null) : BusResponse(RequestId, Error);
}

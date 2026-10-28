using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;

namespace BusinessEntity.MiniApps.TreeMiniApp.Contracts.Connectors
{
    // Контракт bus-connector дерева.
    public interface ITreeConnector
    {
        // Показывает доступный узел в дереве того же окна.
        Task RevealEntityAsync(Guid? entityId, CancellationToken cancellationToken = default);
        // Удаляет набор дерева одной транзакцией.
        Task DeleteEntitiesAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default);
        // Загружает полный снимок дерева для выбранного пространства.
        Task<TreeSpaceSnapshot?> GetTreeForSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default);
    }
}

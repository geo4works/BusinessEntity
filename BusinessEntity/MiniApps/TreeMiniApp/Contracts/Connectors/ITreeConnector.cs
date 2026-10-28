using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;

namespace BusinessEntity.MiniApps.TreeMiniApp.Contracts.Connectors
{
    // Контракт bus-connector дерева.
    public interface ITreeConnector
    {
        // Удаляет набор дерева одной транзакцией.
        Task DeleteEntitiesAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default);
        // Загружает полный снимок дерева для выбранного пространства.
        Task<TreeSpaceSnapshot?> GetTreeForSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default);
    }
}

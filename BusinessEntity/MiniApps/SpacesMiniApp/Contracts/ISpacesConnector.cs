using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
namespace BusinessEntity.MiniApps.SpacesMiniApp.Contracts;

// Публичные запросы к владельцу через единственную шину.
public interface ISpacesConnector
{
    // Выполняет GetSpaceByIdAsync у владельца.
    Task<BusinessEntity.Core.Classes.BusinessEntity?> GetSpaceByIdAsync(Guid spaceId, CancellationToken ct = default);
    // Выполняет GetSpacesAsync у владельца.
    Task<IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity>> GetSpacesAsync(CancellationToken ct = default);
    // Выполняет GetGenericSpacePropertiesAsync у владельца.
    Task<GenericSpaceProperties> GetGenericSpacePropertiesAsync(Guid spaceId, CancellationToken ct = default);
}

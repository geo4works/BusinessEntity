using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
namespace BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;

// Публичные запросы к владельцу через единственную шину.
public interface IMediaLibraryConnector
{
    // Выполняет GetVideosAsync у владельца.
    Task<IReadOnlyList<MediaVideoInfo>> GetVideosAsync(Guid? spaceId, string? filter, CancellationToken ct = default);
}

using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
namespace BusinessEntity.MiniApps.MediaServerMiniApp.Connectors;

// Только адресный транспорт, без предметной логики.
public sealed class MediaLibraryConnector(BusRequestClient client) : IMediaLibraryConnector
{
    // Отправляет GetVideosAsync по адресу текущего scope.
    public async Task<IReadOnlyList<MediaVideoInfo>> GetVideosAsync(Guid? spaceId, string? filter, CancellationToken ct = default)
    {
        var response = await client.SendAsync<MediaLibraryGetVideosRequest, MediaLibraryGetVideosResponse>(new MediaLibraryGetVideosRequest(Guid.NewGuid() , spaceId, filter), ct);
        return response.Value;
    }
}

using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
namespace BusinessEntity.MiniApps.SpacesMiniApp.Connectors;

// Только адресный транспорт, без предметной логики.
public sealed class SpacesConnector(BusRequestClient client) : ISpacesConnector
{
    // Отправляет GetSpaceByIdAsync по адресу текущего scope.
    public async Task<BusinessEntity.Core.Classes.BusinessEntity?> GetSpaceByIdAsync(Guid spaceId, CancellationToken ct = default)
    {
        var response = await client.SendAsync<SpacesGetSpaceByIdRequest, SpacesGetSpaceByIdResponse>(new SpacesGetSpaceByIdRequest(Guid.NewGuid() , spaceId), ct);
        return response.Value;
    }
    // Отправляет GetSpacesAsync по адресу текущего scope.
    public async Task<IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity>> GetSpacesAsync(CancellationToken ct = default)
    {
        var response = await client.SendAsync<SpacesGetSpacesRequest, SpacesGetSpacesResponse>(new SpacesGetSpacesRequest(Guid.NewGuid()), ct);
        return response.Value;
    }
    // Отправляет GetGenericSpacePropertiesAsync по адресу текущего scope.
    public async Task<GenericSpaceProperties> GetGenericSpacePropertiesAsync(Guid spaceId, CancellationToken ct = default)
    {
        var response = await client.SendAsync<SpacesGetGenericSpacePropertiesRequest, SpacesGetGenericSpacePropertiesResponse>(new SpacesGetGenericSpacePropertiesRequest(Guid.NewGuid() , spaceId), ct);
        return response.Value;
    }
}

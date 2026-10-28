using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SampleDataMiniApp.Contracts;
namespace BusinessEntity.MiniApps.SampleDataMiniApp.Connectors;

// Только адресный транспорт, без предметной логики.
public sealed class SampleDataConnector(BusRequestClient client) : ISampleDataConnector
{
    // Отправляет ForceReseedAsync по адресу текущего scope.
    public async Task<bool> ForceReseedAsync(CancellationToken ct = default)
    {
        var response = await client.SendAsync<SampleDataForceReseedRequest, SampleDataForceReseedResponse>(new SampleDataForceReseedRequest(Guid.NewGuid()), ct);
        return response.Value;
    }
}

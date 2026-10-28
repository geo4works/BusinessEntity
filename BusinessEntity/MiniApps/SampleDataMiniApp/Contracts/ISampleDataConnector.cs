using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
namespace BusinessEntity.MiniApps.SampleDataMiniApp.Contracts;

// Публичные запросы к владельцу через единственную шину.
public interface ISampleDataConnector
{
    // Выполняет ForceReseedAsync у владельца.
    Task<bool> ForceReseedAsync(CancellationToken ct = default);
}

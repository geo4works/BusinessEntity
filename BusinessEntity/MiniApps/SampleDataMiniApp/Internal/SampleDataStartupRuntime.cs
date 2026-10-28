using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.SampleDataMiniApp.Contracts;

namespace BusinessEntity.MiniApps.SampleDataMiniApp.Internal;

// Идемпотентная заливка запускается после storage, identity и defaults.
internal sealed class SampleDataStartupRuntime : IModuleRuntime
{
    private readonly ISampleDataService _sample;
    // Runtime вызывает собственный сервис напрямую.
    public SampleDataStartupRuntime(ISampleDataService sample) => _sample = sample;
    // Сохраняет исходное поведение seed без ручного запуска из Program.
    public Task StartAsync(CancellationToken ct) => _sample.InitializeSampleDataAsync(ct);
    // Seed не удерживает фоновых задач.
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

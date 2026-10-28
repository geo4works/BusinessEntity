using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.DataProviderMiniApp.Registration;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal;

// Создаёт актуальную схему, восстанавливает files и загружает граф раньше остальных владельцев.
internal sealed class DataProviderStartupRuntime : IModuleRuntime
{
    private readonly IServiceProvider _services;
    private readonly ApplicationStartupState _state;
    // Получает scope только для локальной композиции storage при старте.
    public DataProviderStartupRuntime(IServiceProvider services, ApplicationStartupState state) { _services = services; _state = state; }
    // Отмечает fresh storage для следующего собственного schema runtime User.
    public async Task StartAsync(CancellationToken ct) => _state.FreshEntityStorage = await _services.InitializeDataProviderStorageAsync(ct);
    // Подписки и singleton storage освобождаются владельцем контейнера DI.
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

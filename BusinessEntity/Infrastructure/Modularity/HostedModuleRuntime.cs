namespace BusinessEntity.Infrastructure.Modularity;

// Каталог управляет существующим BackgroundService через headless runtime, без второго hosted registration.
public sealed class HostedModuleRuntime : IModuleRuntime
{
    private readonly IHostedService _service;
    // Получает фоновую задачу своего владельца из его локальной регистрации.
    public HostedModuleRuntime(IHostedService service) => _service = service;
    // Запускает фон после обязательных startup зависимостей.
    public Task StartAsync(CancellationToken ct) => _service.StartAsync(ct);
    // Дожидается штатного завершения очереди/цикла.
    public Task StopAsync(CancellationToken ct) => _service.StopAsync(ct);
}

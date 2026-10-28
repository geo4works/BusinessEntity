using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Services;
using BusinessEntity.Infrastructure.Modularity;

namespace BusinessEntity.MiniApps.SysParametersMiniApp.Internal;

// Создаёт singleton настроек при startup, до публичного чтения и печати.
internal sealed class SysParametersStartupRuntime : IModuleRuntime
{
    private readonly BusinessEntityHelper _entities;
    // Использует общий движок без дублирования singleton алгоритма.
    public SysParametersStartupRuntime(BusinessEntityHelper entities) => _entities = entities;
    // Публичный читатель не должен впервые создавать обязательные настройки.
    public async Task StartAsync(CancellationToken ct) => await _entities.GetOrCreateSingletonEntityAsync<SysParameters>(global::BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.SysParametersTpType.Id, "SysParameters", ct);
    // Постоянные настройки не удаляются при завершении runtime.
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

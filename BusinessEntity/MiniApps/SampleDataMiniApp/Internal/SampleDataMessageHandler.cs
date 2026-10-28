using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SampleDataMiniApp.Contracts;
using ReactiveUI;
namespace BusinessEntity.MiniApps.SampleDataMiniApp.Internal;

// Делегирует запрос своему владельцу в server scope.
internal sealed class SampleDataMessageHandler : ScopedBusEndpoint
{
    private readonly ISampleDataService _samples;
    private readonly BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors.IUserConnector _users;
    // Подключает собственные сервисы и адрес обработчика.
    public SampleDataMessageHandler(ISampleDataService _samples, BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors.IUserConnector _users, IMessageBus bus, BusRequestScope scope, ILogger<SampleDataMessageHandler> logger) : base(bus, scope, logger)
    { this._samples = _samples; this._users = _users; }
    // Подписки освобождаются вместе с окном.
    protected override void SubscribeRequests()
    {
        Subscribe<SampleDataForceReseedRequest, SampleDataForceReseedResponse>(async request => new SampleDataForceReseedResponse(request.RequestId, await RunAsync()), static (id, error) => new SampleDataForceReseedResponse(id, default!, error));
    }
    // Запуск тестовой заливки разрешён только администратору текущего server scope.
    private async Task<bool> RunAsync()
    {
        var user = await _users.GetCurrentUserAsync();
        if (user?.IsAkadmin != true && user?.IsGeneralAdmin != true && !string.Equals(user?.UserName, "admin", StringComparison.OrdinalIgnoreCase))
        {
            var permissions = await _users.GetCurrentUserPermissionsForSpaceAsync(Guid.Empty);
            if (!permissions.CanGlobalAdmin) throw new UnauthorizedAccessException();
        }
        await _samples.ForceReseedAsync(); return true;
    }
}

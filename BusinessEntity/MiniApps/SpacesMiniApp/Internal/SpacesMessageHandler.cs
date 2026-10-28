using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using ReactiveUI;
namespace BusinessEntity.MiniApps.SpacesMiniApp.Internal;

// Делегирует запрос своему владельцу в server scope.
internal sealed class SpacesMessageHandler : ScopedBusEndpoint
{
    private readonly SpaceHelper _spaces;
    // Подключает собственные сервисы и адрес обработчика.
    public SpacesMessageHandler(SpaceHelper _spaces, IMessageBus bus, BusRequestScope scope, ILogger<SpacesMessageHandler> logger) : base(bus, scope, logger)
    { this._spaces = _spaces; }
    // Подписки освобождаются вместе с окном.
    protected override void SubscribeRequests()
    {
        Subscribe<SpacesGetSpaceByIdRequest, SpacesGetSpaceByIdResponse>(async request => new SpacesGetSpaceByIdResponse(request.RequestId, await _spaces.GetSpaceByIdAsync(request.SpaceId)), static (id, error) => new SpacesGetSpaceByIdResponse(id, default!, error));
        Subscribe<SpacesGetSpacesRequest, SpacesGetSpacesResponse>(async request => new SpacesGetSpacesResponse(request.RequestId, await _spaces.GetSpacesAsync()), static (id, error) => new SpacesGetSpacesResponse(id, default!, error));
        Subscribe<SpacesGetGenericSpacePropertiesRequest, SpacesGetGenericSpacePropertiesResponse>(async request => new SpacesGetGenericSpacePropertiesResponse(request.RequestId, await _spaces.GetGenericSpacePropertiesAsync(request.SpaceId)), static (id, error) => new SpacesGetGenericSpacePropertiesResponse(id, default!, error));
    }
}

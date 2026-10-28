using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;
using ReactiveUI;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal;

// Обрабатывает запросы дерева только своего server scope.
internal sealed class TreeMiniAppMessageHandler : ScopedBusEndpoint
{
    private readonly TreeMiniAppService _service;
    // Привязывает собственный сервис к адресу обработчика.
    public TreeMiniAppMessageHandler(TreeMiniAppService service, IMessageBus bus,
        BusRequestScope scope, ILogger<TreeMiniAppMessageHandler> logger) : base(bus, scope, logger)
        => _service = service;
    // Создаёт единственную подписку для текущего окна.
    protected override void SubscribeRequests()
    {
        Subscribe<DeleteTreeEntitiesRequest, DeleteTreeEntitiesResponse>(async request =>
        { await _service.DeleteEntitiesAsync(request.EntityIds); return new DeleteTreeEntitiesResponse(request.RequestId); },
            static (id, error) => new DeleteTreeEntitiesResponse(id, error));
        Subscribe<GetTreeForSpaceRequest, GetTreeForSpaceResponse>(async request =>
            new GetTreeForSpaceResponse(request.RequestId, await _service.GetTreeForSpaceAsync(request.SpaceId)),
            static (id, error) => new GetTreeForSpaceResponse(id, null, error));
    }
}

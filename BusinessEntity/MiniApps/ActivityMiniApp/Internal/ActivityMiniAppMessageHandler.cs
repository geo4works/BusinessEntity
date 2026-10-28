using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts.Messages;
using ReactiveUI;

namespace BusinessEntity.MiniApps.ActivityMiniApp.Internal;

// Адаптер собственных адресных запросов миниаппа.
internal sealed class ActivityMiniAppMessageHandler : ScopedBusEndpoint
{
    private readonly ActivityCommentService _service;

    // Использует сервис своего миниаппа и общий транспорт текущего scope.
    public ActivityMiniAppMessageHandler(ActivityCommentService service, IMessageBus bus, BusRequestScope scope, ILogger<ActivityMiniAppMessageHandler> logger)
        : base(bus, scope, logger) => _service = service;

    // Подключает предметные запросы один раз для своего адреса.
    protected override void SubscribeRequests()
    {
        Subscribe<GetCommentsRequest, GetCommentsResponse>(async request =>
        {
            var result = await _service.GetCommentsAsync(request.BusinessEntityId);
            return new GetCommentsResponse(request.RequestId, result);
        }, static (id, error) => new GetCommentsResponse(id, default, error));
        Subscribe<CreateCommentRequest, CreateCommentResponse>(async request =>
        {
            var result = await _service.CreateCommentAsync(request.Request);
            return new CreateCommentResponse(request.RequestId, result);
        }, static (id, error) => new CreateCommentResponse(id, default, error));
    }
}

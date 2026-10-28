using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;
using ReactiveUI;
namespace BusinessEntity.MiniApps.TreeMiniApp.Internal;

// Адресный endpoint UI намерения не получает доступ к чужому окну или внутренним VM.
internal sealed class TreeRevealMessageHandler : ScopedBusEndpoint
{
    private readonly TreeRevealService _service;
    // Использует собственный сервис и инфраструктуру одного server scope.
    public TreeRevealMessageHandler(TreeRevealService service, IMessageBus bus, BusRequestScope scope,
        ILogger<TreeRevealMessageHandler> logger) : base(bus, scope, logger) => _service = service;
    // Подписывает одну пару типизированного запроса и ответа.
    protected override void SubscribeRequests() => Subscribe<RevealTreeEntityRequest, RevealTreeEntityResponse>(
        async request =>
        {
            try { await _service.RevealAsync(request.EntityId, request.Context); return new(request.RequestId); }
            // Завершение browser UI даёт явную отмену намерения, а не ошибку предметного сервиса.
            catch (Exception error) when (error is OperationCanceledException or Microsoft.JSInterop.JSDisconnectedException)
            { return new(request.RequestId, new BusError("operation.cancelled", "Показ узла отменён при завершении окна.")); }
        },
        static (id, error) => new(id, error));
}

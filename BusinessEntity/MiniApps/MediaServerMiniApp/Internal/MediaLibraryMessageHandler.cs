using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using ReactiveUI;
namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

// Делегирует запрос своему владельцу в server scope.
internal sealed class MediaLibraryMessageHandler : ScopedBusEndpoint
{
    private readonly MediaServerService _media;
    // Подключает собственные сервисы и адрес обработчика.
    public MediaLibraryMessageHandler(MediaServerService _media, IMessageBus bus, BusRequestScope scope, ILogger<MediaLibraryMessageHandler> logger) : base(bus, scope, logger)
    { this._media = _media; }
    // Подписки освобождаются вместе с окном.
    protected override void SubscribeRequests()
    {
        Subscribe<MediaLibraryGetVideosRequest, MediaLibraryGetVideosResponse>(async request => new MediaLibraryGetVideosResponse(request.RequestId, await _media.GetVideosAsync(request.SpaceId, request.Filter)), static (id, error) => new MediaLibraryGetVideosResponse(id, default!, error));
    }
}

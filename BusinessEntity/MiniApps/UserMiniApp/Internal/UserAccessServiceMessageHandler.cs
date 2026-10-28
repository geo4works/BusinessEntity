using BusinessEntity.Services;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using System.Text.Json;

using ReactiveUI;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Messages;
namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Адресный endpoint выделенного сценария User.
internal sealed class UserAccessServiceMessageHandler : ScopedBusEndpoint
{
    private readonly UserAccessService _service;
    // Подписки принадлежат только текущему server scope.
    public UserAccessServiceMessageHandler(UserAccessService service, IMessageBus bus, BusRequestScope scope, ILogger<UserAccessServiceMessageHandler> logger) : base(bus, scope, logger) => _service = service;
    // Один адресный обработчик на объявленные типы запросов.
    protected override void SubscribeRequests()
    {
        Subscribe<GetEntityContentAccessRequest, GetEntityContentAccessResponse>(async request =>
            new GetEntityContentAccessResponse(request.RequestId, await _service.GetContentAccessForEntityIdAsync(request.EntityId)),
            static (id, error) => new GetEntityContentAccessResponse(id, null, error));
        Subscribe<GetCurrentUserPermissionsForSpaceRequest, GetCurrentUserPermissionsForSpaceResponse>(async request =>
        {
            var result = await _service.GetCurrentUserPermissionsForSpaceAsync(request.SpaceId);
            return new GetCurrentUserPermissionsForSpaceResponse(request.RequestId, result);
        }, static (id, error) => new GetCurrentUserPermissionsForSpaceResponse(id, default, error));
        Subscribe<GetCurrentUserPermissionsForEntityRequest, GetCurrentUserPermissionsForEntityResponse>(async request =>
        {
            var result = await _service.GetCurrentUserPermissionsForEntityAsync(request.EntityId);
            return new GetCurrentUserPermissionsForEntityResponse(request.RequestId, result);
        }, static (id, error) => new GetCurrentUserPermissionsForEntityResponse(id, default, error));
        Subscribe<GetCurrentUserContentAccessForEntityRequest, GetCurrentUserContentAccessForEntityResponse>(async request =>
        {
            var result = await _service.GetCurrentUserContentAccessForEntityAsync(request.Request);
            return new GetCurrentUserContentAccessForEntityResponse(request.RequestId, result);
        }, static (id, error) => new GetCurrentUserContentAccessForEntityResponse(id, default, error));
        Subscribe<GetAnonymousPermissionsForSpaceRequest, GetAnonymousPermissionsForSpaceResponse>(async request =>
        {
            var result = await _service.GetAnonymousPermissionsForSpaceAsync(request.SpaceId);
            return new GetAnonymousPermissionsForSpaceResponse(request.RequestId, result);
        }, static (id, error) => new GetAnonymousPermissionsForSpaceResponse(id, default, error));
        Subscribe<GetAnonymousAccessibleSpacesRequest, GetAnonymousAccessibleSpacesResponse>(async request =>
        {
            var result = await _service.GetAnonymousAccessibleSpacesAsync();
            return new GetAnonymousAccessibleSpacesResponse(request.RequestId, result);
        }, static (id, error) => new GetAnonymousAccessibleSpacesResponse(id, default, error));
        Subscribe<GetAnonymousAccessibleDocumentsRequest, GetAnonymousAccessibleDocumentsResponse>(async request =>
        {
            var result = await _service.GetAnonymousAccessibleDocumentsAsync(request.SpaceId);
            return new GetAnonymousAccessibleDocumentsResponse(request.RequestId, result);
        }, static (id, error) => new GetAnonymousAccessibleDocumentsResponse(id, default, error));
    }
}

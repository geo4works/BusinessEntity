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
internal sealed class UserRoleCatalogServiceMessageHandler : ScopedBusEndpoint
{
    private readonly UserRoleCatalogService _service;
    // Подписки принадлежат только текущему server scope.
    public UserRoleCatalogServiceMessageHandler(UserRoleCatalogService service, IMessageBus bus, BusRequestScope scope, ILogger<UserRoleCatalogServiceMessageHandler> logger) : base(bus, scope, logger) => _service = service;
    // Один адресный обработчик на объявленные типы запросов.
    protected override void SubscribeRequests()
    {
        Subscribe<GetRolesRequest, GetRolesResponse>(async request =>
        {
            var result = await _service.GetRolesAsync();
            return new GetRolesResponse(request.RequestId, result);
        }, static (id, error) => new GetRolesResponse(id, default, error));
        Subscribe<CreateRoleRequest, CreateRoleResponse>(async request =>
        {
            var result = await _service.CreateRoleAsync();
            return new CreateRoleResponse(request.RequestId, result);
        }, static (id, error) => new CreateRoleResponse(id, default, error));
        Subscribe<UpdateRoleRequest, UpdateRoleResponse>(async request =>
        {
            var result = await _service.UpdateRoleAsync(request.RoleId, request.Request);
            return new UpdateRoleResponse(request.RequestId, result);
        }, static (id, error) => new UpdateRoleResponse(id, default, error));
        Subscribe<DeleteRoleRequest, DeleteRoleResponse>(async request =>
        {
            var result = await _service.DeleteRoleAsync(request.RoleId);
            return new DeleteRoleResponse(request.RequestId, result);
        }, static (id, error) => new DeleteRoleResponse(id, default, error));
    }
}

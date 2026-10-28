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
internal sealed class UserGroupServiceMessageHandler : ScopedBusEndpoint
{
    private readonly UserGroupService _service;
    // Подписки принадлежат только текущему server scope.
    public UserGroupServiceMessageHandler(UserGroupService service, IMessageBus bus, BusRequestScope scope, ILogger<UserGroupServiceMessageHandler> logger) : base(bus, scope, logger) => _service = service;
    // Один адресный обработчик на объявленные типы запросов.
    protected override void SubscribeRequests()
    {
        Subscribe<GetUserGroupsRequest, GetUserGroupsResponse>(async request =>
        {
            var result = await _service.GetUserGroupsAsync();
            return new GetUserGroupsResponse(request.RequestId, result);
        }, static (id, error) => new GetUserGroupsResponse(id, default, error));
        Subscribe<CreateUserGroupRequest, CreateUserGroupResponse>(async request =>
        {
            var result = await _service.CreateUserGroupAsync();
            return new CreateUserGroupResponse(request.RequestId, result);
        }, static (id, error) => new CreateUserGroupResponse(id, default, error));
        Subscribe<UpdateUserGroupRequest, UpdateUserGroupResponse>(async request =>
        {
            var result = await _service.UpdateUserGroupAsync(request.GroupId, request.Request);
            return new UpdateUserGroupResponse(request.RequestId, result);
        }, static (id, error) => new UpdateUserGroupResponse(id, default, error));
        Subscribe<DeleteUserGroupRequest, DeleteUserGroupResponse>(async request =>
        {
            var result = await _service.DeleteUserGroupAsync(request.GroupId);
            return new DeleteUserGroupResponse(request.RequestId, result);
        }, static (id, error) => new DeleteUserGroupResponse(id, default, error));
        Subscribe<GetUserGroupMembershipsRequest, GetUserGroupMembershipsResponse>(async request =>
        {
            var result = await _service.GetUserGroupMembershipsAsync(request.GroupId);
            return new GetUserGroupMembershipsResponse(request.RequestId, result);
        }, static (id, error) => new GetUserGroupMembershipsResponse(id, default, error));
        Subscribe<UpdateUserGroupMembershipsRequest, UpdateUserGroupMembershipsResponse>(async request =>
        {
            var result = await _service.UpdateUserGroupMembershipsAsync(request.GroupId, request.Request);
            return new UpdateUserGroupMembershipsResponse(request.RequestId, result);
        }, static (id, error) => new UpdateUserGroupMembershipsResponse(id, default, error));
    }
}

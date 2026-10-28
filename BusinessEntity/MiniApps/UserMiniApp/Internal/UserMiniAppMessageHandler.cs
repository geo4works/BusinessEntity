using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Messages;
using ReactiveUI;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Адаптер собственных адресных запросов миниаппа.
internal sealed class UserMiniAppMessageHandler : ScopedBusEndpoint
{
    private readonly UserMiniAppService _service;

    // Использует сервис своего миниаппа и общий транспорт текущего scope.
    public UserMiniAppMessageHandler(UserMiniAppService service, IMessageBus bus, BusRequestScope scope, ILogger<UserMiniAppMessageHandler> logger)
        : base(bus, scope, logger) => _service = service;

    // Подключает предметные запросы один раз для своего адреса.
    protected override void SubscribeRequests()
    {
        Subscribe<DeleteCurrentUserRequest, DeleteCurrentUserResponse>(async request =>
        {
            var result = await _service.DeleteCurrentUserAsync();
            return new DeleteCurrentUserResponse(request.RequestId, result);
        }, static (id, error) => new DeleteCurrentUserResponse(id, default, error));
        Subscribe<GetAdministrationUsersRequest, GetAdministrationUsersResponse>(async request =>
        {
            var result = await _service.GetAdministrationUsersAsync();
            return new GetAdministrationUsersResponse(request.RequestId, result);
        }, static (id, error) => new GetAdministrationUsersResponse(id, default, error));
        Subscribe<ReadAdministrationUsersFromAuthentikRequest, ReadAdministrationUsersFromAuthentikResponse>(async request =>
        {
            var result = await _service.ReadAdministrationUsersFromAuthentikAsync();
            return new ReadAdministrationUsersFromAuthentikResponse(request.RequestId, result);
        }, static (id, error) => new ReadAdministrationUsersFromAuthentikResponse(id, default, error));
        Subscribe<CreateAdministrationUserRequest, CreateAdministrationUserResponse>(async request =>
        {
            var result = await _service.CreateAdministrationUserAsync();
            return new CreateAdministrationUserResponse(request.RequestId, result);
        }, static (id, error) => new CreateAdministrationUserResponse(id, default, error));
        Subscribe<UpdateAdministrationUserRequest, UpdateAdministrationUserResponse>(async request =>
        {
            var result = await _service.UpdateAdministrationUserAsync(request.UserId, request.Request);
            return new UpdateAdministrationUserResponse(request.RequestId, result);
        }, static (id, error) => new UpdateAdministrationUserResponse(id, default, error));
        Subscribe<DeleteAdministrationUserRequest, DeleteAdministrationUserResponse>(async request =>
        {
            var result = await _service.DeleteAdministrationUserAsync(request.UserId);
            return new DeleteAdministrationUserResponse(request.RequestId, result);
        }, static (id, error) => new DeleteAdministrationUserResponse(id, default, error));
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
        Subscribe<GetRoleAssignmentSpacesRequest, GetRoleAssignmentSpacesResponse>(async request =>
        {
            var result = await _service.GetRoleAssignmentSpacesAsync();
            return new GetRoleAssignmentSpacesResponse(request.RequestId, result);
        }, static (id, error) => new GetRoleAssignmentSpacesResponse(id, default, error));
        Subscribe<GetRoleAssignmentsRequest, GetRoleAssignmentsResponse>(async request =>
        {
            var result = await _service.GetRoleAssignmentsAsync(request.SpaceId);
            return new GetRoleAssignmentsResponse(request.RequestId, result);
        }, static (id, error) => new GetRoleAssignmentsResponse(id, default, error));
        Subscribe<CreateRoleAssignmentRequest, CreateRoleAssignmentResponse>(async request =>
        {
            var result = await _service.CreateRoleAssignmentAsync(request.SpaceId, request.Request);
            return new CreateRoleAssignmentResponse(request.RequestId, result);
        }, static (id, error) => new CreateRoleAssignmentResponse(id, default, error));
        Subscribe<DeleteRoleAssignmentRequest, DeleteRoleAssignmentResponse>(async request =>
        {
            var result = await _service.DeleteRoleAssignmentAsync(request.AssignmentId);
            return new DeleteRoleAssignmentResponse(request.RequestId, result);
        }, static (id, error) => new DeleteRoleAssignmentResponse(id, default, error));
        Subscribe<GetProfileRequest, GetProfileResponse>(async request =>
        {
            var result = await _service.GetProfileAsync();
            return new GetProfileResponse(request.RequestId, result);
        }, static (id, error) => new GetProfileResponse(id, default, error));
        Subscribe<UpdateProfileRequest, UpdateProfileResponse>(async request =>
        {
            var result = await _service.UpdateProfileAsync(request.Request);
            return new UpdateProfileResponse(request.RequestId, result);
        }, static (id, error) => new UpdateProfileResponse(id, default, error));
        Subscribe<GetRichDocBookmarksRequest, GetRichDocBookmarksResponse>(async request =>
        {
            var result = await _service.GetRichDocBookmarksAsync(request.DocumentId);
            return new GetRichDocBookmarksResponse(request.RequestId, result);
        }, static (id, error) => new GetRichDocBookmarksResponse(id, default, error));
        Subscribe<AddRichDocBookmarkRequest, AddRichDocBookmarkResponse>(async request =>
        {
            var result = await _service.AddRichDocBookmarkAsync(request.DocumentId, request.Selection);
            return new AddRichDocBookmarkResponse(request.RequestId, result);
        }, static (id, error) => new AddRichDocBookmarkResponse(id, default, error));
        Subscribe<DeleteRichDocBookmarkRequest, DeleteRichDocBookmarkResponse>(async request =>
        {
            var result = await _service.DeleteRichDocBookmarkAsync(request.BookmarkId);
            return new DeleteRichDocBookmarkResponse(request.RequestId, result);
        }, static (id, error) => new DeleteRichDocBookmarkResponse(id, default, error));
        Subscribe<GetRichDocDisplayedLevelRequest, GetRichDocDisplayedLevelResponse>(async request =>
        {
            var result = await _service.GetRichDocDisplayedLevelAsync(request.DocumentId);
            return new GetRichDocDisplayedLevelResponse(request.RequestId, result);
        }, static (id, error) => new GetRichDocDisplayedLevelResponse(id, default, error));
        Subscribe<SaveRichDocDisplayedLevelRequest, SaveRichDocDisplayedLevelResponse>(async request =>
        {
            await _service.SaveRichDocDisplayedLevelAsync(request.DocumentId, request.DisplayLevelCount);
            return new SaveRichDocDisplayedLevelResponse(request.RequestId, true);
        }, static (id, error) => new SaveRichDocDisplayedLevelResponse(id, default, error));
        Subscribe<GetCurrentUserPropertiesRequest, GetCurrentUserPropertiesResponse>(async request =>
        {
            var result = await _service.GetCurrentUserPropertiesAsync(request.PropertyType);
            return new GetCurrentUserPropertiesResponse(request.RequestId, result);
        }, static (id, error) => new GetCurrentUserPropertiesResponse(id, default, error));
        Subscribe<UpsertCurrentUserPropertyRequest, UpsertCurrentUserPropertyResponse>(async request =>
        {
            var result = await _service.UpsertCurrentUserPropertyAsync(request.PropertyId, request.PropertyType, request.Data, request.Metadata);
            return new UpsertCurrentUserPropertyResponse(request.RequestId, result);
        }, static (id, error) => new UpsertCurrentUserPropertyResponse(id, default, error));
        Subscribe<DeleteCurrentUserPropertyRequest, DeleteCurrentUserPropertyResponse>(async request =>
        {
            var result = await _service.DeleteCurrentUserPropertyAsync(request.PropertyId);
            return new DeleteCurrentUserPropertyResponse(request.RequestId, result);
        }, static (id, error) => new DeleteCurrentUserPropertyResponse(id, default, error));
        Subscribe<GetDocPrintPresetsRequest, GetDocPrintPresetsResponse>(async request =>
        {
            var result = await _service.GetDocPrintPresetsAsync();
            return new GetDocPrintPresetsResponse(request.RequestId, result);
        }, static (id, error) => new GetDocPrintPresetsResponse(id, default, error));
        Subscribe<SaveDocPrintPresetRequest, SaveDocPrintPresetResponse>(async request =>
        {
            var result = await _service.SaveDocPrintPresetAsync(request.Preset);
            return new SaveDocPrintPresetResponse(request.RequestId, result);
        }, static (id, error) => new SaveDocPrintPresetResponse(id, default, error));
        Subscribe<DeleteDocPrintPresetRequest, DeleteDocPrintPresetResponse>(async request =>
        {
            var result = await _service.DeleteDocPrintPresetAsync(request.PresetName);
            return new DeleteDocPrintPresetResponse(request.RequestId, result);
        }, static (id, error) => new DeleteDocPrintPresetResponse(id, default, error));
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

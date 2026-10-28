using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Messages;

namespace BusinessEntity.MiniApps.UserMiniApp.Connectors;

// Публичный клиент предметных запросов через единственную шину.
public sealed class UserConnector : IUserConnector
{
    private readonly BusRequestClient _client;
    private readonly BusRequestScope _scope;

    // Получает клиент и серверный контекст без фасадов и внутренних сервисов.
    public UserConnector(BusRequestClient client, BusRequestScope scope)
    { _client = client; _scope = scope; }

    // Выполняет GetCurrentUser через адресный request/response.
    public async Task<BusinessEntityUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.SendAsync<GetCurrentUserRequest, GetCurrentUserResponse>(
            new GetCurrentUserRequest(Guid.NewGuid()), cancellationToken);
        return response.Result;
    }

    // Выполняет EnsureCurrentUser через адресный request/response.
    public async Task<UserDto?> EnsureCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.SendAsync<EnsureCurrentUserRequest, EnsureCurrentUserResponse>(
            new EnsureCurrentUserRequest(Guid.NewGuid()), cancellationToken);
        _scope.SetUser(response.Result?.Id);
        return response.Result;
    }

    // Выполняет DeleteCurrentUser через адресный request/response.
    public async Task<bool> DeleteCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<DeleteCurrentUserRequest, DeleteCurrentUserResponse>(
            new DeleteCurrentUserRequest(Guid.NewGuid()), cancellationToken);
        return response.Result;
    }

    // Выполняет GetAdministrationUsers через адресный request/response.
    public async Task<IReadOnlyList<UserAdministrationRecord>> GetAdministrationUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetAdministrationUsersRequest, GetAdministrationUsersResponse>(
            new GetAdministrationUsersRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет ReadAdministrationUsersFromAuthentik через адресный request/response.
    public async Task<IReadOnlyList<UserAdministrationRecord>> ReadAdministrationUsersFromAuthentikAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<ReadAdministrationUsersFromAuthentikRequest, ReadAdministrationUsersFromAuthentikResponse>(
            new ReadAdministrationUsersFromAuthentikRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет CreateAdministrationUser через адресный request/response.
    public async Task<UserAdministrationRecord> CreateAdministrationUserAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<CreateAdministrationUserRequest, CreateAdministrationUserResponse>(
            new CreateAdministrationUserRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет UpdateAdministrationUser через адресный request/response.
    public async Task<UserAdministrationRecord> UpdateAdministrationUserAsync(Guid userId, UserAdministrationSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<UpdateAdministrationUserRequest, UpdateAdministrationUserResponse>(
            new UpdateAdministrationUserRequest(Guid.NewGuid(), userId, request), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет DeleteAdministrationUser через адресный request/response.
    public async Task<bool> DeleteAdministrationUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<DeleteAdministrationUserRequest, DeleteAdministrationUserResponse>(
            new DeleteAdministrationUserRequest(Guid.NewGuid(), userId), cancellationToken);
        return response.Result;
    }

    // Выполняет GetRoles через адресный request/response.
    public async Task<IReadOnlyList<UserRoleRecord>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetRolesRequest, GetRolesResponse>(
            new GetRolesRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет CreateRole через адресный request/response.
    public async Task<UserRoleRecord> CreateRoleAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<CreateRoleRequest, CreateRoleResponse>(
            new CreateRoleRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет UpdateRole через адресный request/response.
    public async Task<UserRoleRecord> UpdateRoleAsync(Guid roleId, UserRoleSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<UpdateRoleRequest, UpdateRoleResponse>(
            new UpdateRoleRequest(Guid.NewGuid(), roleId, request), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет DeleteRole через адресный request/response.
    public async Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<DeleteRoleRequest, DeleteRoleResponse>(
            new DeleteRoleRequest(Guid.NewGuid(), roleId), cancellationToken);
        return response.Result;
    }

    // Выполняет GetUserGroups через адресный request/response.
    public async Task<IReadOnlyList<UserGroupRecord>> GetUserGroupsAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetUserGroupsRequest, GetUserGroupsResponse>(
            new GetUserGroupsRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет CreateUserGroup через адресный request/response.
    public async Task<UserGroupRecord> CreateUserGroupAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<CreateUserGroupRequest, CreateUserGroupResponse>(
            new CreateUserGroupRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет UpdateUserGroup через адресный request/response.
    public async Task<UserGroupRecord> UpdateUserGroupAsync(Guid groupId, UserGroupSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<UpdateUserGroupRequest, UpdateUserGroupResponse>(
            new UpdateUserGroupRequest(Guid.NewGuid(), groupId, request), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет DeleteUserGroup через адресный request/response.
    public async Task<bool> DeleteUserGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<DeleteUserGroupRequest, DeleteUserGroupResponse>(
            new DeleteUserGroupRequest(Guid.NewGuid(), groupId), cancellationToken);
        return response.Result;
    }

    // Выполняет GetUserGroupMemberships через адресный request/response.
    public async Task<IReadOnlyList<UserGroupMembershipRecord>> GetUserGroupMembershipsAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetUserGroupMembershipsRequest, GetUserGroupMembershipsResponse>(
            new GetUserGroupMembershipsRequest(Guid.NewGuid(), groupId), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет UpdateUserGroupMemberships через адресный request/response.
    public async Task<IReadOnlyList<UserGroupMembershipRecord>> UpdateUserGroupMembershipsAsync(Guid groupId, UserGroupMembershipSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<UpdateUserGroupMembershipsRequest, UpdateUserGroupMembershipsResponse>(
            new UpdateUserGroupMembershipsRequest(Guid.NewGuid(), groupId, request), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет GetRoleAssignmentSpaces через адресный request/response.
    public async Task<IReadOnlyList<UserSpaceRecord>> GetRoleAssignmentSpacesAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetRoleAssignmentSpacesRequest, GetRoleAssignmentSpacesResponse>(
            new GetRoleAssignmentSpacesRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет GetRoleAssignments через адресный request/response.
    public async Task<IReadOnlyList<UserRoleAssignmentRecord>> GetRoleAssignmentsAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetRoleAssignmentsRequest, GetRoleAssignmentsResponse>(
            new GetRoleAssignmentsRequest(Guid.NewGuid(), spaceId), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет CreateRoleAssignment через адресный request/response.
    public async Task<UserRoleAssignmentRecord> CreateRoleAssignmentAsync(Guid spaceId, UserRoleAssignmentSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<CreateRoleAssignmentRequest, CreateRoleAssignmentResponse>(
            new CreateRoleAssignmentRequest(Guid.NewGuid(), spaceId, request), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет DeleteRoleAssignment через адресный request/response.
    public async Task<bool> DeleteRoleAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<DeleteRoleAssignmentRequest, DeleteRoleAssignmentResponse>(
            new DeleteRoleAssignmentRequest(Guid.NewGuid(), assignmentId), cancellationToken);
        return response.Result;
    }

    // Выполняет GetProfile через адресный request/response.
    public async Task<UserProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetProfileRequest, GetProfileResponse>(
            new GetProfileRequest(Guid.NewGuid()), cancellationToken);
        return response.Result;
    }

    // Выполняет UpdateProfile через адресный request/response.
    public async Task<UserProfileDto> UpdateProfileAsync(UserProfileUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<UpdateProfileRequest, UpdateProfileResponse>(
            new UpdateProfileRequest(Guid.NewGuid(), request), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет GetGroups через адресный request/response.
    public async Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetGroupsRequest, GetGroupsResponse>(
            new GetGroupsRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет IsInGroup через адресный request/response.
    public async Task<bool> IsInGroupAsync(string groupName, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<IsInGroupRequest, IsInGroupResponse>(
            new IsInGroupRequest(Guid.NewGuid(), groupName), cancellationToken);
        return response.Result;
    }

    // Выполняет GetRichDocBookmarks через адресный request/response.
    public async Task<IReadOnlyList<RichTextDocumentBookmark>> GetRichDocBookmarksAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetRichDocBookmarksRequest, GetRichDocBookmarksResponse>(
            new GetRichDocBookmarksRequest(Guid.NewGuid(), documentId), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет AddRichDocBookmark через адресный request/response.
    public async Task<RichTextDocumentBookmark?> AddRichDocBookmarkAsync(Guid documentId, RichTextDocumentTextSelection? selection, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<AddRichDocBookmarkRequest, AddRichDocBookmarkResponse>(
            new AddRichDocBookmarkRequest(Guid.NewGuid(), documentId, selection), cancellationToken);
        return response.Result;
    }

    // Выполняет DeleteRichDocBookmark через адресный request/response.
    public async Task<bool> DeleteRichDocBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<DeleteRichDocBookmarkRequest, DeleteRichDocBookmarkResponse>(
            new DeleteRichDocBookmarkRequest(Guid.NewGuid(), bookmarkId), cancellationToken);
        return response.Result;
    }

    // Выполняет GetRichDocDisplayedLevel через адресный request/response.
    public async Task<int> GetRichDocDisplayedLevelAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetRichDocDisplayedLevelRequest, GetRichDocDisplayedLevelResponse>(
            new GetRichDocDisplayedLevelRequest(Guid.NewGuid(), documentId), cancellationToken);
        return response.Result;
    }

    // Выполняет SaveRichDocDisplayedLevel через адресный request/response.
    public async Task SaveRichDocDisplayedLevelAsync(Guid documentId, int displayLevelCount, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<SaveRichDocDisplayedLevelRequest, SaveRichDocDisplayedLevelResponse>(
            new SaveRichDocDisplayedLevelRequest(Guid.NewGuid(), documentId, displayLevelCount), cancellationToken);
    }

    // Выполняет GetCurrentUserProperties через адресный request/response.
    public async Task<IReadOnlyList<UserPropertyDto>> GetCurrentUserPropertiesAsync(int propertyType, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetCurrentUserPropertiesRequest, GetCurrentUserPropertiesResponse>(
            new GetCurrentUserPropertiesRequest(Guid.NewGuid(), propertyType), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет UpsertCurrentUserProperty через адресный request/response.
    public async Task<UserPropertyDto?> UpsertCurrentUserPropertyAsync(Guid? propertyId, int propertyType, string data, string metadata, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<UpsertCurrentUserPropertyRequest, UpsertCurrentUserPropertyResponse>(
            new UpsertCurrentUserPropertyRequest(Guid.NewGuid(), propertyId, propertyType, data, metadata), cancellationToken);
        return response.Result;
    }

    // Выполняет DeleteCurrentUserProperty через адресный request/response.
    public async Task<bool> DeleteCurrentUserPropertyAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<DeleteCurrentUserPropertyRequest, DeleteCurrentUserPropertyResponse>(
            new DeleteCurrentUserPropertyRequest(Guid.NewGuid(), propertyId), cancellationToken);
        return response.Result;
    }

    // Выполняет GetDocPrintPresets через адресный request/response.
    public async Task<DocPrintSettingsPresetCollection> GetDocPrintPresetsAsync(CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetDocPrintPresetsRequest, GetDocPrintPresetsResponse>(
            new GetDocPrintPresetsRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет SaveDocPrintPreset через адресный request/response.
    public async Task<DocPrintSettingsPreset> SaveDocPrintPresetAsync(DocPrintSettingsPreset preset, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<SaveDocPrintPresetRequest, SaveDocPrintPresetResponse>(
            new SaveDocPrintPresetRequest(Guid.NewGuid(), preset), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет DeleteDocPrintPreset через адресный request/response.
    public async Task<bool> DeleteDocPrintPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<DeleteDocPrintPresetRequest, DeleteDocPrintPresetResponse>(
            new DeleteDocPrintPresetRequest(Guid.NewGuid(), presetName), cancellationToken);
        return response.Result;
    }

    // Выполняет GetCurrentUserPermissionsForSpace через адресный request/response.
    public async Task<UserEffectivePermissions> GetCurrentUserPermissionsForSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetCurrentUserPermissionsForSpaceRequest, GetCurrentUserPermissionsForSpaceResponse>(
            new GetCurrentUserPermissionsForSpaceRequest(Guid.NewGuid(), spaceId), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет GetCurrentUserPermissionsForEntity через адресный request/response.
    public async Task<UserEffectivePermissions> GetCurrentUserPermissionsForEntityAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetCurrentUserPermissionsForEntityRequest, GetCurrentUserPermissionsForEntityResponse>(
            new GetCurrentUserPermissionsForEntityRequest(Guid.NewGuid(), entityId), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет GetCurrentUserContentAccessForEntity через адресный request/response.
    public async Task<UserContentAccessDecision> GetCurrentUserContentAccessForEntityAsync(UserContentAccessRequest request, CancellationToken cancellationToken = default)
    {
        if (!_scope.UserId.HasValue) await EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetCurrentUserContentAccessForEntityRequest, GetCurrentUserContentAccessForEntityResponse>(
            new GetCurrentUserContentAccessForEntityRequest(Guid.NewGuid(), request), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет GetAnonymousPermissionsForSpace через адресный request/response.
    public async Task<UserEffectivePermissions> GetAnonymousPermissionsForSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendAsync<GetAnonymousPermissionsForSpaceRequest, GetAnonymousPermissionsForSpaceResponse>(
            new GetAnonymousPermissionsForSpaceRequest(Guid.NewGuid(), spaceId), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет GetAnonymousAccessibleSpaces через адресный request/response.
    public async Task<IReadOnlyList<UserSpaceRecord>> GetAnonymousAccessibleSpacesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.SendAsync<GetAnonymousAccessibleSpacesRequest, GetAnonymousAccessibleSpacesResponse>(
            new GetAnonymousAccessibleSpacesRequest(Guid.NewGuid()), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет GetAnonymousAccessibleDocuments через адресный request/response.
    public async Task<IReadOnlyList<UserAccessibleDocumentRecord>> GetAnonymousAccessibleDocumentsAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendAsync<GetAnonymousAccessibleDocumentsRequest, GetAnonymousAccessibleDocumentsResponse>(
            new GetAnonymousAccessibleDocumentsRequest(Guid.NewGuid(), spaceId), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }
}

using BusinessEntity.Services;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;

namespace BusinessEntity.MiniApps.UserMiniApp.Contracts.Messages;

// Запрос операции GetCurrentUser.
public sealed record GetCurrentUserRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции GetCurrentUser или явный отказ.
public sealed record GetCurrentUserResponse(Guid RequestId, BusinessEntityUser? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции EnsureCurrentUser.
public sealed record EnsureCurrentUserRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции EnsureCurrentUser или явный отказ.
public sealed record EnsureCurrentUserResponse(Guid RequestId, UserDto? Result, BusError? Error = null) : BusResponse(RequestId, Error)
{
    // Серверные полномочия Authentik admin/akadmin передаются отдельно от редактируемого профиля.
    public bool HasApplicationAdminAuthority { get; init; }
}

// Запрос операции DeleteCurrentUser.
public sealed record DeleteCurrentUserRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции DeleteCurrentUser или явный отказ.
public sealed record DeleteCurrentUserResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetAdministrationUsers.
public sealed record GetAdministrationUsersRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции GetAdministrationUsers или явный отказ.
public sealed record GetAdministrationUsersResponse(Guid RequestId, IReadOnlyList<UserAdministrationRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции ReadAdministrationUsersFromAuthentik.
public sealed record ReadAdministrationUsersFromAuthentikRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции ReadAdministrationUsersFromAuthentik или явный отказ.
public sealed record ReadAdministrationUsersFromAuthentikResponse(Guid RequestId, IReadOnlyList<UserAdministrationRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции CreateAdministrationUser.
public sealed record CreateAdministrationUserRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции CreateAdministrationUser или явный отказ.
public sealed record CreateAdministrationUserResponse(Guid RequestId, UserAdministrationRecord? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции UpdateAdministrationUser.
public sealed record UpdateAdministrationUserRequest(Guid RequestId, Guid UserId, UserAdministrationSaveRequest Request) : BusRequest(RequestId);
// Результат операции UpdateAdministrationUser или явный отказ.
public sealed record UpdateAdministrationUserResponse(Guid RequestId, UserAdministrationRecord? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции DeleteAdministrationUser.
public sealed record DeleteAdministrationUserRequest(Guid RequestId, Guid UserId) : BusRequest(RequestId);
// Результат операции DeleteAdministrationUser или явный отказ.
public sealed record DeleteAdministrationUserResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetRoles.
public sealed record GetRolesRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции GetRoles или явный отказ.
public sealed record GetRolesResponse(Guid RequestId, IReadOnlyList<UserRoleRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции CreateRole.
public sealed record CreateRoleRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции CreateRole или явный отказ.
public sealed record CreateRoleResponse(Guid RequestId, UserRoleRecord? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции UpdateRole.
public sealed record UpdateRoleRequest(Guid RequestId, Guid RoleId, UserRoleSaveRequest Request) : BusRequest(RequestId);
// Результат операции UpdateRole или явный отказ.
public sealed record UpdateRoleResponse(Guid RequestId, UserRoleRecord? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции DeleteRole.
public sealed record DeleteRoleRequest(Guid RequestId, Guid RoleId) : BusRequest(RequestId);
// Результат операции DeleteRole или явный отказ.
public sealed record DeleteRoleResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetUserGroups.
public sealed record GetUserGroupsRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции GetUserGroups или явный отказ.
public sealed record GetUserGroupsResponse(Guid RequestId, IReadOnlyList<UserGroupRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции CreateUserGroup.
public sealed record CreateUserGroupRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции CreateUserGroup или явный отказ.
public sealed record CreateUserGroupResponse(Guid RequestId, UserGroupRecord? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции UpdateUserGroup.
public sealed record UpdateUserGroupRequest(Guid RequestId, Guid GroupId, UserGroupSaveRequest Request) : BusRequest(RequestId);
// Результат операции UpdateUserGroup или явный отказ.
public sealed record UpdateUserGroupResponse(Guid RequestId, UserGroupRecord? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции DeleteUserGroup.
public sealed record DeleteUserGroupRequest(Guid RequestId, Guid GroupId) : BusRequest(RequestId);
// Результат операции DeleteUserGroup или явный отказ.
public sealed record DeleteUserGroupResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetUserGroupMemberships.
public sealed record GetUserGroupMembershipsRequest(Guid RequestId, Guid GroupId) : BusRequest(RequestId);
// Результат операции GetUserGroupMemberships или явный отказ.
public sealed record GetUserGroupMembershipsResponse(Guid RequestId, IReadOnlyList<UserGroupMembershipRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции UpdateUserGroupMemberships.
public sealed record UpdateUserGroupMembershipsRequest(Guid RequestId, Guid GroupId, UserGroupMembershipSaveRequest Request) : BusRequest(RequestId);
// Результат операции UpdateUserGroupMemberships или явный отказ.
public sealed record UpdateUserGroupMembershipsResponse(Guid RequestId, IReadOnlyList<UserGroupMembershipRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetRoleAssignmentSpaces.
public sealed record GetRoleAssignmentSpacesRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции GetRoleAssignmentSpaces или явный отказ.
public sealed record GetRoleAssignmentSpacesResponse(Guid RequestId, IReadOnlyList<UserSpaceRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetRoleAssignments.
public sealed record GetRoleAssignmentsRequest(Guid RequestId, Guid SpaceId) : BusRequest(RequestId);
// Результат операции GetRoleAssignments или явный отказ.
public sealed record GetRoleAssignmentsResponse(Guid RequestId, IReadOnlyList<UserRoleAssignmentRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции CreateRoleAssignment.
public sealed record CreateRoleAssignmentRequest(Guid RequestId, Guid SpaceId, UserRoleAssignmentSaveRequest Request) : BusRequest(RequestId);
// Результат операции CreateRoleAssignment или явный отказ.
public sealed record CreateRoleAssignmentResponse(Guid RequestId, UserRoleAssignmentRecord? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции DeleteRoleAssignment.
public sealed record DeleteRoleAssignmentRequest(Guid RequestId, Guid AssignmentId) : BusRequest(RequestId);
// Результат операции DeleteRoleAssignment или явный отказ.
public sealed record DeleteRoleAssignmentResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetProfile.
public sealed record GetProfileRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции GetProfile или явный отказ.
public sealed record GetProfileResponse(Guid RequestId, UserProfileDto? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции UpdateProfile.
public sealed record UpdateProfileRequest(Guid RequestId, UserProfileUpdateRequest Request) : BusRequest(RequestId);
// Результат операции UpdateProfile или явный отказ.
public sealed record UpdateProfileResponse(Guid RequestId, UserProfileDto? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetGroups.
public sealed record GetGroupsRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции GetGroups или явный отказ.
public sealed record GetGroupsResponse(Guid RequestId, IReadOnlyList<string>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции IsInGroup.
public sealed record IsInGroupRequest(Guid RequestId, string GroupName) : BusRequest(RequestId);
// Результат операции IsInGroup или явный отказ.
public sealed record IsInGroupResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetRichDocBookmarks.
public sealed record GetRichDocBookmarksRequest(Guid RequestId, Guid DocumentId) : BusRequest(RequestId);
// Результат операции GetRichDocBookmarks или явный отказ.
public sealed record GetRichDocBookmarksResponse(Guid RequestId, IReadOnlyList<RichTextDocumentBookmark>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции AddRichDocBookmark.
public sealed record AddRichDocBookmarkRequest(Guid RequestId, Guid DocumentId, RichTextDocumentTextSelection? Selection) : BusRequest(RequestId);
// Результат операции AddRichDocBookmark или явный отказ.
public sealed record AddRichDocBookmarkResponse(Guid RequestId, RichTextDocumentBookmark? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции DeleteRichDocBookmark.
public sealed record DeleteRichDocBookmarkRequest(Guid RequestId, Guid BookmarkId) : BusRequest(RequestId);
// Результат операции DeleteRichDocBookmark или явный отказ.
public sealed record DeleteRichDocBookmarkResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetRichDocDisplayedLevel.
public sealed record GetRichDocDisplayedLevelRequest(Guid RequestId, Guid DocumentId) : BusRequest(RequestId);
// Результат операции GetRichDocDisplayedLevel или явный отказ.
public sealed record GetRichDocDisplayedLevelResponse(Guid RequestId, int Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции SaveRichDocDisplayedLevel.
public sealed record SaveRichDocDisplayedLevelRequest(Guid RequestId, Guid DocumentId, int DisplayLevelCount) : BusRequest(RequestId);
// Результат операции SaveRichDocDisplayedLevel или явный отказ.
public sealed record SaveRichDocDisplayedLevelResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetCurrentUserProperties.
public sealed record GetCurrentUserPropertiesRequest(Guid RequestId, int PropertyType) : BusRequest(RequestId);
// Результат операции GetCurrentUserProperties или явный отказ.
public sealed record GetCurrentUserPropertiesResponse(Guid RequestId, IReadOnlyList<UserPropertyDto>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции UpsertCurrentUserProperty.
public sealed record UpsertCurrentUserPropertyRequest(Guid RequestId, Guid? PropertyId, int PropertyType, string Data, string Metadata) : BusRequest(RequestId);
// Результат операции UpsertCurrentUserProperty или явный отказ.
public sealed record UpsertCurrentUserPropertyResponse(Guid RequestId, UserPropertyDto? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции DeleteCurrentUserProperty.
public sealed record DeleteCurrentUserPropertyRequest(Guid RequestId, Guid PropertyId) : BusRequest(RequestId);
// Результат операции DeleteCurrentUserProperty или явный отказ.
public sealed record DeleteCurrentUserPropertyResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetDocPrintPresets.
public sealed record GetDocPrintPresetsRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции GetDocPrintPresets или явный отказ.
public sealed record GetDocPrintPresetsResponse(Guid RequestId, DocPrintSettingsPresetCollection? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции SaveDocPrintPreset.
public sealed record SaveDocPrintPresetRequest(Guid RequestId, DocPrintSettingsPreset Preset) : BusRequest(RequestId);
// Результат операции SaveDocPrintPreset или явный отказ.
public sealed record SaveDocPrintPresetResponse(Guid RequestId, DocPrintSettingsPreset? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции DeleteDocPrintPreset.
public sealed record DeleteDocPrintPresetRequest(Guid RequestId, string PresetName) : BusRequest(RequestId);
// Результат операции DeleteDocPrintPreset или явный отказ.
public sealed record DeleteDocPrintPresetResponse(Guid RequestId, bool Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetCurrentUserPermissionsForSpace.
public sealed record GetCurrentUserPermissionsForSpaceRequest(Guid RequestId, Guid SpaceId) : BusRequest(RequestId);
// Результат операции GetCurrentUserPermissionsForSpace или явный отказ.
public sealed record GetCurrentUserPermissionsForSpaceResponse(Guid RequestId, UserEffectivePermissions? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetCurrentUserPermissionsForEntity.
public sealed record GetCurrentUserPermissionsForEntityRequest(Guid RequestId, Guid EntityId) : BusRequest(RequestId);
// Результат операции GetCurrentUserPermissionsForEntity или явный отказ.
public sealed record GetCurrentUserPermissionsForEntityResponse(Guid RequestId, UserEffectivePermissions? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetCurrentUserContentAccessForEntity.
public sealed record GetCurrentUserContentAccessForEntityRequest(Guid RequestId, UserContentAccessRequest Request) : BusRequest(RequestId);
// Результат операции GetCurrentUserContentAccessForEntity или явный отказ.
public sealed record GetCurrentUserContentAccessForEntityResponse(Guid RequestId, UserContentAccessDecision? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetAnonymousPermissionsForSpace.
public sealed record GetAnonymousPermissionsForSpaceRequest(Guid RequestId, Guid SpaceId) : BusRequest(RequestId);
// Результат операции GetAnonymousPermissionsForSpace или явный отказ.
public sealed record GetAnonymousPermissionsForSpaceResponse(Guid RequestId, UserEffectivePermissions? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetAnonymousAccessibleSpaces.
public sealed record GetAnonymousAccessibleSpacesRequest(Guid RequestId) : BusRequest(RequestId);
// Результат операции GetAnonymousAccessibleSpaces или явный отказ.
public sealed record GetAnonymousAccessibleSpacesResponse(Guid RequestId, IReadOnlyList<UserSpaceRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции GetAnonymousAccessibleDocuments.
public sealed record GetAnonymousAccessibleDocumentsRequest(Guid RequestId, Guid SpaceId) : BusRequest(RequestId);
// Результат операции GetAnonymousAccessibleDocuments или явный отказ.
public sealed record GetAnonymousAccessibleDocumentsResponse(Guid RequestId, IReadOnlyList<UserAccessibleDocumentRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Права на каноническую сущность проверяются сервером по идентификатору.
public sealed record GetEntityContentAccessRequest(Guid RequestId, Guid EntityId) : BusRequest(RequestId);
// Решение владельца прав, не зависящее от изменяемой карточки UI.
public sealed record GetEntityContentAccessResponse(Guid RequestId, UserContentAccessDecision? Result, BusError? Error = null) : BusResponse(RequestId, Error);

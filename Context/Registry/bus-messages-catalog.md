# Каталог сообщений общей шины

Фактическое состояние этапа 1 TASK_ARCH_001, 2026-10-28.
Один singleton ReactiveUI.IMessageBus; регистрации маршрутов находятся в Registration владельцев.

Все предметные Request/Response наследуют BusRequest/BusResponse.
Ответ повторяет полный адрес запроса; совпадения RequestId недостаточно. Клиент подставляет
проверенный сервером BusRequestContext: ScopeId, UserId, LogicalWindowId, ModuleId,
EditSessionId, ContextId, IsSystem. Модули и edit-session начинают использоваться
менеджером этапа 4; до этого circuit/HTTP/фоновый scope имеет собственный адрес.
Scoped User/Tree/Activity фильтруют scope и окно до исполнения и снимают подписки
при disposal. DataProvider — один общий singleton-владелец запросов хранения.

Ошибки: BusError(Code, Message), BusRequestFailedException; timeout 30 секунд
и отмена завершают ожидание, не доказывая откат сохранения.
UserMessages: адрес пользователя и окна, CorrelationId для однократной доставки.
Login передаёт уведомление новому circuit одноразовым ключом со сроком 2 минуты.

| Владелец | Сообщение | Вид | Собственные поля | Источник |
|---|---|---|---|---|
| ActivityMiniApp | GetCommentsRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| ActivityMiniApp | GetCommentsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityCommentRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| ActivityMiniApp | CreateCommentRequest | Request | `Guid RequestId, BusinessEntityCommentCreateRequest Request` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| ActivityMiniApp | CreateCommentResponse | Response | `Guid RequestId, BusinessEntityCommentRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| DataProviderMiniApp | GetBusinessEntitiesRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntitiesResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity> Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityByIdRequest | Request | `Guid RequestId, Guid Id` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityByIdResponse | Response | `Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity? Record, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataResponse | Response | `Guid RequestId, IBusinessEntityData? Record, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataVersionRequest | Request | `Guid RequestId, Guid BusinessEntityId, int Version` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataVersionResponse | Response | `Guid RequestId, IBusinessEntityData? Record, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataVersionsRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataVersionsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityDataVersionInfo> Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityDataVersionDescriptionRequest | Request | `Guid RequestId, Guid BusinessEntityId, int Version, string? VersionDescription` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityDataVersionDescriptionResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | AddBusinessEntityRequest | Request | `Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity Record` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | AddBusinessEntityResponse | Response | `Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity? Record, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityRequest | Request | `Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity Record` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteBusinessEntityRequest | Request | `Guid RequestId, Guid Id` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteBusinessEntityResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | ClearDataProviderStorageRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | ClearDataProviderStorageResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityDataRequest | Request | `Guid RequestId, Guid BusinessEntityId, IBusinessEntityData Data, bool HasVersions = false, Guid? LastModifiedByUserId = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityDataResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetAllRelationsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetAllRelationsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationsRequest | Request | `Guid RequestId, Guid ObjectAId, Guid ObjectBId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationByIdRequest | Request | `Guid RequestId, Guid Id` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationByIdResponse | Response | `Guid RequestId, BusinessEntityRelation? Record, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | CreateRelationRequest | Request | `Guid RequestId, BusinessEntityRelation Record` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | CreateRelationResponse | Response | `Guid RequestId, BusinessEntityRelation? Record, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateRelationRequest | Request | `Guid RequestId, BusinessEntityRelation Record` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateRelationResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteRelationRequest | Request | `Guid RequestId, Guid Id` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteRelationResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextChunksRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextChunksResponse | Response | `Guid RequestId, IReadOnlyList<RichTextDocumentChunk> Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextTableOfContentsEntriesRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextTableOfContentsEntriesResponse | Response | `Guid RequestId, IReadOnlyList<RichTextDocumentTableOfContentsEntry> Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | RebuildRichTextTableOfContentsEntriesRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | RebuildRichTextTableOfContentsEntriesResponse | Response | `Guid RequestId, IReadOnlyList<RichTextDocumentTableOfContentsEntry> Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | ReplaceRichTextChunksRequest | Request | `Guid RequestId, Guid BusinessEntityId, IReadOnlyList<RichTextDocumentChunk> Records` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | ReplaceRichTextChunksResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | SaveRichTextEmbeddedFilesRequest | Request | `Guid RequestId, Guid BusinessEntityId, IReadOnlyList<RichTextEmbeddedFile> Files, bool ReplaceExistingFiles` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | SaveRichTextEmbeddedFilesResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextEmbeddedFileRequest | Request | `Guid RequestId, Guid BusinessEntityId, string ImageId, string Variant` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextEmbeddedFileResponse | Response | `Guid RequestId, RichTextEmbeddedFileContent? Record, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteRichTextStorageRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteRichTextStorageResponse | Response | `Guid RequestId, bool Success, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| TreeMiniApp | TreeNodeSnapshot | Event | `BusinessEntity.Core.Classes.BusinessEntity Entity, IReadOnlyList<TreeNodeSnapshot> Children` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| TreeMiniApp | TreeSpaceSnapshot | Event | `BusinessEntity.Core.Classes.BusinessEntity Space, IReadOnlyList<TreeNodeSnapshot> Children` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| TreeMiniApp | GetTreeForSpaceRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| TreeMiniApp | GetTreeForSpaceResponse | Response | `Guid RequestId, TreeSpaceSnapshot? Snapshot, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| UserMessagesMiniApp | ClearUserMessages | Command | `Guid UserId, Guid LogicalWindowId` | [код](../../BusinessEntity/MiniApps/UserMessagesMiniApp/Contracts/ClearUserMessages.cs) |
| UserMessagesMiniApp | PostUserMessage | Command | `Guid UserId, Guid LogicalWindowId, Guid CorrelationId, string Text, UserMessageLevel Level = UserMessageLevel.Info, string? Title = null` | [код](../../BusinessEntity/MiniApps/UserMessagesMiniApp/Contracts/PostUserMessage.cs) |
| UserMiniApp | GetCurrentUserRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserResponse | Response | `Guid RequestId, BusinessEntityUser? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | EnsureCurrentUserRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | EnsureCurrentUserResponse | Response | `Guid RequestId, UserDto? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteCurrentUserRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteCurrentUserResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAdministrationUsersRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAdministrationUsersResponse | Response | `Guid RequestId, IReadOnlyList<UserAdministrationRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | ReadAdministrationUsersFromAuthentikRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | ReadAdministrationUsersFromAuthentikResponse | Response | `Guid RequestId, IReadOnlyList<UserAdministrationRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateAdministrationUserRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateAdministrationUserResponse | Response | `Guid RequestId, UserAdministrationRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateAdministrationUserRequest | Request | `Guid RequestId, Guid UserId, UserAdministrationSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateAdministrationUserResponse | Response | `Guid RequestId, UserAdministrationRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteAdministrationUserRequest | Request | `Guid RequestId, Guid UserId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteAdministrationUserResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRolesRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRolesResponse | Response | `Guid RequestId, IReadOnlyList<UserRoleRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateRoleRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateRoleResponse | Response | `Guid RequestId, UserRoleRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateRoleRequest | Request | `Guid RequestId, Guid RoleId, UserRoleSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateRoleResponse | Response | `Guid RequestId, UserRoleRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRoleRequest | Request | `Guid RequestId, Guid RoleId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRoleResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetUserGroupsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetUserGroupsResponse | Response | `Guid RequestId, IReadOnlyList<UserGroupRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateUserGroupRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateUserGroupResponse | Response | `Guid RequestId, UserGroupRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateUserGroupRequest | Request | `Guid RequestId, Guid GroupId, UserGroupSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateUserGroupResponse | Response | `Guid RequestId, UserGroupRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteUserGroupRequest | Request | `Guid RequestId, Guid GroupId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteUserGroupResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetUserGroupMembershipsRequest | Request | `Guid RequestId, Guid GroupId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetUserGroupMembershipsResponse | Response | `Guid RequestId, IReadOnlyList<UserGroupMembershipRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateUserGroupMembershipsRequest | Request | `Guid RequestId, Guid GroupId, UserGroupMembershipSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateUserGroupMembershipsResponse | Response | `Guid RequestId, IReadOnlyList<UserGroupMembershipRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRoleAssignmentSpacesRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRoleAssignmentSpacesResponse | Response | `Guid RequestId, IReadOnlyList<UserSpaceRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRoleAssignmentsRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRoleAssignmentsResponse | Response | `Guid RequestId, IReadOnlyList<UserRoleAssignmentRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateRoleAssignmentRequest | Request | `Guid RequestId, Guid SpaceId, UserRoleAssignmentSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateRoleAssignmentResponse | Response | `Guid RequestId, UserRoleAssignmentRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRoleAssignmentRequest | Request | `Guid RequestId, Guid AssignmentId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRoleAssignmentResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetProfileRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetProfileResponse | Response | `Guid RequestId, UserProfileDto? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateProfileRequest | Request | `Guid RequestId, UserProfileUpdateRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateProfileResponse | Response | `Guid RequestId, UserProfileDto? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetGroupsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetGroupsResponse | Response | `Guid RequestId, IReadOnlyList<string>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | IsInGroupRequest | Request | `Guid RequestId, string GroupName` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | IsInGroupResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRichDocBookmarksRequest | Request | `Guid RequestId, Guid DocumentId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRichDocBookmarksResponse | Response | `Guid RequestId, IReadOnlyList<RichTextDocumentBookmark>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | AddRichDocBookmarkRequest | Request | `Guid RequestId, Guid DocumentId, RichTextDocumentTextSelection? Selection` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | AddRichDocBookmarkResponse | Response | `Guid RequestId, RichTextDocumentBookmark? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRichDocBookmarkRequest | Request | `Guid RequestId, Guid BookmarkId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRichDocBookmarkResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRichDocDisplayedLevelRequest | Request | `Guid RequestId, Guid DocumentId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRichDocDisplayedLevelResponse | Response | `Guid RequestId, int Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | SaveRichDocDisplayedLevelRequest | Request | `Guid RequestId, Guid DocumentId, int DisplayLevelCount` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | SaveRichDocDisplayedLevelResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPropertiesRequest | Request | `Guid RequestId, int PropertyType` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPropertiesResponse | Response | `Guid RequestId, IReadOnlyList<UserPropertyDto>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpsertCurrentUserPropertyRequest | Request | `Guid RequestId, Guid? PropertyId, int PropertyType, string Data, string Metadata` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpsertCurrentUserPropertyResponse | Response | `Guid RequestId, UserPropertyDto? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteCurrentUserPropertyRequest | Request | `Guid RequestId, Guid PropertyId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteCurrentUserPropertyResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetDocPrintPresetsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetDocPrintPresetsResponse | Response | `Guid RequestId, DocPrintSettingsPresetCollection? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | SaveDocPrintPresetRequest | Request | `Guid RequestId, DocPrintSettingsPreset Preset` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | SaveDocPrintPresetResponse | Response | `Guid RequestId, DocPrintSettingsPreset? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteDocPrintPresetRequest | Request | `Guid RequestId, string PresetName` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteDocPrintPresetResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPermissionsForSpaceRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPermissionsForSpaceResponse | Response | `Guid RequestId, UserEffectivePermissions? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPermissionsForEntityRequest | Request | `Guid RequestId, Guid EntityId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPermissionsForEntityResponse | Response | `Guid RequestId, UserEffectivePermissions? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserContentAccessForEntityRequest | Request | `Guid RequestId, UserContentAccessRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserContentAccessForEntityResponse | Response | `Guid RequestId, UserContentAccessDecision? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousPermissionsForSpaceRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousPermissionsForSpaceResponse | Response | `Guid RequestId, UserEffectivePermissions? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousAccessibleSpacesRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousAccessibleSpacesResponse | Response | `Guid RequestId, IReadOnlyList<UserSpaceRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousAccessibleDocumentsRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousAccessibleDocumentsResponse | Response | `Guid RequestId, IReadOnlyList<UserAccessibleDocumentRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| Services (удаляется на этапе 2) | EntityUpdatedMessage | Event | `BusinessEntity.Core.Classes.BusinessEntity EntityData` | [код](../../BusinessEntity/Services/EntityMessages.cs) |
| Services (удаляется на этапе 2) | EntityCreatedMessage | Event | `BusinessEntity.Core.Classes.BusinessEntity EntityData` | [код](../../BusinessEntity/Services/EntityMessages.cs) |
| Services (удаляется на этапе 2) | EntityDeletedMessage | Event | `Guid EntityId` | [код](../../BusinessEntity/Services/EntityMessages.cs) |

## Следующие этапы

Этап 2: транзакции, исход OperationId, события только от DataProvider;
Services/EntityMessages удаляется. Этапы 4–8: декларации, жизненный цикл,
контексты сохранения, команды и выделение. Запланированные сообщения здесь
не выдаются за реализованные.

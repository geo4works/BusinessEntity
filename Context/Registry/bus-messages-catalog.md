# Каталог сообщений общей шины

Фактическое состояние этапа 6 TASK_ARCH_001, 2026-10-28.
Один singleton ReactiveUI.IMessageBus; регистрации маршрутов находятся в Registration владельцев.

Все предметные Request/Response наследуют BusRequest/BusResponse.
Ответ повторяет полный адрес запроса; совпадения RequestId недостаточно. Клиент подставляет
проверенный сервером BusRequestContext: ScopeId, UserId, LogicalWindowId, ModuleId,
EditSessionId, ContextId, IsSystem. Модули и edit-session начинают использоваться
менеджером этапа 4; до этого circuit/HTTP/фоновый scope имеет собственный адрес.
Scoped User/Tree/Activity фильтруют scope и окно до исполнения и снимают подписки
при disposal. DataProvider — один общий singleton-владелец запросов хранения.

Ошибки: BusError(Code, Message), BusRequestFailedException; timeout 30 секунд
и отмена завершают ожидание, не доказывая откат сохранения. Для export/import
и пакетов файлов явно используется 30 минут.
UserMessages: адрес пользователя и окна, CorrelationId для однократной доставки.
Login передаёт уведомление новому circuit одноразовым ключом со сроком 2 минуты.

| Владелец | Сообщение | Вид | Собственные поля | Источник |
|---|---|---|---|---|
| ActivityMiniApp | GetCommentsRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| ActivityMiniApp | GetCommentsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityCommentRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| ActivityMiniApp | CreateCommentRequest | Request | `Guid RequestId, BusinessEntityCommentCreateRequest Request` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| ActivityMiniApp | CreateCommentResponse | Response | `Guid RequestId, BusinessEntityCommentRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
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

| DataProviderMiniApp | BusinessEntityChanged | Event | `Guid OperationId, Guid EntityId, bool Deleted` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderChangeEvents.cs) |
| DataProviderMiniApp | BusinessEntityRelationChanged | Event | `Guid OperationId, Guid RelationId, bool Deleted` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderChangeEvents.cs) |
| DataProviderMiniApp | EntityBatchCommitted | Event | `Guid OperationId, IReadOnlyList<Guid> ChangedEntities, IReadOnlyList<Guid> DeletedEntities, IReadOnlyList<Guid> ChangedRelations, IReadOnlyList<Guid> DeletedRelations` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderChangeEvents.cs) |
| DataProviderMiniApp | ExportSubgraphRequest | Request | `Guid RequestId, BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives.SubgraphExportScope Scope` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | ExportSubgraphResponse | Response | `Guid RequestId, BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives.SubgraphExportResult? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | GetAllRelationsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetAllRelationsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
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
| DataProviderMiniApp | GetOperationOutcomeRequest | Request | `Guid RequestId, Guid OperationId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | GetOperationOutcomeResponse | Response | `Guid RequestId, StoredOperationOutcome? Outcome, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | GetRelationByIdRequest | Request | `Guid RequestId, Guid Id` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationByIdResponse | Response | `Guid RequestId, BusinessEntityRelation? Record, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationsRequest | Request | `Guid RequestId, Guid ObjectAId, Guid ObjectBId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextEmbeddedFileRequest | Request | `Guid RequestId, Guid BusinessEntityId, string ImageId, string Variant` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextEmbeddedFileResponse | Response | `Guid RequestId, RichTextEmbeddedFileContent? Record, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetStoredCommentsRequest | Request | `Guid RequestId, Guid EntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | GetStoredCommentsResponse | Response | `Guid RequestId, IReadOnlyList<StoredEntityComment>? Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | GetStoredFileRequest | Request | `Guid RequestId, Guid EntityId, string FileId, string Variant` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | GetStoredFileResponse | Response | `Guid RequestId, StoredEntityFile? File, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | GetStoredPropertiesRequest | Request | `Guid RequestId, EntityPropertyOwner Owner, Guid? ParentEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | GetStoredPropertiesResponse | Response | `Guid RequestId, IReadOnlyList<StoredEntityProperty>? Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | GetVersionChunkMembershipRequest | Request | `Guid RequestId, Guid EntityId, int Version` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | GetVersionChunkMembershipResponse | Response | `Guid RequestId, IReadOnlyList<VersionChunkMembership>? Records, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | GetVersionChunkWindowRequest | Request | `Guid RequestId, Guid EntityId, int Version, long Start, int Count, IReadOnlyList<Guid>? ChunkIds = null, string? SearchText = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | GetVersionChunkWindowResponse | Response | `Guid RequestId, RichTextDocumentChunkWindow? Window, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderReadMessages.cs) |
| DataProviderMiniApp | ReleaseExportRequest | Request | `Guid RequestId, Guid ExportId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | ReleaseExportResponse | Response | `Guid RequestId, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | SaveEntityBatchRequest | Request | `Guid RequestId, EntitySaveBatch Batch` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | SaveEntityBatchResponse | Response | `Guid RequestId, SaveBatchResult? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |

## Состояние раскатки

Этапы 0–7 завершены: транзакции/outcome, кэш, декларации, lifecycle, рабочие
контексты, UI-команды, предметные границы и master/helper реализованы.
Навигационный guard и beforeunload подключаются этапом 8.

## Проекции и статистика этапа 3

DataProvider: `QueryGraphRequest/Response` (All, Roots, Children, Subgraph,
PathToRoot, SearchName); `GetPublishedEntityNamesRequest/Response`;
`GetDataProviderCacheStatisticsRequest/Response`. Все используют общий
BusRequestClient. Структура выдаётся безопасными копиями одной ревизии;
права применяют Tree/User и владельцы страниц до отображения.

## Модульность этапа 4

`GiveMeModuleDefinitions(RequestId)` / `HereAreModuleDefinitions(RequestId,
MiniAppId, Definitions)` — сбор без Razor/Type. Подписка каталога сохраняется
для поздних ответов. `ModuleLifecycleChanged` переносит ModuleId, MiniAppId,
DefinitionId, LogicalWindowId, ContextId, EditSessionId и Opened/Activated/
Deactivated/Closed. Источник предметного Request устанавливается async-local
lease одного живого модуля; параллельные компоненты не меняют адрес друг друга.

## Команды этапа 5

GiveMeButtons(RequestId, HostId) / HereAreButtons(RequestId, MiniAppId, HostId,
Buttons). ButtonDefinition задаёт CommandId/владельца, место, title/icon/order,
право, route и ограничения типов/argument. Никаких runtime callback в декларации.

ButtonStateSnapshot: LogicalWindowId, ModuleId, EditSessionId, ContextId,
MiniAppId, BindingId, immutable states. ButtonCommandInvoked — BusRequest с
DeclarationOwner/CommandId/BindingId; ButtonCommandResult — BusResponse/BusError.
Endpoint scoped проверяет окно, module/edit/context, живой nonce, Enabled/Visible
и актуальное право. Runtime binding снимается вместе с компонентом/меню.

[Фактические UI-модули и маршруты](modules-and-routes.md).

## Предметные границы этапа 6

- Spaces: GetSpaceById/GetSpaces/GetGenericSpaceProperties Request/Response
  в Contracts/SpacesMessages.cs; scoped endpoint/connector.
- BackupRestore: RunManualBackup/RunBackupCycle/ClearSpaceBackup/
  GetDisplaySpaceBackupFolder/RestoreSpace Request/Response в Contracts/BackupRestoreMessages.cs.
- Media: MediaLibraryGetVideos Request/Response, только значения списка.
- Sample: ForceReseed Request/Response с серверной проверкой администратора.
- Tree: DeleteTreeEntities Request/Response; внешние страницы не вызывают Internal.
- User: GetEntityContentAccess Request/Response рассчитывает права по свежей карточке.
  Endpoint-ы разделены по accounts, preferences, roles, groups, assignments,
  assignment directory, access и session. Их жизни и адреса scoped.

BusRequestContext содержит также HasApplicationAdminAuthority: значение задаёт
серверный владелец identity, Scope сбрасывает его при смене UserId, BusRequestClient
перезаписывает входной Context целиком. Полномочия admin/akadmin/Authentik admin
согласованы с существующей политикой; локальные роли проверяются в SQL отдельно.

EntitySaveBatch.OwnedServiceChanges — техническое исключение служебных таблиц
User. Пакет содержит только value JSON своих DTO; writer зарегистрирован владельцем.
Канонический writer DataProvider вызывает его в своей SQL-транзакции и пишет
durable outcome там же. Пароли и callbacks в сообщениях отсутствуют.


## Выделение этапа 7

ObjectInContextSelected: LogicalWindowId, OriginModuleId, SelectionRevision,
SourceRevision, ImmutableArray<SelectedObjectSnapshot>. Значения объекта — ID,
заголовок, ключ типа и даты; mutable Entity/VM не передаются.
SelectionSourceChanged объявляет поколение текущего master. Менеджер окна хранит
только его адрес, без выделенных объектов. Закрытие/деактивация снимает источник.
GiveMeCurrentSelection(RequestId, LogicalWindowId, RequesterModuleId) /
HereIsCurrentSelection(RequestId, Snapshot) обслуживают поздний helper.
Проверяются окно, живые module IDs, текущий источник/поколение, ревизия и корреляция.

RevealTreeEntity Request/Response — адресное намерение страницы показать узел;
Tree connector использует общий BusRequestClient. Собственный TreeRevealService
связывает намерение с renderer Tree; он не содержит контекста выбранных объектов.
ITreeSelectionService/TreeSelectionService/TreeSelectedItem удалены.

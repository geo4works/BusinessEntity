# Каталог сообщений общей шины

Фактический пакет TASK_ARCH_001, 0.17.0, 2026-10-29. Таблица сверена с текущими
C# records; DTO значений и локальные результаты не выдаются за сообщения шины.

Один singleton ReactiveUI.IMessageBus. BusRequestClient выбирает endpoint по
регистрации владельца, подписывается до отправки и коррелирует RequestId плюс
полный серверный Context. Он содержит ScopeId, UserId, LogicalWindowId, ModuleId,
EditSessionId, ContextId, IsSystem и HasApplicationAdminAuthority. Входной browser
Context перезаписывается; identity/полномочия устанавливаются сервером.
Scoped endpoint фильтрует адрес до работы. Обычный timeout 30 секунд; большой
export/import имеет явный timeout. Отмена ожидания не доказывает SQL rollback.

BusError — стабильный Code/Message, законное отсутствие отделено от отказа.
Дополнительные init-свойства records и наследуемый Context описаны в коде по ссылке.

| Владелец | Сообщение | Вид | Поля конструктора | Источник |
|---|---|---|---|---|
| ActivityMiniApp | CreateCommentRequest | Request | `Guid RequestId, BusinessEntityCommentCreateRequest Request` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| ActivityMiniApp | CreateCommentResponse | Response | `Guid RequestId, BusinessEntityCommentRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| ActivityMiniApp | GetCommentsRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
| ActivityMiniApp | GetCommentsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityCommentRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/ActivityMiniApp/Contracts/Messages/ActivityMiniAppMessages.cs) |
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
| DataProviderMiniApp | GetDataProviderCacheStatisticsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderGraphMessages.cs) |
| DataProviderMiniApp | GetDataProviderCacheStatisticsResponse | Response | `Guid RequestId, DataProviderCacheStatistics? Statistics, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderGraphMessages.cs) |
| DataProviderMiniApp | GetOperationOutcomeRequest | Request | `Guid RequestId, Guid OperationId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | GetOperationOutcomeResponse | Response | `Guid RequestId, StoredOperationOutcome? Outcome, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | GetPublishedEntityNamesRequest | Request | `Guid RequestId, IReadOnlyList<Guid> EntityIds` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderGraphMessages.cs) |
| DataProviderMiniApp | GetPublishedEntityNamesResponse | Response | `Guid RequestId, IReadOnlyDictionary<Guid, string>? Names, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderGraphMessages.cs) |
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
| DataProviderMiniApp | QueryGraphRequest | Request | `Guid RequestId, GraphQueryKind Kind, Guid? RootId = null, string? SearchText = null, int Take = 100` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderGraphMessages.cs) |
| DataProviderMiniApp | QueryGraphResponse | Response | `Guid RequestId, GraphProjection? Projection, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderGraphMessages.cs) |
| DataProviderMiniApp | ReleaseExportRequest | Request | `Guid RequestId, Guid ExportId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | ReleaseExportResponse | Response | `Guid RequestId, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | SaveEntityBatchRequest | Request | `Guid RequestId, EntitySaveBatch Batch` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| DataProviderMiniApp | SaveEntityBatchResponse | Response | `Guid RequestId, SaveBatchResult? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderTransactionMessages.cs) |
| Infrastructure/Commands | ButtonCommandInvoked | Request | `Guid RequestId, string DeclarationOwner, string CommandId, Guid BindingId` | [код](../../BusinessEntity/Infrastructure/Commands/ButtonContracts.cs) |
| Infrastructure/Commands | ButtonCommandResult | Response | `Guid RequestId, BusError? Error = null` | [код](../../BusinessEntity/Infrastructure/Commands/ButtonContracts.cs) |
| Infrastructure/Commands | ButtonStateSnapshot | Event | `Guid LogicalWindowId, Guid ModuleId, Guid? EditSessionId, Guid? ContextId, string MiniAppId, Guid BindingId, IReadOnlyDictionary<string, ButtonState> States` | [код](../../BusinessEntity/Infrastructure/Commands/ButtonContracts.cs) |
| Infrastructure/Commands | GiveMeButtons | Declaration | `Guid RequestId, string HostId` | [код](../../BusinessEntity/Infrastructure/Commands/ButtonContracts.cs) |
| Infrastructure/Commands | HereAreButtons | Declaration | `Guid RequestId, string MiniAppId, string HostId, IReadOnlyList<ButtonDefinition> Buttons` | [код](../../BusinessEntity/Infrastructure/Commands/ButtonContracts.cs) |
| Infrastructure/Modularity | GiveMeModuleDefinitions | Declaration | `Guid RequestId` | [код](../../BusinessEntity/Infrastructure/Modularity/ModuleContracts.cs) |
| Infrastructure/Modularity | HereAreModuleDefinitions | Declaration | `Guid RequestId, string MiniAppId, IReadOnlyList<ModuleDefinition> Definitions` | [код](../../BusinessEntity/Infrastructure/Modularity/ModuleContracts.cs) |
| Infrastructure/Modularity | ModuleLifecycleChanged | Event | `Guid ModuleId, string MiniAppId, string DefinitionId, Guid LogicalWindowId, Guid? ContextId, Guid? EditSessionId, ModuleLifecycle Lifecycle` | [код](../../BusinessEntity/Infrastructure/Modularity/ModuleContracts.cs) |
| Infrastructure/Saving | MiniAppSaveStateChanged | Event | `Guid LogicalWindowId, Guid? UserId, string MiniAppId, Guid EditSessionId, Guid? ContextId, long StateRevision, long EditRevision, bool IsDirty, MiniAppSaveStatus Status, Guid? OperationId` | [код](../../BusinessEntity/Infrastructure/Saving/SaveContracts.cs) |
| Infrastructure/Selection | GiveMeCurrentSelection | Declaration | `Guid RequestId, Guid LogicalWindowId, Guid RequesterModuleId` | [код](../../BusinessEntity/Infrastructure/Selection/SelectionContracts.cs) |
| Infrastructure/Selection | HereIsCurrentSelection | Declaration | `Guid RequestId, ObjectInContextSelected Snapshot` | [код](../../BusinessEntity/Infrastructure/Selection/SelectionContracts.cs) |
| Infrastructure/Selection | ObjectInContextSelected | Event | `Guid LogicalWindowId, Guid OriginModuleId, long SelectionRevision, long SourceRevision, ImmutableArray<SelectedObjectSnapshot> Objects` | [код](../../BusinessEntity/Infrastructure/Selection/SelectionContracts.cs) |
| Infrastructure/Selection | SelectionSourceChanged | Event | `Guid LogicalWindowId, SelectionSourceAddress? Source` | [код](../../BusinessEntity/Infrastructure/Selection/SelectionContracts.cs) |
| MediaServerMiniApp | MediaLibraryGetVideosRequest | Request | `Guid RequestId, Guid? SpaceId, string? Filter` | [код](../../BusinessEntity/MiniApps/MediaServerMiniApp/Contracts/MediaLibraryMessages.cs) |
| MediaServerMiniApp | MediaLibraryGetVideosResponse | Response | `Guid RequestId, IReadOnlyList<MediaVideoInfo> Value, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/MediaServerMiniApp/Contracts/MediaLibraryMessages.cs) |
| SampleDataMiniApp | SampleDataForceReseedRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/SampleDataMiniApp/Contracts/SampleDataMessages.cs) |
| SampleDataMiniApp | SampleDataForceReseedResponse | Response | `Guid RequestId, bool Value, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/SampleDataMiniApp/Contracts/SampleDataMessages.cs) |
| SpacesMiniApp | SpacesGetGenericSpacePropertiesRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/SpacesMiniApp/Contracts/SpacesMessages.cs) |
| SpacesMiniApp | SpacesGetGenericSpacePropertiesResponse | Response | `Guid RequestId, GenericSpaceProperties Value, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/SpacesMiniApp/Contracts/SpacesMessages.cs) |
| SpacesMiniApp | SpacesGetSpaceByIdRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/SpacesMiniApp/Contracts/SpacesMessages.cs) |
| SpacesMiniApp | SpacesGetSpaceByIdResponse | Response | `Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity? Value, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/SpacesMiniApp/Contracts/SpacesMessages.cs) |
| SpacesMiniApp | SpacesGetSpacesRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/SpacesMiniApp/Contracts/SpacesMessages.cs) |
| SpacesMiniApp | SpacesGetSpacesResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity> Value, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/SpacesMiniApp/Contracts/SpacesMessages.cs) |
| TreeMiniApp | DeleteTreeEntitiesRequest | Request | `Guid RequestId, IReadOnlyList<Guid> EntityIds` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| TreeMiniApp | DeleteTreeEntitiesResponse | Response | `Guid RequestId, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| TreeMiniApp | GetTreeForSpaceRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| TreeMiniApp | GetTreeForSpaceResponse | Response | `Guid RequestId, TreeSpaceSnapshot? Snapshot, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| TreeMiniApp | RevealTreeEntityRequest | Request | `Guid RequestId, Guid? EntityId` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeRevealMessages.cs) |
| TreeMiniApp | RevealTreeEntityResponse | Response | `Guid RequestId, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeRevealMessages.cs) |
| UserMessagesMiniApp | ClearUserMessages | Command | `Guid UserId, Guid LogicalWindowId` | [код](../../BusinessEntity/MiniApps/UserMessagesMiniApp/Contracts/ClearUserMessages.cs) |
| UserMessagesMiniApp | PostUserMessage | Command | `Guid UserId, Guid LogicalWindowId, Guid CorrelationId, string Text, UserMessageLevel Level = UserMessageLevel.Info, string? Title = null` | [код](../../BusinessEntity/MiniApps/UserMessagesMiniApp/Contracts/PostUserMessage.cs) |
| UserMiniApp | AddRichDocBookmarkRequest | Request | `Guid RequestId, Guid DocumentId, RichTextDocumentTextSelection? Selection` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | AddRichDocBookmarkResponse | Response | `Guid RequestId, RichTextDocumentBookmark? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateAdministrationUserRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateAdministrationUserResponse | Response | `Guid RequestId, UserAdministrationRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateRoleAssignmentRequest | Request | `Guid RequestId, Guid SpaceId, UserRoleAssignmentSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateRoleAssignmentResponse | Response | `Guid RequestId, UserRoleAssignmentRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateRoleRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateRoleResponse | Response | `Guid RequestId, UserRoleRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateUserGroupRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | CreateUserGroupResponse | Response | `Guid RequestId, UserGroupRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteAdministrationUserRequest | Request | `Guid RequestId, Guid UserId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteAdministrationUserResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteCurrentUserPropertyRequest | Request | `Guid RequestId, Guid PropertyId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteCurrentUserPropertyResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteCurrentUserRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteCurrentUserResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteDocPrintPresetRequest | Request | `Guid RequestId, string PresetName` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteDocPrintPresetResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRichDocBookmarkRequest | Request | `Guid RequestId, Guid BookmarkId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRichDocBookmarkResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRoleAssignmentRequest | Request | `Guid RequestId, Guid AssignmentId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRoleAssignmentResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRoleRequest | Request | `Guid RequestId, Guid RoleId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteRoleResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteUserGroupRequest | Request | `Guid RequestId, Guid GroupId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | DeleteUserGroupResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | EnsureCurrentUserRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | EnsureCurrentUserResponse | Response | `Guid RequestId, UserDto? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAdministrationUsersRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAdministrationUsersResponse | Response | `Guid RequestId, IReadOnlyList<UserAdministrationRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousAccessibleDocumentsRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousAccessibleDocumentsResponse | Response | `Guid RequestId, IReadOnlyList<UserAccessibleDocumentRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousAccessibleSpacesRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousAccessibleSpacesResponse | Response | `Guid RequestId, IReadOnlyList<UserSpaceRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousPermissionsForSpaceRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetAnonymousPermissionsForSpaceResponse | Response | `Guid RequestId, UserEffectivePermissions? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserContentAccessForEntityRequest | Request | `Guid RequestId, UserContentAccessRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserContentAccessForEntityResponse | Response | `Guid RequestId, UserContentAccessDecision? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPermissionsForEntityRequest | Request | `Guid RequestId, Guid EntityId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPermissionsForEntityResponse | Response | `Guid RequestId, UserEffectivePermissions? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPermissionsForSpaceRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPermissionsForSpaceResponse | Response | `Guid RequestId, UserEffectivePermissions? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPropertiesRequest | Request | `Guid RequestId, int PropertyType` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserPropertiesResponse | Response | `Guid RequestId, IReadOnlyList<UserPropertyDto>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetCurrentUserResponse | Response | `Guid RequestId, BusinessEntityUser? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetDocPrintPresetsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetDocPrintPresetsResponse | Response | `Guid RequestId, DocPrintSettingsPresetCollection? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetEntityContentAccessRequest | Request | `Guid RequestId, Guid EntityId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetEntityContentAccessResponse | Response | `Guid RequestId, UserContentAccessDecision? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetGroupsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetGroupsResponse | Response | `Guid RequestId, IReadOnlyList<string>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetProfileRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetProfileResponse | Response | `Guid RequestId, UserProfileDto? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRichDocBookmarksRequest | Request | `Guid RequestId, Guid DocumentId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRichDocBookmarksResponse | Response | `Guid RequestId, IReadOnlyList<RichTextDocumentBookmark>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRichDocDisplayedLevelRequest | Request | `Guid RequestId, Guid DocumentId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRichDocDisplayedLevelResponse | Response | `Guid RequestId, int Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRoleAssignmentSpacesRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRoleAssignmentSpacesResponse | Response | `Guid RequestId, IReadOnlyList<UserSpaceRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRoleAssignmentsRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRoleAssignmentsResponse | Response | `Guid RequestId, IReadOnlyList<UserRoleAssignmentRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRolesRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetRolesResponse | Response | `Guid RequestId, IReadOnlyList<UserRoleRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetUserGroupMembershipsRequest | Request | `Guid RequestId, Guid GroupId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetUserGroupMembershipsResponse | Response | `Guid RequestId, IReadOnlyList<UserGroupMembershipRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetUserGroupsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | GetUserGroupsResponse | Response | `Guid RequestId, IReadOnlyList<UserGroupRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | IsInGroupRequest | Request | `Guid RequestId, string GroupName` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | IsInGroupResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | ReadAdministrationUsersFromAuthentikRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | ReadAdministrationUsersFromAuthentikResponse | Response | `Guid RequestId, IReadOnlyList<UserAdministrationRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | SaveDocPrintPresetRequest | Request | `Guid RequestId, DocPrintSettingsPreset Preset` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | SaveDocPrintPresetResponse | Response | `Guid RequestId, DocPrintSettingsPreset? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | SaveRichDocDisplayedLevelRequest | Request | `Guid RequestId, Guid DocumentId, int DisplayLevelCount` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | SaveRichDocDisplayedLevelResponse | Response | `Guid RequestId, bool Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateAdministrationUserRequest | Request | `Guid RequestId, Guid UserId, UserAdministrationSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateAdministrationUserResponse | Response | `Guid RequestId, UserAdministrationRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateProfileRequest | Request | `Guid RequestId, UserProfileUpdateRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateProfileResponse | Response | `Guid RequestId, UserProfileDto? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateRoleRequest | Request | `Guid RequestId, Guid RoleId, UserRoleSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateRoleResponse | Response | `Guid RequestId, UserRoleRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateUserGroupMembershipsRequest | Request | `Guid RequestId, Guid GroupId, UserGroupMembershipSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateUserGroupMembershipsResponse | Response | `Guid RequestId, IReadOnlyList<UserGroupMembershipRecord>? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateUserGroupRequest | Request | `Guid RequestId, Guid GroupId, UserGroupSaveRequest Request` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpdateUserGroupResponse | Response | `Guid RequestId, UserGroupRecord? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpsertCurrentUserPropertyRequest | Request | `Guid RequestId, Guid? PropertyId, int PropertyType, string Data, string Metadata` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |
| UserMiniApp | UpsertCurrentUserPropertyResponse | Response | `Guid RequestId, UserPropertyDto? Result, BusError? Error = null` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/UserMiniAppMessages.cs) |


## Публикация и состояния

BusinessEntityChanged/BusinessEntityRelationChanged/EntityBatchCommitted
публикует только DataProvider после SQL commit и публикации immutable кэша.
События содержат идентичности, не изменяемые карточки UI.

ModuleLifecycleChanged несёт полный адрес runtime; прежняя команда с закрытым
адресом не исполняется. GiveMe/HereAre собирают metadata нескольких владельцев,
включая поздние объявления. По шине нет Type/RenderFragment/delegates/IBrowserFile.

ButtonStateSnapshot адресован окну/module/edit/context/binding. Endpoint проверяет
живой nonce, enabled/visible и актуальное право. Закрытый runtime отвечает явным
command.closed. Описание команды не содержит её исполняемый callback.

ObjectInContextSelected содержит immutable значения объектов, окно, источник,
SelectionRevision и SourceRevision. Helper сверяется с актуальным адресом master
и его lifecycle. Смена/закрытие очищает snapshot; поздний helper использует
коррелированный GiveMeCurrentSelection/HereIsCurrentSelection.

MiniAppSaveStateChanged несёт адрес рабочего экземпляра, StateRevision,
EditRevision, IsDirty, Status и OperationId. Ревизия состояния растёт в окне;
дубликаты не публикуются, закрытие последнего модуля освобождает metadata.
Черновики, payload и секреты в событие не входят. Save/Unknown/retry передаются
обычными SaveEntityBatch/GetOperationOutcome, без глобального Save пространства.

EntitySaveBatch.OwnedServiceChanges содержит только value JSON служебных DTO
владельца User. Его writer работает в общей SQL-транзакции DataProvider/outcome.
Пароли и callbacks не попадают в пакет. Authentik API остаётся внешним эффектом.

[Правила шины](../Policy/miniapp-reactivebus-architecture-guide.md),
[модули/маршруты](modules-and-routes.md), [рабочие наборы](save-contexts.md).

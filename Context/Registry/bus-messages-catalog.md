# Каталог сообщений шины

Снимок фактического кода этапа 0 TASK_ARCH_001, 2026-10-28.
Транспорт один: singleton ReactiveUI.IMessageBus. Адресация и BusError ещё
реализуются на этапе 1; текущие поля ниже не следует принимать за целевую норму.
Нормы: [шина](../Policy/miniapp-reactivebus-architecture-guide.md),
[ТЗ v2.2](../Tasks/TASK_ARCH_001_miniapp-bus-architecture-migration.md).

Контракты добавляются и удаляются в каталоге вместе с кодом.
DTO, которые сами не являются сообщениями, в список не включены.

| Владелец | Сообщение | Вид | Текущие поля | Источник |
|---|---|---|---|---|
| DataProviderMiniApp | GetBusinessEntitiesRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntitiesResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity> Records, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityByIdRequest | Request | `Guid RequestId, Guid Id` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityByIdResponse | Response | `Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity? Record, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataResponse | Response | `Guid RequestId, string? Data, int Version = 1, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataVersionRequest | Request | `Guid RequestId, Guid BusinessEntityId, int Version` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataVersionResponse | Response | `Guid RequestId, string? Data, int Version = 1, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataVersionsRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetBusinessEntityDataVersionsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityDataVersionInfo> Records, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityDataVersionDescriptionRequest | Request | `Guid RequestId, Guid BusinessEntityId, int Version, string? VersionDescription` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityDataVersionDescriptionResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | AddBusinessEntityRequest | Request | `Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity Record` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | AddBusinessEntityResponse | Response | `Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity? Record, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityRequest | Request | `Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity Record` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteBusinessEntityRequest | Request | `Guid RequestId, Guid Id` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteBusinessEntityResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | ClearDataProviderStorageRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | ClearDataProviderStorageResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityDataRequest | Request | `Guid RequestId, Guid BusinessEntityId, string Data, bool HasVersions = false, Guid? LastModifiedByUserId = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateBusinessEntityDataResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetAllRelationsRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetAllRelationsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationsRequest | Request | `Guid RequestId, Guid ObjectAId, Guid ObjectBId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationsResponse | Response | `Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationByIdRequest | Request | `Guid RequestId, Guid Id` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRelationByIdResponse | Response | `Guid RequestId, BusinessEntityRelation? Record, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | CreateRelationRequest | Request | `Guid RequestId, BusinessEntityRelation Record` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | CreateRelationResponse | Response | `Guid RequestId, BusinessEntityRelation? Record, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateRelationRequest | Request | `Guid RequestId, BusinessEntityRelation Record` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | UpdateRelationResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteRelationRequest | Request | `Guid RequestId, Guid Id` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteRelationResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextChunksRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextChunksResponse | Response | `Guid RequestId, IReadOnlyList<RichTextDocumentChunk> Records, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextTableOfContentsEntriesRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextTableOfContentsEntriesResponse | Response | `Guid RequestId, IReadOnlyList<RichTextDocumentTableOfContentsEntry> Records, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | RebuildRichTextTableOfContentsEntriesRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | RebuildRichTextTableOfContentsEntriesResponse | Response | `Guid RequestId, IReadOnlyList<RichTextDocumentTableOfContentsEntry> Records, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | ReplaceRichTextChunksRequest | Request | `Guid RequestId, Guid BusinessEntityId, IReadOnlyList<RichTextDocumentChunk> Records` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | ReplaceRichTextChunksResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | SaveRichTextEmbeddedFilesRequest | Request | `Guid RequestId, Guid BusinessEntityId, IReadOnlyList<RichTextEmbeddedFile> Files, bool ReplaceExistingFiles` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | SaveRichTextEmbeddedFilesResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextEmbeddedFileRequest | Request | `Guid RequestId, Guid BusinessEntityId, string ImageId, string Variant` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | GetRichTextEmbeddedFileResponse | Response | `Guid RequestId, RichTextEmbeddedFileContent? Record, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteRichTextStorageRequest | Request | `Guid RequestId, Guid BusinessEntityId` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| DataProviderMiniApp | DeleteRichTextStorageResponse | Response | `Guid RequestId, bool Success, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Messages/DataProviderMessages.cs) |
| MediaServerMiniApp | MediaVideoUploadFailedMessage | Event | `string? ClientUploadToken, string ErrorMessage, Guid? SpaceId` | [код](../../BusinessEntity/MiniApps/MediaServerMiniApp/Contracts/MediaVideoUploadFailedMessage.cs) |
| MediaServerMiniApp | MediaVideoUploadedMessage | Event | `string? ClientUploadToken, MediaVideoInfo Video, Guid? SpaceId` | [код](../../BusinessEntity/MiniApps/MediaServerMiniApp/Contracts/MediaVideoUploadedMessage.cs) |
| TreeMiniApp | GetTreeForSpaceRequest | Request | `Guid RequestId, Guid SpaceId` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| TreeMiniApp | GetTreeForSpaceResponse | Response | `Guid RequestId, TreeSpaceSnapshot? Snapshot, string? ErrorMessage = null` | [код](../../BusinessEntity/MiniApps/TreeMiniApp/Contracts/Messages/TreeMiniAppMessages.cs) |
| UserMessagesMiniApp | ClearUserMessages | Command | `Guid UserId` | [код](../../BusinessEntity/MiniApps/UserMessagesMiniApp/Contracts/ClearUserMessages.cs) |
| UserMessagesMiniApp | PostUserMessage | Command | `Guid UserId, string Text, UserMessageLevel Level = UserMessageLevel.Info, string? Title = null` | [код](../../BusinessEntity/MiniApps/UserMessagesMiniApp/Contracts/PostUserMessage.cs) |
| UserMiniApp | GetUserRequest | Request | `Guid RequestId` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/GetUserRequest.cs) |
| UserMiniApp | GetUserResponse | Response | `Guid RequestId, BusinessEntityUser? User` | [код](../../BusinessEntity/MiniApps/UserMiniApp/Contracts/Messages/GetUserResponse.cs) |
| Services (удаляется на этапе 2) | EntityUpdatedMessage | Event | `BusinessEntity.Core.Classes.BusinessEntity EntityData` | [код](../../BusinessEntity/Services/EntityMessages.cs) |
| Services (удаляется на этапе 2) | EntityCreatedMessage | Event | `BusinessEntity.Core.Classes.BusinessEntity EntityData` | [код](../../BusinessEntity/Services/EntityMessages.cs) |
| Services (удаляется на этапе 2) | EntityDeletedMessage | Event | `Guid EntityId` | [код](../../BusinessEntity/Services/EntityMessages.cs) |

## Запланированные изменения

- Этап 1: RequestId, адресный контекст, типизированные отказы и недостающие запросы User/Activity.
- Этап 2: транзакции, исход OperationId, события изменения только от DataProvider; старые EntityMessages удаляются.
- Этапы 4–8: декларации, жизненный цикл, контексты сохранения, команды и выделение.

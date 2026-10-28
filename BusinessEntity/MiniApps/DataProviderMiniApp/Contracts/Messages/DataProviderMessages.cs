using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.RichText;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;

// Запрос GetBusinessEntitiesRequest.
public sealed record GetBusinessEntitiesRequest(Guid RequestId) : BusRequest(RequestId);
// Ответ GetBusinessEntitiesResponse.
public sealed record GetBusinessEntitiesResponse(Guid RequestId, IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity> Records, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetBusinessEntityByIdRequest.
public sealed record GetBusinessEntityByIdRequest(Guid RequestId, Guid Id) : BusRequest(RequestId);
// Ответ GetBusinessEntityByIdResponse.
public sealed record GetBusinessEntityByIdResponse(Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity? Record, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetBusinessEntityDataRequest.
public sealed record GetBusinessEntityDataRequest(Guid RequestId, Guid BusinessEntityId) : BusRequest(RequestId);
// Ответ GetBusinessEntityDataResponse.
public sealed record GetBusinessEntityDataResponse(Guid RequestId, IBusinessEntityData? Record, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetBusinessEntityDataVersionRequest.
public sealed record GetBusinessEntityDataVersionRequest(Guid RequestId, Guid BusinessEntityId, int Version) : BusRequest(RequestId);
// Ответ GetBusinessEntityDataVersionResponse.
public sealed record GetBusinessEntityDataVersionResponse(Guid RequestId, IBusinessEntityData? Record, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetBusinessEntityDataVersionsRequest.
public sealed record GetBusinessEntityDataVersionsRequest(Guid RequestId, Guid BusinessEntityId) : BusRequest(RequestId);
// Ответ GetBusinessEntityDataVersionsResponse.
public sealed record GetBusinessEntityDataVersionsResponse(Guid RequestId, IReadOnlyList<BusinessEntityDataVersionInfo> Records, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос UpdateBusinessEntityDataVersionDescriptionRequest.
public sealed record UpdateBusinessEntityDataVersionDescriptionRequest(Guid RequestId, Guid BusinessEntityId, int Version, string? VersionDescription) : BusRequest(RequestId);
// Ответ UpdateBusinessEntityDataVersionDescriptionResponse.
public sealed record UpdateBusinessEntityDataVersionDescriptionResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос AddBusinessEntityRequest.
public sealed record AddBusinessEntityRequest(Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity Record) : BusRequest(RequestId);
// Ответ AddBusinessEntityResponse.
public sealed record AddBusinessEntityResponse(Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity? Record, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос UpdateBusinessEntityRequest.
public sealed record UpdateBusinessEntityRequest(Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity Record) : BusRequest(RequestId);
// Ответ UpdateBusinessEntityResponse.
public sealed record UpdateBusinessEntityResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос DeleteBusinessEntityRequest.
public sealed record DeleteBusinessEntityRequest(Guid RequestId, Guid Id) : BusRequest(RequestId);
// Ответ DeleteBusinessEntityResponse.
public sealed record DeleteBusinessEntityResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос ClearDataProviderStorageRequest.
public sealed record ClearDataProviderStorageRequest(Guid RequestId) : BusRequest(RequestId);
// Ответ ClearDataProviderStorageResponse.
public sealed record ClearDataProviderStorageResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос UpdateBusinessEntityDataRequest.
public sealed record UpdateBusinessEntityDataRequest(Guid RequestId, Guid BusinessEntityId, IBusinessEntityData Data, bool HasVersions = false, Guid? LastModifiedByUserId = null) : BusRequest(RequestId);
// Ответ UpdateBusinessEntityDataResponse.
public sealed record UpdateBusinessEntityDataResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetAllRelationsRequest.
public sealed record GetAllRelationsRequest(Guid RequestId) : BusRequest(RequestId);
// Ответ GetAllRelationsResponse.
public sealed record GetAllRelationsResponse(Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetRelationsRequest.
public sealed record GetRelationsRequest(Guid RequestId, Guid ObjectAId, Guid ObjectBId) : BusRequest(RequestId);
// Ответ GetRelationsResponse.
public sealed record GetRelationsResponse(Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetRelationByIdRequest.
public sealed record GetRelationByIdRequest(Guid RequestId, Guid Id) : BusRequest(RequestId);
// Ответ GetRelationByIdResponse.
public sealed record GetRelationByIdResponse(Guid RequestId, BusinessEntityRelation? Record, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос CreateRelationRequest.
public sealed record CreateRelationRequest(Guid RequestId, BusinessEntityRelation Record) : BusRequest(RequestId);
// Ответ CreateRelationResponse.
public sealed record CreateRelationResponse(Guid RequestId, BusinessEntityRelation? Record, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос UpdateRelationRequest.
public sealed record UpdateRelationRequest(Guid RequestId, BusinessEntityRelation Record) : BusRequest(RequestId);
// Ответ UpdateRelationResponse.
public sealed record UpdateRelationResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос DeleteRelationRequest.
public sealed record DeleteRelationRequest(Guid RequestId, Guid Id) : BusRequest(RequestId);
// Ответ DeleteRelationResponse.
public sealed record DeleteRelationResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetRichTextChunksRequest.
public sealed record GetRichTextChunksRequest(Guid RequestId, Guid BusinessEntityId) : BusRequest(RequestId);
// Ответ GetRichTextChunksResponse.
public sealed record GetRichTextChunksResponse(Guid RequestId, IReadOnlyList<RichTextDocumentChunk> Records, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetRichTextTableOfContentsEntriesRequest.
public sealed record GetRichTextTableOfContentsEntriesRequest(Guid RequestId, Guid BusinessEntityId) : BusRequest(RequestId);
// Ответ GetRichTextTableOfContentsEntriesResponse.
public sealed record GetRichTextTableOfContentsEntriesResponse(Guid RequestId, IReadOnlyList<RichTextDocumentTableOfContentsEntry> Records, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос RebuildRichTextTableOfContentsEntriesRequest.
public sealed record RebuildRichTextTableOfContentsEntriesRequest(Guid RequestId, Guid BusinessEntityId) : BusRequest(RequestId);
// Ответ RebuildRichTextTableOfContentsEntriesResponse.
public sealed record RebuildRichTextTableOfContentsEntriesResponse(Guid RequestId, IReadOnlyList<RichTextDocumentTableOfContentsEntry> Records, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос ReplaceRichTextChunksRequest.
public sealed record ReplaceRichTextChunksRequest(Guid RequestId, Guid BusinessEntityId, IReadOnlyList<RichTextDocumentChunk> Records) : BusRequest(RequestId);
// Ответ ReplaceRichTextChunksResponse.
public sealed record ReplaceRichTextChunksResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос SaveRichTextEmbeddedFilesRequest.
public sealed record SaveRichTextEmbeddedFilesRequest(
    Guid RequestId,
    Guid BusinessEntityId,
    IReadOnlyList<RichTextEmbeddedFile> Files,
    bool ReplaceExistingFiles) : BusRequest(RequestId);
// Ответ SaveRichTextEmbeddedFilesResponse.
public sealed record SaveRichTextEmbeddedFilesResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос GetRichTextEmbeddedFileRequest.
public sealed record GetRichTextEmbeddedFileRequest(Guid RequestId, Guid BusinessEntityId, string ImageId, string Variant) : BusRequest(RequestId);
// Ответ GetRichTextEmbeddedFileResponse.
public sealed record GetRichTextEmbeddedFileResponse(Guid RequestId, RichTextEmbeddedFileContent? Record, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос DeleteRichTextStorageRequest.
public sealed record DeleteRichTextStorageRequest(Guid RequestId, Guid BusinessEntityId) : BusRequest(RequestId);
// Ответ DeleteRichTextStorageResponse.
public sealed record DeleteRichTextStorageResponse(Guid RequestId, bool Success, BusError? Error = null) : BusResponse(RequestId, Error);

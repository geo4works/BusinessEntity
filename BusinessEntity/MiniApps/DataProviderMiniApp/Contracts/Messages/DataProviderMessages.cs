using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;

public sealed record GetBusinessEntitiesRequest(Guid RequestId) : BusRequest(RequestId);

public sealed record GetBusinessEntitiesResponse(Guid RequestId, IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity> Records, BusError? Error = null) : BusResponse(RequestId, Error);

public sealed record GetBusinessEntityByIdRequest(Guid RequestId, Guid Id) : BusRequest(RequestId);

public sealed record GetBusinessEntityByIdResponse(Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity? Record, BusError? Error = null) : BusResponse(RequestId, Error);

public sealed record GetBusinessEntityDataRequest(Guid RequestId, Guid BusinessEntityId) : BusRequest(RequestId);

public sealed record GetBusinessEntityDataResponse(Guid RequestId, IBusinessEntityData? Record, BusError? Error = null) : BusResponse(RequestId, Error);

public sealed record GetBusinessEntityDataVersionRequest(Guid RequestId, Guid BusinessEntityId, int Version) : BusRequest(RequestId);

public sealed record GetBusinessEntityDataVersionResponse(Guid RequestId, IBusinessEntityData? Record, BusError? Error = null) : BusResponse(RequestId, Error);

public sealed record GetBusinessEntityDataVersionsRequest(Guid RequestId, Guid BusinessEntityId) : BusRequest(RequestId);

public sealed record GetBusinessEntityDataVersionsResponse(Guid RequestId, IReadOnlyList<BusinessEntityDataVersionInfo> Records, BusError? Error = null) : BusResponse(RequestId, Error);

public sealed record GetAllRelationsRequest(Guid RequestId) : BusRequest(RequestId);

public sealed record GetAllRelationsResponse(Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, BusError? Error = null) : BusResponse(RequestId, Error);

public sealed record GetRelationsRequest(Guid RequestId, Guid ObjectAId, Guid ObjectBId) : BusRequest(RequestId);

public sealed record GetRelationsResponse(Guid RequestId, IReadOnlyList<BusinessEntityRelation> Records, BusError? Error = null) : BusResponse(RequestId, Error);

public sealed record GetRelationByIdRequest(Guid RequestId, Guid Id) : BusRequest(RequestId);

public sealed record GetRelationByIdResponse(Guid RequestId, BusinessEntityRelation? Record, BusError? Error = null) : BusResponse(RequestId, Error);

public sealed record GetRichTextEmbeddedFileRequest(Guid RequestId, Guid BusinessEntityId, string ImageId, string Variant) : BusRequest(RequestId);

public sealed record GetRichTextEmbeddedFileResponse(Guid RequestId, RichTextEmbeddedFileContent? Record, BusError? Error = null) : BusResponse(RequestId, Error);

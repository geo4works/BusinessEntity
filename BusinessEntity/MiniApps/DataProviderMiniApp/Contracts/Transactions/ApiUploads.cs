using BusinessEntity.Core.Classes;
using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

public sealed record ApiUploadCreate(string Purpose,Guid SpaceId,Guid TargetId,long Length,string FileName,string ContentType,string? Sha256=null);
public sealed record ApiUploadPart(int Number,long Length,string Sha256);
public sealed record ApiUploadStatus(Guid Id,string Purpose,Guid SpaceId,Guid TargetId,long Length,string FileName,string ContentType,int PartSize,string State,string? Sha256,IReadOnlyList<ApiUploadPart> Parts,DateTime ExpiresAt);
// Типоспецифичное право/формат задаёт владелец назначения, storage не перечисляет Rich/Media типы.
public interface IApiUploadPurpose
{
    string Id{get;}string OwnerMiniAppId{get;}string RequiredCapability{get;}long MaxBytes{get;}bool CreatesEntity{get;}
    ApiPermissionCheck AuthorizeTarget(BusinessEntityTypeEnum type,Guid targetId,Guid spaceId);
    void ValidateFormat(string fileName,string contentType);
}
public sealed record CreateApiUploadRequest(Guid RequestId,ApiUploadCreate Input):BusRequest(RequestId);
public sealed record ApiUploadResponse(Guid RequestId,ApiUploadStatus? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ReadApiUploadRequest(Guid RequestId,Guid UploadId):BusRequest(RequestId);
public sealed record CompleteApiUploadRequest(Guid RequestId,Guid UploadId):BusRequest(RequestId);
public sealed record CancelApiUploadRequest(Guid RequestId,Guid UploadId):BusRequest(RequestId);
public sealed record OpenApiUploadPartRequest(Guid RequestId,Guid UploadId,int PartNumber,long Length,string Sha256):BusRequest(RequestId);
public sealed record OpenApiUploadPartResponse(Guid RequestId,Guid? LeaseId,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record CompleteApiUploadPartRequest(Guid RequestId,Guid UploadId,int PartNumber,long Length,string Sha256):BusRequest(RequestId)
{[System.Text.Json.Serialization.JsonIgnore] public Guid LeaseId{get;init;}}
public sealed record ReadApiUploadLeaseRequest(Guid RequestId,Guid UploadId,string Purpose):BusRequest(RequestId);
public sealed record ReadApiUploadLeaseResponse(Guid RequestId,Guid? LeaseId,ApiUploadStatus? Upload=null,BusError? Error=null):BusResponse(RequestId,Error);

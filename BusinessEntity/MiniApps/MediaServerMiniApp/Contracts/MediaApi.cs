using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;

public static class MediaSpaceRelation{public const string Parameters="media-video-space";}
public sealed record ApiMediaCreate(Guid ReadyUploadId,string? DisplayName=null,string Comment="");
public sealed record ApiMediaUpdate(string DisplayName,string Comment);
public sealed record ApiMediaVideo(Guid Id,Guid SpaceId,string DisplayName,string Comment,string FileName,string ContentType,long Length,double? DurationSeconds,Guid? UploadedByUserId,DateTime UploadedDate,string DownloadUrl);
public sealed record ListApiMediaRequest(Guid RequestId,Guid SpaceId,string? Search,int Limit=50,string? Cursor=null):BusRequest(RequestId);
public sealed record ListApiMediaResponse(Guid RequestId,ApiPage<ApiMediaVideo>? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ReadApiMediaRequest(Guid RequestId,Guid VideoId):BusRequest(RequestId);
public sealed record ReadApiMediaResponse(Guid RequestId,ApiMediaVideo? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record CreateApiMediaRequest(Guid RequestId,Guid SpaceId,ApiMediaCreate Input):BusRequest(RequestId);
public sealed record UpdateApiMediaRequest(Guid RequestId,Guid VideoId,ApiMediaUpdate Input):BusRequest(RequestId);
public sealed record DeleteApiMediaRequest(Guid RequestId,Guid VideoId):BusRequest(RequestId);
public sealed record ApiMediaWriteResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record DownloadApiMediaRequest(Guid RequestId,Guid VideoId):BusRequest(RequestId);
public sealed record DownloadApiMediaResponse(Guid RequestId,ApiFileDownload? Result,BusError? Error=null):BusResponse(RequestId,Error);

public sealed record RunApiMediaJobRequest(Guid RequestId,Guid JobId):BusRequest(RequestId);

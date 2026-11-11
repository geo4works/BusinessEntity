using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;

public sealed record ApiRichImageAttach(Guid ReadyUploadId,int BaseVersion,string? Title=null,Guid? AfterChunkId=null,string AltText="",int Width=0,int Height=0);
public sealed record AttachApiRichImageRequest(Guid RequestId,Guid DocumentId,ApiRichImageAttach Input):BusRequest(RequestId);
public sealed record DownloadApiRichImageRequest(Guid RequestId,Guid DocumentId,string FileId,string Variant,int? Version):BusRequest(RequestId);
public sealed record DownloadApiRichImageResponse(Guid RequestId,ApiFileDownload? Result,BusError? Error=null):BusResponse(RequestId,Error);

using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;

// Внешняя schema v1 не экспортирует EF/viewport manifest. Image/video — явные проверяемые ссылки.
public sealed record ApiRichBlock(string Kind="paragraph",string Html="",int Level=0,string? AttachmentId=null,string Variant="original",string? VideoId=null,string AltText="",int Width=0,int Height=0);
public sealed record ApiRichChunk(Guid Id,long Position,IReadOnlyList<ApiRichBlock> Blocks);
public sealed record ApiRichChunkWrite(string Id,ApiRichBlock[] Blocks);
public sealed record ApiRichCreate(string Title,ApiRichBlock[]? Blocks=null,Guid? ParentId=null,bool? IsPublic=null,int SchemaVersion=1);
public sealed record ApiRichRevision(int BaseVersion,string Title,string[] ChunkOrder,ApiRichChunkWrite[] Changes,int SchemaVersion=1,string? VersionDescription=null);
public sealed record ApiRichMetadata(bool IsPublic);
public sealed record ApiRichVersionSelection(int Version);
public sealed record ApiRichVersionDescription(string Description);
public sealed record ApiRichDocument(Guid Id,Guid SpaceId,string Title,int Version,int PublishedVersion,bool IsPublic,int ChunkCount,int SchemaVersion=1);
public sealed record ApiRichContent(Guid Id,int Version,IReadOnlyList<ApiRichChunk> Chunks,string? NextCursor,int SchemaVersion=1);
public sealed record ApiRichVersion(int Version,string Title,Guid? SavedBy,DateTime CreatedAt,string Description);
public sealed record ApiRichPublication(int Version);
public sealed record ReadApiRichRequest(Guid RequestId,Guid EntityId,int? Version=null,bool RequireHistory=false):BusRequest(RequestId);
public sealed record ReadApiRichResponse(Guid RequestId,ApiRichDocument? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ReadApiRichContentRequest(Guid RequestId,Guid EntityId,int? Version,int Limit,string? Cursor):BusRequest(RequestId);
public sealed record ReadApiRichContentResponse(Guid RequestId,ApiRichContent? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ListApiRichVersionsRequest(Guid RequestId,Guid EntityId,int Limit,string? Cursor):BusRequest(RequestId);
public sealed record ListApiRichVersionsResponse(Guid RequestId,ApiPage<ApiRichVersion>? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record CreateApiRichRequest(Guid RequestId,Guid SpaceId,ApiRichCreate Input):BusRequest(RequestId);
public sealed record SaveApiRichRevisionRequest(Guid RequestId,Guid EntityId,ApiRichRevision Input):BusRequest(RequestId);
public sealed record UpdateApiRichMetadataRequest(Guid RequestId,Guid EntityId,ApiRichMetadata Input):BusRequest(RequestId);
public sealed record RestoreApiRichRequest(Guid RequestId,Guid EntityId,ApiRichVersionSelection Input):BusRequest(RequestId);
public sealed record DescribeApiRichVersionRequest(Guid RequestId,Guid EntityId,int Version,ApiRichVersionDescription Input):BusRequest(RequestId);
public sealed record PublishApiRichRequest(Guid RequestId,Guid EntityId,ApiRichVersionSelection Input):BusRequest(RequestId);
public sealed record ReadApiRichPublicationRequest(Guid RequestId,Guid EntityId):BusRequest(RequestId);
public sealed record ReadApiRichPublicationResponse(Guid RequestId,ApiRichPublication? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record DeleteApiRichRequest(Guid RequestId,Guid EntityId,Guid? PlanId=null):BusRequest(RequestId);
public sealed record ApiRichWriteResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);

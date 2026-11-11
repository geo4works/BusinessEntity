using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Contracts;

// Заметка использует этот же Document; отдельного доменного типа/дублирующих endpoints нет.
public sealed record ApiDocument(Guid Id,Guid SpaceId,string Title,string Content,int Version,bool IsPublic);
public sealed record ApiDocumentCreate(string Title,string Content,Guid? ParentId=null,bool? IsPublic=null);
public sealed record ApiDocumentUpdate(int BaseVersion,string Title,string Content,bool? IsPublic=null,string? VersionDescription=null);
public sealed record ApiDocumentRestore(int Version);
public sealed record ApiDocumentVersion(int Version,string Title,Guid? SavedBy,DateTime CreatedAt,string Description);
public sealed record ReadApiDocumentRequest(Guid RequestId,Guid DocumentId,int? Version=null):BusRequest(RequestId);
public sealed record ReadApiDocumentResponse(Guid RequestId,ApiDocument? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ListApiDocumentVersionsRequest(Guid RequestId,Guid DocumentId,int Limit,string? Cursor):BusRequest(RequestId);
public sealed record ListApiDocumentVersionsResponse(Guid RequestId,ApiPage<ApiDocumentVersion>? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record CreateApiDocumentRequest(Guid RequestId,Guid SpaceId,ApiDocumentCreate Input):BusRequest(RequestId);
public sealed record UpdateApiDocumentRequest(Guid RequestId,Guid DocumentId,ApiDocumentUpdate Input):BusRequest(RequestId);
public sealed record RestoreApiDocumentRequest(Guid RequestId,Guid DocumentId,ApiDocumentRestore Input):BusRequest(RequestId);
public sealed record DeleteApiDocumentRequest(Guid RequestId,Guid DocumentId,Guid? PlanId=null):BusRequest(RequestId);
public sealed record ApiDocumentWriteResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);

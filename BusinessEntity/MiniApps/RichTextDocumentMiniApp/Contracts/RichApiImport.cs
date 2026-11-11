using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;

// Create: TargetId родителя; Replace/Append: TargetId статьи и обязательная база автора.
public sealed record ApiRichImport(Guid ReadyUploadId,string Mode,string Title,int? BaseVersion=null,bool? IsPublic=null);
public sealed record SubmitApiRichImportRequest(Guid RequestId,Guid TargetId,ApiRichImport Input):BusRequest(RequestId);
public sealed record RunApiRichImportJobRequest(Guid RequestId,Guid JobId):BusRequest(RequestId);
public sealed record RunApiRichImportJobResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);

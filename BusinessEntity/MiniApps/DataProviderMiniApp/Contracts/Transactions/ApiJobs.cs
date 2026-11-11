using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

public sealed record ApiJobAccepted(Guid JobId,Guid OperationId,string StatusUrl);
public sealed record ApiJobStatus(Guid Id,string Owner,string Kind,string State,Guid OperationId,Guid SpaceId,long ProcessedBytes,int Chunks,string? ErrorCode,ApiMutationOutcome? Result,DateTime CreatedAt,DateTime UpdatedAt);
// InputJson создаёт зарегистрированный owner из своего известного DTO; HTTP generic relay отсутствует.
public sealed record StoredApiJob(ApiJobStatus Public,string InputJson,ApiOperationIdentity Operation,ApiJobAuthorization Authorization,ApiJobAuthorization SubmittedAuthorization,bool CancelRequested=false,Guid? RuntimeId=null,IReadOnlyList<Guid>? UploadIds=null);
public sealed record CreateApiJobRequest(Guid RequestId,Guid JobId,string Owner,string Kind,string InputJson,Guid SpaceId,ApiJobAuthorization Authorization,IReadOnlyList<ApiPermissionCheck>? Checks=null,IReadOnlyList<Guid>? UploadIds=null):BusRequest(RequestId);
public sealed record CreateApiJobResponse(Guid RequestId,ApiJobAccepted? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ReadApiJobRequest(Guid RequestId,Guid JobId,string? RequiredCapability=null):BusRequest(RequestId);
public sealed record ReadApiJobResponse(Guid RequestId,StoredApiJob? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ClaimApiJobRequest(Guid RequestId,Guid RuntimeId,IReadOnlyList<string> RegisteredKinds):BusRequest(RequestId);
public sealed record ClaimApiJobResponse(Guid RequestId,StoredApiJob? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record UpdateApiJobRequest(Guid RequestId,Guid JobId,string State,long ProcessedBytes=0,int Chunks=0,string? ErrorCode=null):BusRequest(RequestId);
public sealed record UpdateApiJobResponse(Guid RequestId,ApiJobStatus? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record CancelApiJobRequest(Guid RequestId,Guid JobId):BusRequest(RequestId);
public sealed record ReauthorizeApiJobRequest(Guid RequestId,Guid JobId,ApiJobAuthorization Authorization):BusRequest(RequestId);

public sealed record ReauthorizeApiJobControlRequest(Guid RequestId,Guid JobId):BusRequest(RequestId);

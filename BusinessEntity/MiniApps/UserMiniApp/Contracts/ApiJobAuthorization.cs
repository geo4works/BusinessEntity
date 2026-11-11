using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.UserMiniApp.Contracts;

// Снимок уже проверенной authority, не bearer/refresh token или пароль.
public sealed record ApiJobAuthorization(ApiSessionInfo Session,string Issuer,string Subject,DateTime AccessExpiresAt);
public sealed record CaptureApiJobAuthorizationRequest(Guid RequestId,string CapabilityId):BusRequest(RequestId);
public sealed record CaptureApiJobAuthorizationResponse(Guid RequestId,ApiJobAuthorization? Result,BusError? Error=null):BusResponse(RequestId,Error);
// Worker не задаёт UserId/claims: User сам читает доверенную запись DataProvider по JobId.
public sealed record RestoreApiJobAuthorizationRequest(Guid RequestId,Guid JobId):BusRequest(RequestId);
public sealed record RestoreApiJobAuthorizationResponse(Guid RequestId,ApiSessionInfo? Result,BusError? Error=null):BusResponse(RequestId,Error);

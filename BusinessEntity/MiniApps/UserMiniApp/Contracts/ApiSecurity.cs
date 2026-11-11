using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.MiniApps.UserMiniApp.Contracts;

// Ограничение по пространствам явно отличается от браузерного выбранного пространства.
public sealed record ApiSpaceScope(bool AllSpaces=false,Guid[]? SpaceIds=null)
{
    // Empty не предоставляет доступ ни к одному пространству.
    public bool Allows(Guid id)=>AllSpaces||(SpaceIds??[]).Contains(id);
}
// Сессия содержит только безопасные metadata и фиксированные ограничения.
public sealed record ApiSessionInfo(Guid Id,Guid UserId,string ClientId,Guid ProviderId,long Epoch,
    DateTime ExpiresAt,string State,string[] Capabilities,ApiSpaceScope Spaces);
// Запрошенные ограничения сужают, но не расширяют локальный grant.
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ApiSessionOptions(string[]? Capabilities=null,ApiSpaceScope? Spaces=null);
// Карточка внешнего API не раскрывает raw claims и management IDs.
public sealed record ApiSelf(Guid UserId,string Login,string DisplayName,string Kind,bool Enabled);
public sealed record OpenApiSessionRequest(Guid RequestId,ApiSessionOptions Options):BusRequest(RequestId);
public sealed record OpenApiSessionResponse(Guid RequestId,ApiSessionInfo? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ValidateApiSessionRequest(Guid RequestId,Guid SessionId,string? Capability=null):BusRequest(RequestId);
public sealed record ValidateApiSessionResponse(Guid RequestId,ApiSessionInfo? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record CloseApiSessionRequest(Guid RequestId,Guid SessionId):BusRequest(RequestId);
public sealed record CloseApiSessionResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ReadApiSelfRequest(Guid RequestId):BusRequest(RequestId);
public sealed record ReadApiSelfResponse(Guid RequestId,ApiSelf? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record AuthorizeApiCommitRequest(Guid RequestId,string CapabilityId,Guid? SpaceId,IReadOnlyList<ApiPermissionCheck> Checks):BusRequest(RequestId);
public sealed record AuthorizeApiCommitResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);

// Connector исполняет только public typed requests текущего server request scope.
public interface IUserApiConnector
{
    Task<ApiSessionInfo> OpenAsync(ApiSessionOptions options,CancellationToken ct);
    Task<ApiSessionInfo> ValidateAsync(Guid id,string? capability,CancellationToken ct);
    Task CloseAsync(Guid id,CancellationToken ct);
}

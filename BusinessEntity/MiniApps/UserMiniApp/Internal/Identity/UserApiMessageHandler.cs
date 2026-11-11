using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Owner endpoint адресует API security тем же scoped транспортом, без самозапросов User.
internal sealed class UserApiMessageHandler(UserApiSessions sessions,UserApiCommitAuthorizer authorizer,IMessageBus bus,BusRequestScope scope,ILogger<UserApiMessageHandler> logger)
    : ScopedBusEndpoint(bus,scope,logger)
{
    // Набор типов фиксирован при регистрации и не выбирается строкой CLR-типа клиента.
    protected override void SubscribeRequests()
    {
        Subscribe<AuthorizeApiCommitRequest,AuthorizeApiCommitResponse>(async r=>{await authorizer.AuthorizeAsync(r,default);return new(r.RequestId);},(id,error)=>new(id,error));
        Subscribe<OpenApiSessionRequest,OpenApiSessionResponse>(async r=>new(r.RequestId,await sessions.OpenAsync(r.Options,default)),(id,error)=>new(id,null,error));
        Subscribe<ValidateApiSessionRequest,ValidateApiSessionResponse>(async r=>new(r.RequestId,await sessions.ValidateAsync(r.SessionId,r.Capability,default)),(id,error)=>new(id,null,error));
        Subscribe<CloseApiSessionRequest,CloseApiSessionResponse>(async r=>{await sessions.CloseAsync(r.SessionId,default);return new(r.RequestId);},(id,error)=>new(id,error));
        Subscribe<ReadApiSelfRequest,ReadApiSelfResponse>(async r=>new(r.RequestId,await sessions.ReadSelfAsync(default)),(id,error)=>new(id,null,error));
    }
}

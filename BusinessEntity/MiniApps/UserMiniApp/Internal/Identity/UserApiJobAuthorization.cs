using System.Globalization;
using System.Security.Claims;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

internal sealed class UserApiJobAuthorization(UserApiSessions sessions,ICurrentPrincipalSource principals,IBackgroundPrincipalSink background,BusRequestScope scope,BusRequestClient bus)
{
    public async Task<ApiJobAuthorization> CaptureAsync(string capability,CancellationToken ct)
    {
        if(scope.IsSystem||scope.ApiSessionId is not {} id)throw new UnauthorizedAccessException();var session=await sessions.ValidateAsync(id,capability,ct);var principal=await principals.GetAsync(ct);
        if(principal.Identity?.AuthenticationType!=ActiveBearerHandler.SchemeName||scope.ApiAccessExpiresAt is not {} expiry||expiry<=DateTime.UtcNow)throw new UnauthorizedAccessException("Нужен действующий API access.");
        return new(session,principal.FindFirstValue(IdentityClaimNames.Issuer)??throw new UnauthorizedAccessException(),principal.FindFirstValue(IdentityClaimNames.Subject)??throw new UnauthorizedAccessException(),expiry);
    }
    public async Task<ApiSessionInfo> RestoreAsync(Guid jobId,CancellationToken ct)
    {
        if(!scope.IsSystem)throw new UnauthorizedAccessException();var stored=(await bus.SendAsync<ReadApiJobRequest,ReadApiJobResponse>(new(Guid.NewGuid(),jobId),ct)).Result??throw new KeyNotFoundException();var auth=stored.Authorization;var session=auth.Session;
        if(stored.Public.State!="Running"||stored.CancelRequested||auth.AccessExpiresAt<=DateTime.UtcNow||session.ExpiresAt<=DateTime.UtcNow)throw new UnauthorizedAccessException("Работа требует нового разрешения.");
        var claims=new[]{new Claim(IdentityClaimNames.UserId,session.UserId.ToString()),new Claim(IdentityClaimNames.ProviderId,session.ProviderId.ToString()),new Claim(IdentityClaimNames.Epoch,session.Epoch.ToString(CultureInfo.InvariantCulture)),new Claim(IdentityClaimNames.Issuer,auth.Issuer),new Claim(IdentityClaimNames.Subject,auth.Subject),new Claim(IdentityClaimNames.ClientId,session.ClientId),new Claim(IdentityClaimNames.AccessExpiresAt,auth.AccessExpiresAt.ToString("O",CultureInfo.InvariantCulture))};
        background.Install(new(new ClaimsIdentity(claims,ActiveBearerHandler.SchemeName)));scope.BeginBackgroundApi();scope.SetApiAccessExpiry(auth.AccessExpiresAt);
        var current=(await bus.SendAsync<ValidateApiSessionRequest,ValidateApiSessionResponse>(new(Guid.NewGuid(),session.Id,stored.Operation.CapabilityId),ct)).Result??throw new UnauthorizedAccessException();if(current.UserId!=session.UserId||current.ClientId!=session.ClientId||current.Epoch!=session.Epoch||!current.Spaces.Allows(stored.Public.SpaceId))throw new UnauthorizedAccessException();
        scope.SetApiOperation(stored.Operation.OperationId,stored.Operation.RequestHash);return current;
    }
}

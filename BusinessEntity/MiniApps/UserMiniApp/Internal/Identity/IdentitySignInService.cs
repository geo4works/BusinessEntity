using System.Security.Claims;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Шесть зависимостей завершают один flow: привязка, журнал, права, профиль, bootstrap и проверка внешнего reset.
// Завершает проверенный протоколом вход, не доверяя совпадению внешних имён.
internal sealed class IdentitySignInService(IdentityDirectory directory, IdentityStateStore store,
    UserSecurityAuthority authority, IIdentityProviderRegistry registry, IdentityAdmissionGate admission, IdentityProvisioners provisioners) : IIdentitySignInHandler
{
    // Probe не меняет активного провайдера и никогда не выдаёт рабочую сессию целевой системы.
    public async Task<ClaimsPrincipal> CompleteAsync(VerifiedExternalIdentity identity, long epoch, Guid? probePlanId, CancellationToken ct)
    {
        using var accepted=admission.Enter(epoch,()=>registry.Current.Epoch);
        if (epoch != registry.Current.Epoch) throw new UnauthorizedAccessException("Начатый вход относится к старой эпохе.");
        var passwordLink=await store.ReadAsync(db=>db.ExternalIdentities.SingleOrDefaultAsync(x=>x.Issuer==identity.Issuer&&x.Subject==identity.Subject,ct));
        if(passwordLink?.RequiresPasswordChange==true)
        {
            var profile=registry.Profiles.Single(x=>x.Id==identity.ProviderId);
            var account=await provisioners.Get(profile).ReadAsync(profile,passwordLink.AccountId,ct);
            if(!account.Enabled||account.Subject!=identity.Subject||account.RequiresPasswordChange)
                throw new UnauthorizedAccessException("Сначала смените временный пароль у провайдера.");
            await store.ReadAsync(async db=>{var current=await db.ExternalIdentities.SingleAsync(x=>x.Id==passwordLink.Id,ct);
                current.RequiresPasswordChange=false;await db.SaveChangesAsync(ct);return true;});
        }
        if (probePlanId.HasValue)
        {
            var actor = await authority.RequireGlobalAdminAsync(ct);
            var plan = await store.ReadAsync(db => db.IdentitySwitchPlans.SingleAsync(x => x.Id == probePlanId.Value, ct));
            if (plan.ActorUserId != actor.Id || plan.TargetProviderId != identity.ProviderId
                || plan.TargetRevision != registry.Profiles.Single(x => x.Id == identity.ProviderId).Revision
                || plan.SourceEpoch != epoch || plan.State != "Ready") throw new UnauthorizedAccessException("Пробный вход не соответствует плану.");
            var linked = await store.ReadAsync(db => db.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == identity.Issuer && x.Subject == identity.Subject, ct));
            if (linked == null || !linked.Enabled || !await authority.IsGlobalAdminAsync(linked.UserId, ct))
                throw new UnauthorizedAccessException("Целевая identity не связана с локальным GlobalAdmin.");
            plan.VerifiedAdminId = linked.UserId; plan.VerifiedAt = DateTime.UtcNow;
            await store.WriteAsync(actor.Id, [plan], ct: ct);
            return new ClaimsPrincipal(new ClaimsIdentity());
        }
        if (registry.Current.Profile.Id != identity.ProviderId) throw new UnauthorizedAccessException("Провайдер больше не активен.");
        var user = await directory.ResolveAsync(identity, ct);
        await directory.BootstrapAdministratorAsync(user, identity, ct);
        return IdentityDirectory.CreatePrincipal(user, identity, epoch);
    }
}

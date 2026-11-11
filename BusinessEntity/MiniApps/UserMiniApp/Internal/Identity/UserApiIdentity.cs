using System.Security.Claims;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Проверенный API subject связывается с локальным пользователем, не предоставляя grant автоматически.
internal sealed class UserApiIdentity(IdentityStateStore store,IIdentityProviderRegistry registry,
    IdentityProvisioners provisioners,IdentityAdmissionGate admission) : IApiIdentityHandler
{
    // При разных issuer используется доказательство адаптера, а не совпадение login/email.
    public async Task<ClaimsPrincipal> CompleteAsync(VerifiedAccessIdentity identity,CancellationToken ct)
    {
        using var accepted=admission.Enter(identity.Epoch,()=>registry.Current.Epoch);
        var profile=registry.Current.Profile;
        if(profile.Id!=identity.ProviderId)throw new UnauthorizedAccessException();
        var user=await store.ReadAsync(async db=>
        {
            var link=await db.ExternalIdentities.SingleOrDefaultAsync(x=>x.Issuer==identity.Issuer&&x.Subject==identity.Subject,ct);
            if(link==null&&profile.ProvisioningKind!="None")
            {
                var api=profile.ApiTokenProfiles.Single(x=>x.Issuer==identity.Issuer&&x.AllowedClientIds.Contains(identity.ClientId));
                var account=await provisioners.Get(profile).ResolveAccessSubjectAsync(profile,api,identity.Subject,ct);
                if(account==null||!account.Enabled||account.RequiresPasswordChange)throw new UnauthorizedAccessException();
                var known=await db.ExternalIdentities.SingleOrDefaultAsync(x=>x.Issuer==profile.Issuer&&x.Subject==account.Subject&&x.Enabled,ct);
                if(known==null)throw new UnauthorizedAccessException("Служебная identity ещё не связана локальным администратором.");
                link=new ExternalIdentityLinkRow {Id=Guid.NewGuid(),UserId=known.UserId,ProviderId=profile.Id,Issuer=identity.Issuer,
                    Subject=identity.Subject,AccountId=account.AccountId,ProviderLogin=account.Login};
                db.ExternalIdentities.Add(link);
                try{await db.SaveChangesAsync(ct);}
                catch(DbUpdateException ex)when(ex.InnerException is PostgresException{SqlState:"23505"})
                {db.ChangeTracker.Clear();link=await db.ExternalIdentities.SingleAsync(x=>x.Issuer==identity.Issuer&&x.Subject==identity.Subject,ct);if(link.UserId!=known.UserId)throw new UnauthorizedAccessException();}
            }
            if(link==null||!link.Enabled||link.RequiresPasswordChange)throw new UnauthorizedAccessException();
            return await db.Users.SingleOrDefaultAsync(x=>x.Id==link.UserId&&x.Enabled&&x.Kind!="LocalSystem",ct)??throw new UnauthorizedAccessException();
        });
        var principal=IdentityDirectory.CreatePrincipal(user,new(identity.ProviderId,identity.Issuer,identity.Subject,user.Login,
            UserValueRules.ReadUserData(user).DisplayedName,null,user.Kind),identity.Epoch);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new(IdentityClaimNames.ClientId,identity.ClientId));
        if(identity.AccessExpiresAt!=default)((ClaimsIdentity)principal.Identity!).AddClaim(new(IdentityClaimNames.AccessExpiresAt,identity.AccessExpiresAt.ToString("O",System.Globalization.CultureInfo.InvariantCulture)));
        return new(new ClaimsIdentity(principal.Claims,ActiveBearerHandler.SchemeName));
    }
}

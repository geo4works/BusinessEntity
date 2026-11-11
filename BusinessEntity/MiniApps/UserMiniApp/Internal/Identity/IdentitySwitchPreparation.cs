using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Подготовка и provisioning не переключают рабочий профиль даже при частично созданных аккаунтах.
internal sealed class IdentitySwitchPreparation(UserSecurityAuthority authority,IdentityStateStore store,
    IdentityRuntime runtime,IdentityProvisioners provisioners,IdentityProfileValidation validation) : BusinessEntity.MiniApps.UserMiniApp.Contracts.IIdentityProbeAuthorizer
{
    // Непосредственно перед активацией снова проверяется доступность цели и административной привязки.
    public async Task ValidateActivationTargetAsync(IdentitySwitchPlanRow plan,CancellationToken ct)
    {
        var profile=runtime.Get(plan.TargetProviderId,plan.TargetRevision);
        await validation.ValidateAsync(profile,ct);
        var link=await store.ReadAsync(db=>db.ExternalIdentities.SingleOrDefaultAsync(x=>x.UserId==plan.VerifiedAdminId
            &&x.ProviderId==profile.Id&&x.Issuer==profile.Issuer&&x.Enabled&&!x.RequiresPasswordChange
            &&db.Users.Any(u=>u.Id==x.UserId&&u.Enabled),ct));
        if(link==null)throw new UnauthorizedAccessException("Целевая административная привязка больше недоступна.");
        if(profile.ProvisioningKind!="None")
        {
            var adapter=provisioners.Get(profile);
            var account=string.IsNullOrEmpty(link.AccountId)?await adapter.FindAsync(profile,link.ProviderLogin,ct):await adapter.ReadAsync(profile,link.AccountId,ct);
            if(account==null||!account.Enabled||account.RequiresPasswordChange||account.Subject!=link.Subject)
                throw new UnauthorizedAccessException("Целевой административный аккаунт больше недоступен.");
        }
    }
    // Фиксирует пользователей и все обнаруженные конфликты до изменения внешнего каталога.
    public Task<IdentitySwitchPlanRow> BuildAsync(Guid targetId,CancellationToken ct=default) => store.ExclusiveAsync(async () =>
    {
        var actor=await authority.RequireGlobalAdminAsync(ct); var active=runtime.Current;
        var target=await store.ReadAsync(db=>db.IdentityProfiles.SingleAsync(x=>x.Id==targetId,ct));
        if(target.ValidatedRevision!=target.Revision) throw new InvalidOperationException("Сначала проверьте текущую ревизию профиля.");
        var profile=runtime.Get(targetId,target.Revision);
        if(!profile.Enabled) throw new InvalidOperationException("Профиль отключён.");
        var users=await store.ReadAsync(db=>db.Users.Where(x=>x.Enabled&&x.Kind!="LocalSystem").OrderBy(x=>x.Id).ToArrayAsync(ct));
        var links=await store.ReadAsync(db=>db.ExternalIdentities.Where(x=>x.Enabled).ToArrayAsync(ct));
        var entries=new List<IdentitySwitchEntry>();
        foreach(var user in users)
        {
            var link=links.SingleOrDefault(x=>x.UserId==user.Id&&x.Issuer==profile.Issuer);
            try
            {
                if(profile.ProvisioningKind=="None")
                {
                    entries.Add(new(user.Id,user.Login,user.Kind,link==null?"Blocked":"Linked",Guid.NewGuid(),link?.AccountId,link?.Subject,
                        link==null?"identity.capability_unavailable: нет подтверждённой связи и provisioning.":null)); continue;
                }
                var adapter=provisioners.Get(profile);
                var account=link!=null&&!string.IsNullOrEmpty(link.AccountId)
                    ? await adapter.ReadAsync(profile,link.AccountId,ct) : await adapter.FindAsync(profile,user.Login,ct);
                if(link!=null)
                {
                    if(account==null||account.Subject!=link.Subject||account.Issuer!=link.Issuer||!account.Enabled)
                        throw new InvalidOperationException("Подтверждённый целевой аккаунт исчез или изменился.");
                    entries.Add(new(user.Id,user.Login,user.Kind,"Linked",Guid.NewGuid(),account.AccountId,account.Subject));
                }
                else if(account!=null)
                {
                    var known=links.SingleOrDefault(x=>x.Issuer==account.Issuer&&x.Subject==account.Subject);
                    if(known?.UserId!=user.Id) throw new InvalidOperationException("identity.username_conflict: логин занят другой identity.");
                    entries.Add(new(user.Id,user.Login,user.Kind,"Linked",Guid.NewGuid(),account.AccountId,account.Subject));
                }
                else if(user.Kind=="Human"||(user.Kind=="Service"&&adapter is IIdentityServiceProvisioningAdapter)) entries.Add(new(user.Id,user.Login,user.Kind,"Missing",Guid.NewGuid()));
                else entries.Add(new(user.Id,user.Login,user.Kind,"Blocked",Guid.NewGuid(),Error:"Служебной identity нужна отдельная подготовка credentials."));
            }
            catch(Exception ex) when(ex is not OperationCanceledException)
            { entries.Add(new(user.Id,user.Login,user.Kind,"Conflict",Guid.NewGuid(),Error:ex.Message)); }
        }
        var plan=new IdentitySwitchPlanRow { Id=Guid.NewGuid(),ActorUserId=actor.Id,SourceProviderId=active.Profile.Id,
            TargetProviderId=targetId,TargetRevision=target.Revision,SourceEpoch=active.Epoch,CreatedAt=DateTime.UtcNow,
            UsersFingerprint=Fingerprint(users),Json=JsonSerializer.Serialize(entries,IdentityStateStore.Json),
            State=entries.Any(x=>x.Status is "Conflict" or "Blocked")?"Failed":entries.Any(x=>x.Status=="Missing")?"Validated":"Ready" };
        await store.WriteAsync(actor.Id,[plan],ct:ct); return plan;
    },ct);
    // Журнал каждого create записывается до внешнего эффекта; повтор сверяет operation marker, а не только имя.
    public Task<IReadOnlyList<IdentityProvisioningCredential>> ProvisionAsync(Guid id,CancellationToken ct=default) => store.ExclusiveAsync(async () =>
    {
        var actor=await authority.RequireGlobalAdminAsync(ct); var plan=await GetOwnedPlanAsync(id,actor.Id,ct);
        if(plan.State=="Active") throw new InvalidOperationException("План уже активирован.");
        await CheckCurrentAsync(plan,ct); var profile=runtime.Get(plan.TargetProviderId,plan.TargetRevision);
        var adapter=provisioners.Get(profile); await adapter.ValidateAsync(profile,ct);
        var entries=JsonSerializer.Deserialize<List<IdentitySwitchEntry>>(plan.Json,IdentityStateStore.Json)!;
        if(entries.Any(x=>x.Status is "Conflict" or "Blocked")) throw new InvalidOperationException("План содержит конфликты.");
        var passwords=new List<IdentityProvisioningCredential>(); plan.State="Provisioning";
        await store.WriteAsync(actor.Id,[plan],ct:ct);
        for(var i=0;i<entries.Count;i++)
        {
            if(entries[i].Status is not ("Missing" or "Creating")) continue;
            entries[i]=entries[i] with { Status="Creating",Error=null }; plan.Json=JsonSerializer.Serialize(entries,IdentityStateStore.Json);
            await store.WriteAsync(actor.Id,[plan],ct:ct);
            try
            {
                var password=UserAccountProvisioning.GeneratePassword();
                ProvisionedIdentity account;
                if(entries[i].Kind=="Service")
                {
                    if(adapter is not IIdentityServiceProvisioningAdapter services)throw new NotSupportedException("Нет service provisioning capability.");
                    var provisioned=await services.CreateServiceAsync(profile,entries[i].Login,entries[i].ProvisioningOperationId,ct);
                    account=provisioned.Account;password=provisioned.Credential;
                }
                else account=await adapter.CreateAsync(profile,entries[i].Login,password,entries[i].ProvisioningOperationId,ct);
                var row=new ExternalIdentityLinkRow { Id=Guid.NewGuid(),UserId=entries[i].UserId,ProviderId=profile.Id,
                    Issuer=account.Issuer,Subject=account.Subject,AccountId=account.AccountId,ProviderLogin=account.Login,
                    ProvisioningOperationId=entries[i].ProvisioningOperationId,RequiresPasswordChange=entries[i].Kind=="Human" };
                entries[i]=entries[i] with { Status="Created",AccountId=account.AccountId,Subject=account.Subject };
                plan.Json=JsonSerializer.Serialize(entries,IdentityStateStore.Json);
                await store.WriteAsync(actor.Id,[row,plan],ct:ct); passwords.Add(new(account.Login,password,entries[i].Kind));
            }
            catch(Exception ex) when(ex is not OperationCanceledException)
            {
                entries[i]=entries[i] with { Status="Creating",Error=ex.Message }; plan.State="NeedsReconciliation";
                plan.Json=JsonSerializer.Serialize(entries,IdentityStateStore.Json); await store.WriteAsync(actor.Id,[plan],ct:ct); throw;
            }
        }
        plan.State="Ready"; plan.Json=JsonSerializer.Serialize(entries,IdentityStateStore.Json);
        await store.WriteAsync(actor.Id,[plan],ct:ct); return (IReadOnlyList<IdentityProvisioningCredential>)passwords;
    },ct);
    // Результат внешней операции не сохраняет пароль; потерявший выдачу администратор явно запрашивает новый.
    public Task<IReadOnlyList<IdentityProvisioningCredential>> ReissueAsync(Guid id,CancellationToken ct=default) => store.ExclusiveAsync(async () =>
    {
        var actor=await authority.RequireGlobalAdminAsync(ct);var plan=await GetOwnedPlanAsync(id,actor.Id,ct);await CheckCurrentAsync(plan,ct);
        var profile=runtime.Get(plan.TargetProviderId,plan.TargetRevision);var adapter=provisioners.Get(profile);
        var links=await store.ReadAsync(db=>db.ExternalIdentities.Where(x=>x.ProviderId==profile.Id&&x.ProvisioningOperationId!=null).ToArrayAsync(ct));
        var entries=JsonSerializer.Deserialize<IdentitySwitchEntry[]>(plan.Json,IdentityStateStore.Json)!;
        var result=new List<IdentityProvisioningCredential>();
        foreach(var entry in entries.Where(x=>x.Status=="Created"))
        {
            var link=links.Single(x=>x.UserId==entry.UserId&&x.ProvisioningOperationId==entry.ProvisioningOperationId);
            var current=await adapter.ReadAsync(profile,link.AccountId,ct);
            if(entry.Kind=="Service")
            {
                if(adapter is not IIdentityServiceProvisioningAdapter services)throw new NotSupportedException();
                result.Add(new(entry.Login,await services.RotateServiceCredentialAsync(profile,link.AccountId,entry.ProvisioningOperationId,ct),"Service"));continue;
            }
            if(!current.RequiresPasswordChange) continue;
            var password=UserAccountProvisioning.GeneratePassword();await adapter.SetTemporaryPasswordAsync(profile,link.AccountId,password,ct);
            result.Add(new(entry.Login,password));
        }
        return (IReadOnlyList<IdentityProvisioningCredential>)result;
    },ct);
    // Probe разрешён только инициатору готового актуального плана.
    public async Task<BusinessEntity.MiniApps.UserMiniApp.Contracts.IdentityProbe> AuthorizeProbeAsync(Guid id,CancellationToken ct)
    {var plan=await ReadAsync(id,ct);await CheckCurrentAsync(plan,ct);if(plan.State!="Ready")throw new InvalidOperationException("План ещё не готов.");return new(plan.TargetProviderId,plan.TargetRevision,plan.SourceEpoch);}
    // История собственных планов позволяет продолжить процесс после перезагрузки страницы.
    public async Task<IdentitySwitchPlanRow[]> ListAsync(CancellationToken ct=default)
    {var actor=await authority.RequireGlobalAdminAsync(ct);return await store.ReadAsync(db=>db.IdentitySwitchPlans.Where(x=>x.ActorUserId==actor.Id).OrderByDescending(x=>x.CreatedAt).Take(20).ToArrayAsync(ct));}
    // Страница видит только план своей проверки; общая история доступна через отдельную административную выборку.
    public async Task<IdentitySwitchPlanRow> ReadAsync(Guid id,CancellationToken ct=default)
    {var actor=await authority.RequireGlobalAdminAsync(ct);return await GetOwnedPlanAsync(id,actor.Id,ct);}
    // Перед каждой фазой сверяется источник и снимок пользователей, а не только время создания плана.
    internal async Task CheckCurrentAsync(IdentitySwitchPlanRow plan,CancellationToken ct)
    {
        var current=runtime.Current;
        var users=await store.ReadAsync(db=>db.Users.Where(x=>x.Enabled&&x.Kind!="LocalSystem").OrderBy(x=>x.Id).ToArrayAsync(ct));
        var target=await store.ReadAsync(db=>db.IdentityProfiles.SingleAsync(x=>x.Id==plan.TargetProviderId,ct));
        if(current.Profile.Id!=plan.SourceProviderId||current.Epoch!=plan.SourceEpoch||target.Revision!=plan.TargetRevision
            ||target.ValidatedRevision!=target.Revision||Fingerprint(users)!=plan.UsersFingerprint)
            throw new InvalidOperationException("identity.plan_stale: постройте новый план.");
    }
    // Стабильный fingerprint включает только идентичность/переносимый состав, не приватные данные профиля.
    internal static string Fingerprint(IEnumerable<BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos.UserDto> users)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(users.OrderBy(x=>x.Id)
            .Select(x=>new {x.Id,x.Login,x.Kind,x.Enabled})))));
    // Только инициатор продолжает свой план и получает одноразовые credentials.
    private async Task<IdentitySwitchPlanRow> GetOwnedPlanAsync(Guid id,Guid actor,CancellationToken ct)
    {
        var plan=await store.ReadAsync(db=>db.IdentitySwitchPlans.SingleAsync(x=>x.Id==id,ct));
        return plan.ActorUserId==actor?plan:throw new UnauthorizedAccessException("План принадлежит другому администратору.");
    }
}

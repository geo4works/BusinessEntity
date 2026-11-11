using System.Text.Json;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Страница владельца управляет metadata, проверкой и ревизиями без неявной активации.
internal sealed class IdentityProviderAdministration(UserSecurityAuthority authority, IdentityStateStore store,
    IdentityRuntime runtime, IdentityProfileValidation validation, IdentityProvisioners provisioners,IIdentitySecretResolver secrets)
{
    // Администратор видит только наличие секретов сохранённого профиля, без их значений.
    public async Task<string> SecretStatusAsync(Guid id,CancellationToken ct=default)
    {
        await authority.RequireGlobalAdminAsync(ct);
        var row=await store.ReadAsync(db=>db.IdentityProfiles.SingleAsync(x=>x.Id==id,ct));
        var profile=JsonSerializer.Deserialize<IdentityProviderProfile>(row.Json,IdentityStateStore.Json)!;
        bool Present(string reference){try{_ = secrets.Resolve(reference);return true;}catch(InvalidOperationException){return false;}}
        return $"Секрет приложения: {(Present(profile.ClientSecretRef)?"настроен":"не настроен")}. Управление: {(profile.ProvisioningKind=="None"?"не требуется":Present(profile.ManagementSecretRef)?"настроено":"не настроено")}.";
    }
    // Неиспользованный неактивный профиль удаляется целиком; использованный сохраняет аудит.
    public Task<bool> DeleteAsync(Guid id,CancellationToken ct=default)=>store.ExclusiveAsync(async()=>
    {
        var actor=await authority.RequireGlobalAdminAsync(ct);
        if(runtime.Current.Profile.Id==id)throw new InvalidOperationException("Активный профиль нельзя удалить.");
        var used=await store.ReadAsync(async db=>await db.ExternalIdentities.AnyAsync(x=>x.ProviderId==id,ct)
            ||await db.IdentitySwitchPlans.AnyAsync(x=>x.SourceProviderId==id||x.TargetProviderId==id,ct));
        if(used)throw new InvalidOperationException("Использованный профиль можно отключить, сохранив аудит.");
        var revisions=await store.ReadAsync(db=>db.IdentityProfileRevisions.Where(x=>x.ProviderId==id).ToArrayAsync(ct));
        var changes=revisions.Select(x=>new UserTableMutation(nameof(IdentityProfileRevisionRow),x.Id,null))
            .Append(new(nameof(IdentityProfileRow),id,null)).ToArray();
        await store.WriteMutationsAsync(actor.Id,changes,null,ct);return true;
    },ct);
    // Список доступен только текущему локальному GlobalAdmin.
    public async Task<IdentityProfileRow[]> ListAsync(CancellationToken ct = default)
    { await authority.RequireGlobalAdminAsync(ct); return await store.ReadAsync(db => db.IdentityProfiles.OrderBy(x => x.Id).ToArrayAsync(ct)); }
    // Прикладной сценарий можно выполнить напрямую; UI SaveContext использует ту же подготовку.
    public Task<IdentityProviderProfile> SaveAsync(IdentityProviderProfile proposed,CancellationToken ct=default) => store.ExclusiveAsync(async () =>
    {
        var actor=await authority.RequireGlobalAdminAsync(ct);var prepared=await PrepareAsync(proposed,ct);
        await store.WriteAsync(actor.Id,prepared.Rows,ct:ct);await PublishAsync(prepared.Profile);return prepared.Profile;
    },ct);
    // Фиксирует immutable revision без commit; технический writer проверит конкурирующую ревизию ещё раз.
    public async Task<PreparedIdentityProfile> PrepareAsync(IdentityProviderProfile proposed,CancellationToken ct)
    {
        await authority.RequireGlobalAdminAsync(ct);
        var previous=await store.ReadAsync(db=>db.IdentityProfiles.SingleOrDefaultAsync(x=>x.Id==proposed.Id,ct));
        if(previous!=null&&previous.Revision!=proposed.Revision)throw new InvalidOperationException("identity.revision_conflict");
        var links=await store.ReadAsync(db=>db.ExternalIdentities.Where(x=>x.ProviderId==proposed.Id).ToArrayAsync(ct));
        if(links.Any(x=>x.Issuer!=proposed.Issuer))throw new InvalidOperationException("Для нового issuer создайте отдельный профиль.");
        var profile=proposed with {Revision=previous==null?1:previous.Revision+1};
        await validation.ValidateAsync(profile,ct);
        var json=JsonSerializer.Serialize(profile,IdentityStateStore.Json);
        return new(profile,[new IdentityProfileRow {Id=profile.Id,Revision=profile.Revision,Json=json},
            new IdentityProfileRevisionRow {Id=Guid.NewGuid(),ProviderId=profile.Id,Revision=profile.Revision,Json=json}]);
    }
    // Публикация не изменяет ActiveProviderId, даже если профиль был отредактирован.
    public Task PublishAsync(IdentityProviderProfile profile)=>runtime.AddAsync(profile);
    // Проверка provisioning отделена от стандартной OIDC-проверки для read-only внешних провайдеров.
    public Task<bool> ValidateAsync(Guid id, CancellationToken ct = default) => store.ExclusiveAsync(async () =>
    {
        var actor=await authority.RequireGlobalAdminAsync(ct);
        var row=await store.ReadAsync(db => db.IdentityProfiles.SingleAsync(x=>x.Id==id,ct));
        var profile=JsonSerializer.Deserialize<IdentityProviderProfile>(row.Json,IdentityStateStore.Json)!;
        await validation.ValidateAsync(profile,ct);
        if(profile.ProvisioningKind!="None") await provisioners.Get(profile).ValidateAsync(profile,ct);
        row.ValidatedRevision=row.Revision; row.ValidatedAt=DateTime.UtcNow;
        await store.WriteAsync(actor.Id,[row],ct:ct); return true;
    },ct);
    // Активные и уже использованные профили не удаляются; неиспользованный draft можно отключить.
    public async Task DisableAsync(Guid id,CancellationToken ct=default)
    {
        var actor=await authority.RequireGlobalAdminAsync(ct);
        if(runtime.Current.Profile.Id==id) throw new InvalidOperationException("Активный профиль нельзя отключить.");
        var row=await store.ReadAsync(db=>db.IdentityProfiles.SingleAsync(x=>x.Id==id,ct));
        var profile=JsonSerializer.Deserialize<IdentityProviderProfile>(row.Json,IdentityStateStore.Json)!;
        await SaveAsync(profile with { Enabled=false },ct);
    }
}

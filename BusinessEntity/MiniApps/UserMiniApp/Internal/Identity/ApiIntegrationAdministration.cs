using System.Text.Json;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// User владеет API grants; HTTP miniapp не получает management API или user tables.
internal sealed class ApiIntegrationAdministration(UserSecurityAuthority authority,IdentityStateStore store,
    IIdentityProviderRegistry registry,ApiCapabilityCatalog catalog,ApiServiceIdentityLinking linking)
{
    // Список не выдаёт credentials и доступен только действующему локальному GlobalAdmin.
    public async Task<ApiAccessGrantRow[]> ListAsync(CancellationToken ct=default)
    {await authority.RequireGlobalAdminAsync(ct);return await store.ReadAsync(db=>db.ApiAccessGrants.OrderBy(x=>x.ClientId).ToArrayAsync(ct));}
    // Только подтверждённые локальные Human/Service доступны для выдачи допуска.
    public async Task<UserDto[]> UsersAsync(CancellationToken ct=default)
    {await authority.RequireGlobalAdminAsync(ct);return await store.ReadAsync(db=>db.Users.Where(x=>x.Enabled&&x.Kind!="LocalSystem").OrderBy(x=>x.Login).ToArrayAsync(ct));}
    // Служебная identity связывается по проверенному management subject mode, без пароля в форме grant.
    public Task LinkServiceAsync(string login,CancellationToken ct=default)=>linking.LinkAsync(login,ct);
    // Prepare не выполняет commit; UI записывает grant через общий MiniAppSaveContext.
    public async Task<ApiAccessGrantRow> PrepareAsync(ApiGrantForm form,CancellationToken ct)
    {
        await authority.RequireGlobalAdminAsync(ct);
        if(!registry.Current.Profile.ApiTokenProfiles.SelectMany(x=>x.AllowedClientIds).Contains(form.ClientId,StringComparer.Ordinal))
            throw new ArgumentException("OAuth client не разрешён активным профилем.");
        if(!await store.ReadAsync(db=>db.Users.AnyAsync(x=>x.Id==form.UserId&&x.Enabled&&x.Kind!="LocalSystem",ct)))throw new ArgumentException("Выберите включённого пользователя.");
        var caps=form.Capabilities.Split([',','\n','\r'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
        if(caps.Any(x=>!catalog.Definitions.Any(d=>d.Id==x)))throw new ArgumentException("Неизвестная capability.");
        var ids=form.SpaceIds.Split([',','\n','\r'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(Guid.Parse).Distinct().ToArray();
        var existing=await store.ReadAsync(db=>db.ApiAccessGrants.SingleOrDefaultAsync(x=>x.Id==form.Id,ct));
        if(existing!=null&&(existing.UserId!=form.UserId||existing.ClientId!=form.ClientId||existing.Revision!=form.Revision))
            throw new ApiFault(409,"api.grant_conflict","Субъект/client неизменяемы; обновите сохранённую ревизию.");
        if(await store.ReadAsync(db=>db.ApiAccessGrants.AnyAsync(x=>x.Id!=form.Id&&x.UserId==form.UserId&&x.ClientId==form.ClientId,ct)))
            throw new ApiFault(409,"api.grant_conflict","Для этой пары субъект/client допуск уже существует.");
        return new() {Id=form.Id,UserId=form.UserId,ClientId=form.ClientId,Revision=existing==null?1:existing.Revision+1,Enabled=form.Enabled,
            CapabilitiesJson=JsonSerializer.Serialize(caps,IdentityStateStore.Json),SpacesJson=JsonSerializer.Serialize(new ApiSpaceScope(form.AllSpaces,ids),IdentityStateStore.Json)};
    }
}

// Связывание каталога отделено от редактирования grant и никогда не выдаёт application roles.
internal sealed class ApiServiceIdentityLinking(UserSecurityAuthority authority,IdentityStateStore store,
    IIdentityProviderRegistry registry,IdentityProvisioners provisioners,IdentityDirectory directory,IdentityAdmissionGate admission)
{
    // Администратор выбирает существующий service account; совпадение имени с локальной другой identity — конфликт.
    public async Task LinkAsync(string login,CancellationToken ct)
    {
        await authority.RequireGlobalAdminAsync(ct);var active=registry.Current;
        using var accepted=admission.Enter(active.Epoch,()=>registry.Current.Epoch);
        var adapter=provisioners.Get(active.Profile);await adapter.ValidateAsync(active.Profile,ct);
        var account=await adapter.FindAsync(active.Profile,login.Trim(),ct)??throw new KeyNotFoundException();
        if(account.AccountKind!="Service"||!account.Enabled||account.RequiresPasswordChange)throw new ArgumentException("Нужна включённая служебная учётная запись без human reset.");
        var user=await directory.ResolveAsync(new(active.Profile.Id,account.Issuer,account.Subject,account.Login,account.DisplayName,null,"Service"),ct);
        if(user.Kind!="Service")throw new ApiFault(409,"identity.kind_conflict","Локальная учётная запись имеет другой тип.");
        await store.ReadAsync(async db=>{var link=await db.ExternalIdentities.SingleAsync(x=>x.UserId==user.Id&&x.Issuer==account.Issuer&&x.Subject==account.Subject,ct);
            link.AccountId=account.AccountId;await db.SaveChangesAsync(ct);return true;});
    }
}

// Форма содержит только metadata; M2M credentials здесь не принимаются.
internal sealed class ApiGrantForm
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public Guid UserId {get;set;}
    public string ClientId {get;set;}=string.Empty;
    public long Revision {get;set;}
    public bool Enabled {get;set;}=true;
    public string Capabilities {get;set;}="user.self.read";
    public bool AllSpaces {get;set;}
    public string SpaceIds {get;set;}=string.Empty;
    // Снимок не разделяет mutable форму с последующим вводом.
    public ApiGrantForm Copy()=>JsonSerializer.Deserialize<ApiGrantForm>(JsonSerializer.Serialize(this))!;
    // Загрузка сохранённой записи не использует raw token claims.
    public static ApiGrantForm From(ApiAccessGrantRow row)
    {var spaces=UserApiSessions.Read<ApiSpaceScope>(row.SpacesJson);return new(){Id=row.Id,UserId=row.UserId,ClientId=row.ClientId,Revision=row.Revision,Enabled=row.Enabled,
        Capabilities=string.Join(", ",UserApiSessions.Read<string[]>(row.CapabilitiesJson)),AllSpaces=spaces.AllSpaces,SpaceIds=string.Join(", ",spaces.SpaceIds??[])};}
}

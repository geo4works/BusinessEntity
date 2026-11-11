using System.Security.Claims;
using System.Text.Json;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Устойчивые security sessions не держат circuit, mutable working set или bearer credentials.
internal sealed class UserApiSessions(UserSecurityAuthority authority,IdentityStateStore store,
    IIdentityProviderRegistry registry,BusRequestScope scope,IdentityAdmissionGate admission,ApiSecuritySettings settings)
{
    // Открытие фиксирует ограничения текущего grant, а не копирует полномочия из JSON клиента.
    public async Task<ApiSessionInfo> OpenAsync(ApiSessionOptions requested,CancellationToken ct)
    {
        var user=await authority.CurrentAsync(ct)??throw new ApiFault(401,"api.identity_invalid","Требуется API identity.");
        var client=await ClientAsync(ct);var active=registry.Current;
        using var accepted=admission.Enter(active.Epoch,()=>registry.Current.Epoch);
        return await store.ReadAsync(async db=>
        {
            var grant=await db.ApiAccessGrants.SingleOrDefaultAsync(x=>x.UserId==user.Id&&x.ClientId==client&&x.Enabled,ct)
                ??throw new ApiFault(403,"api.grant_denied","Нет локального допуска интеграции.");
            var allowed=Read<string[]>(grant.CapabilitiesJson);var spaces=Read<ApiSpaceScope>(grant.SpacesJson);
            var caps=requested.Capabilities??allowed;
            if(caps.Except(allowed,StringComparer.Ordinal).Any())throw new ApiFault(403,"api.capability_denied","Запрошенные возможности не разрешены.");
            var narrowed=requested.Spaces??spaces;
            if((narrowed.AllSpaces&&!spaces.AllSpaces)||(narrowed.SpaceIds??[]).Any(id=>!spaces.Allows(id)))
                throw new ApiFault(403,"api.space_denied","Пространство не разрешено допуском.");
            var now=new DateTime(DateTime.UtcNow.Ticks/10*10,DateTimeKind.Utc);
            var row=new ApiSessionRow {Id=Guid.NewGuid(),UserId=user.Id,ProviderId=active.Profile.Id,Epoch=active.Epoch,
                ClientId=client,GrantId=grant.Id,GrantRevision=grant.Revision,CapabilitiesJson=JsonSerializer.Serialize(caps,IdentityStateStore.Json),SpacesJson=JsonSerializer.Serialize(narrowed,IdentityStateStore.Json),CreatedAt=now,ExpiresAt=now+settings.SessionLifetime};
            db.ApiSessions.Add(row);await db.SaveChangesAsync(ct);return Info(row);
        });
    }
    // Grant revision, срок, субъект, client и epoch проверяются заново при каждом HTTP запросе.
    public async Task<ApiSessionInfo> ValidateAsync(Guid id,string? capability,CancellationToken ct,bool closing=false)
    {
        var user=await authority.CurrentAsync(ct)??throw new ApiFault(401,"api.identity_invalid","Требуется API identity.");
        var client=await ClientAsync(ct);var current=registry.Current;
        return await store.ReadAsync(async db=>
        {
            var row=await db.ApiSessions.SingleOrDefaultAsync(x=>x.Id==id&&x.UserId==user.Id&&x.ClientId==client,ct);
            if(row==null||row.ProviderId!=current.Profile.Id||row.Epoch!=current.Epoch||row.ExpiresAt<=DateTime.UtcNow||(!closing&&row.State!="Active"))
                throw new ApiFault(401,"api.session_invalid","Сессия отсутствует, отозвана или устарела.");
            var grant=await db.ApiAccessGrants.SingleOrDefaultAsync(x=>x.Id==row.GrantId&&x.UserId==user.Id&&x.ClientId==client&&x.Enabled,ct);
            if(grant==null||grant.Revision!=row.GrantRevision)throw new ApiFault(401,"api.session_grant_revoked","Допуск интеграции изменён или отозван.");
            var info=Info(row);
            if(capability!=null&&!info.Capabilities.Contains(capability,StringComparer.Ordinal))throw new ApiFault(403,"api.capability_denied","Возможность не разрешена сессией.");
            scope.SetApiContext(row.Id,client);
            return info;
        });
    }
    // Принятые операции не откатываются закрытием; новые вызовы этой сессии не допускаются.
    public async Task CloseAsync(Guid id,CancellationToken ct)
    {
        await ValidateAsync(id,null,ct,closing:true);
        using var accepted=admission.Enter(registry.Current.Epoch,()=>registry.Current.Epoch);
        await store.ReadAsync(async db=>{var row=await db.ApiSessions.SingleAsync(x=>x.Id==id,ct);row.State="Revoked";await db.SaveChangesAsync(ct);return true;});
    }
    // Client identity получена из проверенного access token; обычная UI cookie её не содержит.
    private async Task<string> ClientAsync(CancellationToken ct)=>await authority.CurrentApiClientAsync(ct)
        ??throw new ApiFault(401,"api.identity_invalid","Требуется API access token.");
    // Собственный toolset использует тот же owner authority и конкретную security session.
    public async Task<ApiSelf> ReadSelfAsync(CancellationToken ct)
    {
        if(scope.ApiSessionId is not { } id)throw new ApiFault(401,"api.session_required","Требуется сессия API.");
        await ValidateAsync(id,"user.self.read",ct);
        var user=await authority.CurrentAsync(ct)??throw new UnauthorizedAccessException();
        return new(user.Id,user.Login,UserValueRules.ReadUserData(user).DisplayedName,user.Kind,user.Enabled);
    }
    // Strict metadata не допускает произвольных payload или устаревших форматов security record.
    internal static T Read<T>(string json)=>JsonSerializer.Deserialize<T>(json,IdentityStateStore.Json)??throw new InvalidDataException();
    // Проекция не возвращает grant internals, bearer или refresh credentials.
    private static ApiSessionInfo Info(ApiSessionRow row)=>new(row.Id,row.UserId,row.ClientId,row.ProviderId,row.Epoch,row.ExpiresAt,row.State,
        Read<string[]>(row.CapabilitiesJson),Read<ApiSpaceScope>(row.SpacesJson));
}

// Ограничения security session являются конфигурацией установки, без state конкретного пользователя.
internal sealed class ApiSecuritySettings
{
    public TimeSpan SessionLifetime {get;}
    // Некорректный срок не подменяется бессрочной сессией или неявным clamp.
    public ApiSecuritySettings(IConfiguration configuration)
    {var minutes=configuration.GetValue("Api:SessionLifetimeMinutes",60);if(minutes is <1 or >1440)throw new ArgumentOutOfRangeException("Api:SessionLifetimeMinutes");SessionLifetime=TimeSpan.FromMinutes(minutes);}
}

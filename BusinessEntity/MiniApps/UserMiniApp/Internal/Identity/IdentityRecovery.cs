using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Локальная операторская команда восстанавливает известный профиль, не создавая HTTP обхода входа.
internal sealed class IdentityRecovery(IdentityStateStore store, IdentityRuntime runtime,
    IdentityRecoveryTarget targetValidation, IdentityAdmissionGate admission, BusRequestScope scope)
{
    // Вызывается только composition root до старта host, под эксклюзивным lease данной БД.
    public async Task<long> RunAsync(Guid providerId, int revision, CancellationToken ct)
    {
        if (!await store.ReadAsync(db => db.IdentityInstallation.AnyAsync(ct)))
            throw new InvalidOperationException("Recovery предназначен для уже настроенной установки.");
        await store.LoadAsync(runtime, ct);
        var target = runtime.Get(providerId, revision);
        var administrator = await targetValidation.RequireAdministratorAsync(target, ct);
        using var pause = await admission.PauseAsync(TimeSpan.FromSeconds(30), ct);
        using var permit = scope.EnterActivation(pause.Id);
        scope.MarkSystem();
        var state = await store.ReadAsync(db => db.IdentityInstallation.SingleAsync(ct));
        var sourceId = state.ActiveProviderId; var sourceEpoch = state.Epoch;
        state.ActiveProviderId = providerId; state.ActiveRevision = revision; state.Epoch = checked(sourceEpoch + 1);
        var operationId = Guid.NewGuid();
        var audit = new IdentitySwitchPlanRow { Id = operationId, ActorUserId = administrator.UserId,
            SourceProviderId = sourceId, SourceEpoch = sourceEpoch, TargetProviderId = providerId, TargetRevision = revision,
            State = "OperatorRecovery", Json = "[]", VerifiedAdminId = administrator.UserId, CreatedAt = DateTime.UtcNow };
        await store.WriteAsync(administrator.UserId, [state, audit], operationId, ct);
        runtime.Activate(providerId, revision, state.Epoch);
        return state.Epoch;
    }
}

// Проверяет доступность выбранного профиля и уже доказанную административную привязку.
internal sealed class IdentityRecoveryTarget(IdentityStateStore store, IdentityProfileValidation validation,
    IdentityProvisioners provisioners, UserSecurityAuthority authority)
{
    // Команда не сопоставляет имена и не создаёт новых привилегированных аккаунтов.
    public async Task<ExternalIdentityLinkRow> RequireAdministratorAsync(IdentityProviderProfile target, CancellationToken ct)
    {
        if (!target.Enabled) throw new InvalidOperationException("Целевой профиль отключён.");
        await validation.ValidateAsync(target, ct);
        var links = await store.ReadAsync(db => db.ExternalIdentities.Where(x => x.ProviderId == target.Id && x.Issuer == target.Issuer
            && x.Enabled && !x.RequiresPasswordChange && db.Users.Any(u => u.Id == x.UserId && u.Enabled && u.Kind == "Human")).ToArrayAsync(ct));
        foreach (var link in links)
        {
            if (!await authority.IsGlobalAdminAsync(link.UserId, ct)) continue;
            if (target.ProvisioningKind != "None")
            {
                var adapter = provisioners.Get(target);
                var account = string.IsNullOrEmpty(link.AccountId) ? await adapter.FindAsync(target, link.ProviderLogin, ct)
                    : await adapter.ReadAsync(target, link.AccountId, ct);
                if (account == null || !account.Enabled || account.RequiresPasswordChange || account.Subject != link.Subject) continue;
            }
            return link;
        }
        throw new InvalidOperationException("Нет подтверждённой доступной привязки GlobalAdmin к целевому профилю.");
    }
}

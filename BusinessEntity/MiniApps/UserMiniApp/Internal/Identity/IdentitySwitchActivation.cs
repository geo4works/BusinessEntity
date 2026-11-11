using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Единственная активация связывает drain, SQL/outcome, runtime revision и отзыв сессий.
internal sealed class IdentitySwitchActivation(UserSecurityAuthority authority,IdentityStateStore store,
    IdentityRuntime runtime,IdentitySwitchPreparation preparation,IdentityAdmissionGate admission,BusRequestScope scope)
{
    // Шесть зависимостей относятся к одному протоколу: автор, запись, конфигурация, план, drain и адрес коммита.
    public Task<long> ActivateAsync(Guid id,CancellationToken ct=default) => store.ExclusiveAsync(async () =>
    {
        var actor=await authority.RequireGlobalAdminAsync(ct);
        var plan=await preparation.ReadAsync(id,ct);
        await preparation.CheckCurrentAsync(plan,ct);
        if(plan.State!="Ready"||!plan.VerifiedAdminId.HasValue||plan.VerifiedAt<DateTime.UtcNow.AddMinutes(-15)
            ||!await authority.IsGlobalAdminAsync(plan.VerifiedAdminId.Value,ct))
            throw new InvalidOperationException("Перед переключением подтвердите пробный вход локального GlobalAdmin.");
        await preparation.ValidateActivationTargetAsync(plan,ct);
        using var pause=await admission.PauseAsync(TimeSpan.FromSeconds(30),ct);
        await preparation.CheckCurrentAsync(plan,ct);
        var installation=await store.ReadAsync(db=>db.IdentityInstallation.SingleAsync(ct));
        installation.ActiveProviderId=plan.TargetProviderId;installation.ActiveRevision=plan.TargetRevision;
        installation.Epoch=checked(plan.SourceEpoch+1);plan.State="Active";
        using var permit=scope.EnterActivation(pause.Id);
        try { await store.WriteAsync(actor.Id,[installation,plan],plan.Id,ct); }
        catch
        {
            // Потерянный ответ после commit не откатывает выбор; сверяется сохранённое состояние.
            try
            {
                var actual=await store.ReadAsync(db=>db.IdentityInstallation.SingleAsync(CancellationToken.None));
                var saved=await store.ReadAsync(db=>db.IdentitySwitchPlans.SingleAsync(x=>x.Id==plan.Id,CancellationToken.None));
                if(actual.Epoch!=installation.Epoch||actual.ActiveProviderId!=plan.TargetProviderId
                    ||actual.ActiveRevision!=plan.TargetRevision||saved.State!="Active")throw;
            }
            catch {runtime.Suspend();throw;}

        }
        runtime.Activate(plan.TargetProviderId,plan.TargetRevision,installation.Epoch);
        return installation.Epoch;
    },ct);
}

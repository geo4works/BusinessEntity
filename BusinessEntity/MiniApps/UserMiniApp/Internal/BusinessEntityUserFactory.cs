using System.Security.Claims;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Нормализует локального пользователя и его текущие права без зависимости от конкретного IdP.
internal sealed class BusinessEntityUserFactory(UserSecurityAuthority authority, ICurrentPrincipalSource principal)
{
    // Старая cookie или circuit не получает прежних прав после смены провайдера/локальных назначений.
    public async Task<BusinessEntityUser?> CreateAsync(CancellationToken ct = default)
    {
        var user = await authority.CurrentAsync(ct); if (user == null) return null;
        var current = await principal.GetAsync(ct);
        return new(user.Id.ToString(), user.Login, UserValueRules.ReadUserData(user).Email, true,
            await authority.IsGlobalAdminAsync(user.Id, ct), [],
            current.Claims.Select(c => new BusinessEntityClaim(c.Type,c.Value,c.Issuer,c.OriginalIssuer,c.ValueType)).ToArray());
    }
}

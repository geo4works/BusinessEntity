using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Сессия не кэширует устаревшую identity/роль между действиями длительного circuit.
internal sealed class UserIdentitySession(BusinessEntityUserFactory factory)
{
    // Проверка epoch и локальных полномочий выполняется на каждом обращении.
    public Task<BusinessEntityUser?> GetCurrentUserAsync(CancellationToken ct = default) => factory.CreateAsync(ct);
}

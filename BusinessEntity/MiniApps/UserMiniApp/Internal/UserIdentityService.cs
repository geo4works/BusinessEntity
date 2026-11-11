using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Пользователь уже связан с локальным ID при проверенном входе; login не служит внешним ключом.
internal sealed class UserIdentityService(UserIdentitySession identitySession, IUserMiniAppRepository<UserDto> users)
{
    // Остальные сценарии User получают ту же нормализованную текущую identity.
    public Task<BusinessEntityUser?> GetCurrentUserAsync(CancellationToken ct = default) => identitySession.GetCurrentUserAsync(ct);
    // Локальная материализация выполнена до выдачи cookie/token context, а не по имени на каждом вызове.
    public async Task<UserDto?> EnsureCurrentUserAsync(CancellationToken ct = default)
    {
        var identity = await GetCurrentUserAsync(ct);
        return identity?.IsAuthenticated == true && Guid.TryParse(identity.UserId, out var id) ? await users.GetByIdAsync(id, ct) : null;
    }
    // Системный anonymous не имеет внешнего аккаунта и не участвует в переносе провайдеров.
    public async Task EnsureSystemUsersAsync(CancellationToken ct = default) => _ = await EnsureAnonymousUserAsync(ct);
    // Создаёт один стабильный локальный anonymous; не меняет существующие человеческие записи.
    internal async Task<UserDto> EnsureAnonymousUserAsync(CancellationToken ct)
    {
        var id = Guid.Parse(UserValueRules.SystemAnonymousUserIdText);
        var existing = await users.GetByIdAsync(id, ct); if (existing != null) return existing;
        return await AddIdentityAsync(new UserDto { Id = id, Login = UserValueRules.SystemAnonymousLogin,
            NormalizedLogin = UserValueRules.SystemAnonymousLogin.ToUpperInvariant(), Kind = "LocalSystem",
            Payload = UserValueRules.SerializeUserData(UserValueRules.BuildAnonymousUserData()) }, ct);
    }
    // Уникальность нормализованного локального имени разрешает только гонку той же служебной записи.
    internal async Task<UserDto> AddIdentityAsync(UserDto candidate, CancellationToken ct)
    {
        candidate.NormalizedLogin = candidate.Login.Normalize().ToUpperInvariant();
        try { return await users.AddAsync(candidate, ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            var existing = (await users.GetAllAsync(x => x.NormalizedLogin == candidate.NormalizedLogin, ct)).Single();
            if (existing.Id != candidate.Id && candidate.Kind != "LocalSystem") throw new InvalidOperationException("identity.username_conflict");
            return existing;
        }
    }
}

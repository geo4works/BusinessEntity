using System.Security.Claims;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Проверяет локальный аккаунт и права по свежим SQL-назначениям, а не по имени admin или cookie-флагу.
internal sealed class UserSecurityAuthority(DbContextOptions<UserMiniAppDbContext> options,
    ICurrentPrincipalSource principal, IIdentityProviderRegistry registry, BusinessEntity.Infrastructure.Bus.BusRequestScope scope) : IIdentitySessionValidator
{
    // Устаревшие circuit/HTTP cookie отсекаются при каждом действии владельца.
    public async Task<UserDto?> CurrentAsync(CancellationToken ct = default)
    {
        var current = await principal.GetAsync(ct);
        if (current.Identity?.IsAuthenticated != true) return null;
        var user=await ReadValidatedAsync(current,ct)??throw new UnauthorizedAccessException("Сессия завершена; требуется новый вход.");
        var expires=current.FindFirstValue(IdentityClaimNames.AccessExpiresAt);if(expires!=null){var expiry=DateTime.Parse(expires,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind);if(expiry<=DateTime.UtcNow)throw new UnauthorizedAccessException("API access истёк.");scope.SetApiAccessExpiry(expiry);}
        scope.SetUser(user.Id);scope.SetAuthenticationEpoch(registry.Current.Epoch);return user;
    }
    // Cookie pipeline проверяет переданный principal до заполнения HttpContext.User.
    public async Task<bool> ValidateAsync(ClaimsPrincipal current,CancellationToken ct)=>await ReadValidatedAsync(current,ct)!=null;
    // OAuth client берётся только из проверенного Bearer principal, а не HTTP body.
    public async Task<string?> CurrentApiClientAsync(CancellationToken ct)=>(await principal.GetAsync(ct)).FindFirstValue(IdentityClaimNames.ClientId);
    // Отключённая привязка отзывает существующие сессии даже без изменения самого LocalUser.
    private async Task<UserDto?> ReadValidatedAsync(ClaimsPrincipal current,CancellationToken ct)
    {
        if(current.Identity?.IsAuthenticated!=true
            ||!Guid.TryParse(current.FindFirstValue(IdentityClaimNames.UserId),out var userId)
            ||!Guid.TryParse(current.FindFirstValue(IdentityClaimNames.ProviderId),out var providerId)
            ||!long.TryParse(current.FindFirstValue(IdentityClaimNames.Epoch),out var epoch)
            ||registry.Current.Profile.Id!=providerId||registry.Current.Epoch!=epoch)return null;
        var issuer=current.FindFirstValue(IdentityClaimNames.Issuer);var subject=current.FindFirstValue(IdentityClaimNames.Subject);
        await using var db=new UserMiniAppDbContext(options);
        if(!await db.ExternalIdentities.AnyAsync(x=>x.UserId==userId&&x.Issuer==issuer&&x.Subject==subject&&x.Enabled,ct))return null;
        return await db.Users.SingleOrDefaultAsync(x=>x.Id==userId&&x.Enabled&&x.Kind!="LocalSystem",ct);
    }
    // Вычисляет GlobalAdmin только из локальных ролей и групп.
    public async Task<bool> IsGlobalAdminAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = new UserMiniAppDbContext(options);
        var groupIds = await db.UserGroupMembers.Where(x => x.UserId == userId).Select(x => x.GroupId).ToArrayAsync(ct);
        var roleIds = await db.UserRoleAssignments.Where(x => (x.SpaceId == Guid.Empty || x.Subject == UserRoleAssignmentSubjects.AllSpaces)
            && ((x.AssignmentType == UserRoleAssignmentTypes.UserToRole && x.SubjectId == userId)
                || (x.AssignmentType == UserRoleAssignmentTypes.GroupToRole && groupIds.Contains(x.SubjectId))))
            .Select(x => x.RoleId).ToArrayAsync(ct);
        var roles = await db.UserRoles.Where(x => roleIds.Contains(x.Id)).ToArrayAsync(ct);
        return roles.Any(x => UserValueRules.ParsePermissionCodes(x.Permissions).Contains(UserRolePermissionCodes.GlobalAdmin));
    }
    // Административная операция требует свежего полномочия непосредственно перед выполнением.
    public async Task<UserDto> RequireGlobalAdminAsync(CancellationToken ct = default)
    {
        var user = await CurrentAsync(ct) ?? throw new UnauthorizedAccessException();
        if (!await IsGlobalAdminAsync(user.Id, ct)) throw new UnauthorizedAccessException("Требуются права GlobalAdmin.");
        return user;
    }
}

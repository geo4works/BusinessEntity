using System.Security.Claims;
using System.Text.Json;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Материализует только проверенные внешние identities и сохраняет постоянный LocalUserId.
internal sealed class IdentityDirectory(DbContextOptions<UserMiniAppDbContext> options,IConfiguration? configuration=null)
{
    // Проверенная iss/sub имеет приоритет; совпадение username не разрешает присвоить локальную запись.
    public async Task<UserDto> ResolveAsync(VerifiedExternalIdentity identity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identity.Subject) || string.IsNullOrWhiteSpace(identity.Issuer)
            || string.IsNullOrWhiteSpace(identity.Login)) throw new InvalidDataException("Неполная внешняя identity.");
        await using var db = new UserMiniAppDbContext(options);
        var link = await db.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == identity.Issuer && x.Subject == identity.Subject, ct);
        if (link != null)
        {
            if (!link.Enabled) throw new UnauthorizedAccessException("Привязка identity отключена.");
            return await GetEnabledAsync(db, link.UserId, ct);
        }
        var normalized = identity.Login.Normalize().ToUpperInvariant();
        if (await db.Users.AnyAsync(x => x.NormalizedLogin == normalized, ct))
        {
            // Победитель конкурентного первого входа мог закоммититься между двумя чтениями.
            var concurrent=await db.ExternalIdentities.SingleOrDefaultAsync(x=>x.Issuer==identity.Issuer&&x.Subject==identity.Subject,ct);
            if(concurrent?.Enabled==true)return await GetEnabledAsync(db,concurrent.UserId,ct);
            throw new InvalidOperationException("identity.username_conflict: локальный логин уже связан с другой identity.");
        }
        var user = new UserDto { Id = Guid.NewGuid(), Login = identity.Login, NormalizedLogin = normalized,
            Kind = identity.AccountKind, Payload = UserValueRules.SerializeUserData(new UserData
                { DisplayedName = identity.DisplayName, Email = identity.Email ?? string.Empty }) };
        db.Users.Add(user);
        db.ExternalIdentities.Add(new() { Id = Guid.NewGuid(), UserId = user.Id, ProviderId = identity.ProviderId,
            Issuer = identity.Issuer, Subject = identity.Subject, ProviderLogin = identity.Login });
        try { await db.SaveChangesAsync(ct); return user; }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            // Гонка одного первого входа возвращает победителя только по проверенному iss/sub.
            db.ChangeTracker.Clear();
            var winner = await db.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == identity.Issuer && x.Subject == identity.Subject, ct);
            if (winner == null || !winner.Enabled) throw new InvalidOperationException("identity.username_conflict: логин уже занят.");
            return await GetEnabledAsync(db, winner.UserId, ct);
        }
    }
    // Неактивный аккаунт не получает сессию даже с действительным внешним токеном.
    private static async Task<UserDto> GetEnabledAsync(UserMiniAppDbContext db, Guid id, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(x => x.Id == id, ct);
        return user.Enabled && user.Kind != "LocalSystem" ? user : throw new UnauthorizedAccessException("Локальная учётная запись отключена.");
    }
    // Локальное назначение первому администратору выполняется один раз по pinned issuer/subject установки.
    public async Task BootstrapAdministratorAsync(UserDto user, VerifiedExternalIdentity identity, CancellationToken ct)
    {
        await using var db = new UserMiniAppDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var installation = await db.IdentityInstallation.SingleAsync(ct);
        if (installation.BootstrapCompleted) return;
        var expectedIssuer = configuration?["Identity:Bootstrap:AdministratorIssuer"];
        var expectedSubject = configuration?["Identity:Bootstrap:AdministratorSubject"];
        if (string.IsNullOrWhiteSpace(expectedIssuer) || string.IsNullOrWhiteSpace(expectedSubject)
            || identity.Issuer != expectedIssuer || identity.Subject != expectedSubject) return;
        var role = await db.UserRoles.SingleAsync(x => x.Name == UserValueRules.SystemAdminRoleName, ct);
        db.UserRoleAssignments.Add(new() { Id = Guid.NewGuid(), SpaceId = Guid.Empty, SubjectId = user.Id,
            Subject = UserRoleAssignmentSubjects.AllSpaces, AssignmentType = UserRoleAssignmentTypes.UserToRole, RoleId = role.Id });
        installation.BootstrapCompleted = true;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    // Собирает cookie только из локальных ID и проверенных протоколом связей; сырой JWT не переносится.
    public static ClaimsPrincipal CreatePrincipal(UserDto user, VerifiedExternalIdentity identity, long epoch)
        => new(new ClaimsIdentity(new[] {
            new Claim(IdentityClaimNames.UserId, user.Id.ToString()), new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(IdentityClaimNames.ProviderId, identity.ProviderId.ToString()), new Claim(IdentityClaimNames.Epoch, epoch.ToString()),
            new Claim(IdentityClaimNames.Issuer, identity.Issuer), new Claim(IdentityClaimNames.Subject, identity.Subject),
            new Claim(IdentityClaimNames.Kind, user.Kind), new Claim(ClaimTypes.Name, user.Login)
        }, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme));
}

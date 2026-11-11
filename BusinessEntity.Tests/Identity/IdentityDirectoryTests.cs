using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using BusinessEntity.Tests.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Реальная БД проверяет уникальность идентичности и отказ автоматического слияния имён.
public sealed class IdentityDirectoryTests
{
    // Один внешний субъект при конкурентном первом входе материализуется ровно один раз.
    [PostgresFact]
    public async Task ParallelVerifiedIdentityResolvesToOneLocalUser()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var options = await UserOwnedTransactionTests.InitializeUserAsync(fixture);
        var profile = await CreateProfileAsync(options);
        var identity = new VerifiedExternalIdentity(profile, "https://issuer.test", "subject-1", "reader", "Reader", null);
        var users = await Task.WhenAll(Enumerable.Range(0,8).Select(_ => new IdentityDirectory(options).ResolveAsync(identity, default)));
        Assert.Single(users.Select(x => x.Id).Distinct());
        await using var db = new UserMiniAppDbContext(options); Assert.Single(await db.ExternalIdentities.ToArrayAsync());
    }
    // Одинаковое имя у другого issuer не даёт захватить существующее локальное авторство или роли.
    [PostgresFact]
    public async Task SameLoginFromAnotherIssuerIsExplicitConflict()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var options = await UserOwnedTransactionTests.InitializeUserAsync(fixture);
        var profile = await CreateProfileAsync(options); var directory = new IdentityDirectory(options);
        var first = await directory.ResolveAsync(new(profile,"https://one.test","alice","alice","Alice",null),default);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => directory.ResolveAsync(new(profile,"https://two.test","alice","alice","Alice",null),default));
        Assert.Contains("identity.username_conflict",error.Message);
        Assert.Equal(first.Id,(await directory.ResolveAsync(new(profile,"https://one.test","alice","alice","Alice",null),default)).Id);
    }
    // Локальное отключение блокирует даже корректную и уже связанную внешнюю identity.
    [PostgresFact]
    public async Task DisabledUserCannotObtainNewSession()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var options = await UserOwnedTransactionTests.InitializeUserAsync(fixture);
        var profile = await CreateProfileAsync(options); var directory = new IdentityDirectory(options);
        var identity = new VerifiedExternalIdentity(profile,"https://issuer.test","subject","admin","Admin",null);
        var user = await directory.ResolveAsync(identity,default);
        await using (var db = new UserMiniAppDbContext(options))
        { await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enabled,false)); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => directory.ResolveAsync(identity,default));
    }
    // Минимальный профиль удовлетворяет FK; реальный discovery проверяется отдельными интеграционными сценариями.
    private static async Task<Guid> CreateProfileAsync(DbContextOptions<UserMiniAppDbContext> options)
    {
        await using var db = new UserMiniAppDbContext(options); var id = Guid.NewGuid();
        db.IdentityProfiles.Add(new() { Id=id,Revision=1,Json="{}" }); await db.SaveChangesAsync(); return id;
    }
}

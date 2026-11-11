using BusinessEntity.MiniApps.UserMiniApp.Internal;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.Tests.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Реальная PostgreSQL блокировка исключает recovery во время работы другого процесса приложения.
public sealed class IdentityHostLeaseTests
{
    // После завершения владельца новый процесс может безопасно получить ту же установку.
    [PostgresFact]
    public async Task RecoveryRequiresExclusiveDatabaseOwnership()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DockerConnection"] = db.Database.GetConnectionString() }).Build();
        await using var first = new IdentityHostLease(configuration);
        await using var second = new IdentityHostLease(configuration);
        await first.AcquireAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.AcquireAsync(default));
        await first.DisposeAsync();
        await second.AcquireAsync(default);
    }
}

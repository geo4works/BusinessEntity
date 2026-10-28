using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Repositories.EfPostgres;
using Xunit;

namespace BusinessEntity.Tests.Storage;

// Проверяет создание одной identity несколькими параллельными server scopes на реальном Postgres.
public sealed class UserIdentityConcurrencyTests
{
    // Все проигравшие insert получают ту же строку без дубликатов и ошибки первого входа.
    [PostgresFact]
    public async Task ParallelIdentityCreationReturnsOneUser()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var options = await UserOwnedTransactionTests.InitializeUserAsync(fixture);
        var externalId = Guid.NewGuid().ToString();
        var records = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            new UserIdentityService(null!, new UserDtoEfRepository(options)).AddIdentityAsync(new UserDto
            {
                Id = Guid.NewGuid(), ExternalId = externalId, Payload = "{}",
                DateCreated = DateTime.UtcNow, DateLastModified = DateTime.UtcNow
            }, default)));
        Assert.Single(records.Select(x => x.Id).Distinct());
        Assert.Single(await new UserDtoEfRepository(options).GetAllAsync(x => x.ExternalId == externalId));
    }
}

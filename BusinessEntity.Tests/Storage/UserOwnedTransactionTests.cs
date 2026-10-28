using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessEntity.Tests.Storage;

// Служебное исключение User имеет те же атомарность и durable outcome, что и бизнес-граф.
public sealed class UserOwnedTransactionTests
{
    // Откат после подготовки строк User не оставляет ни строку, ни исход операции.
    [PostgresFact]
    public async Task UserWriteAndOutcomeRollBackTogether()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var options = await InitializeUserAsync(fixture);
        var id = Guid.NewGuid(); var batch = RoleBatch(id); fixture.Faults.ThrowAt = StorageFaultPoint.BeforeCommit;
        await Assert.ThrowsAsync<IOException>(() => fixture.Writer.SaveAsync(batch, SystemActor()));
        await using var users = new UserMiniAppDbContext(options); await using var graph = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.False(await users.Set<UserRoleDto>().AnyAsync(x => x.Id == id)); Assert.False(await graph.OperationOutcomes.AnyAsync());
    }
    // Потерянный ответ после commit читается новым писателем без повторного применения User DTO.
    [PostgresFact]
    public async Task UserOutcomeSurvivesWriterRestart()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var options = await InitializeUserAsync(fixture);
        var id = Guid.NewGuid(); var batch = RoleBatch(id); var actor = SystemActor(); fixture.Faults.ThrowAt = StorageFaultPoint.AfterCommit;
        await Assert.ThrowsAsync<IOException>(() => fixture.Writer.SaveAsync(batch, actor)); fixture.Faults.ThrowAt = null;
        var writer = fixture.CreateWriter(); var outcome = await writer.GetOutcomeAsync(batch.OperationId, actor);
        Assert.NotNull(outcome); Assert.Equal(JsonSerializer.Serialize(outcome.Result), JsonSerializer.Serialize(await writer.SaveAsync(batch, actor)));
        await using var users = new UserMiniAppDbContext(options); Assert.Equal(1, await users.Set<UserRoleDto>().CountAsync(x => x.Id == id));
        var changed = batch with { OwnedServiceChanges = RoleBatch(id, "подменено").OwnedServiceChanges };
        var error = await Assert.ThrowsAsync<BusRequestFailedException>(() => writer.SaveAsync(changed, actor)); Assert.Equal("operation.id_conflict", error.Error.Code);
    }
    // Обычный автор не может изменить роль через пакет служебного исключения.
    [PostgresFact]
    public async Task UnprivilegedActorCannotWriteRoles()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); await InitializeUserAsync(fixture);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Writer.SaveAsync(RoleBatch(Guid.NewGuid()), new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
    }
    // Создаёт только новую служебную схему в одноразовой БД теста.
    internal static async Task<DbContextOptions<UserMiniAppDbContext>> InitializeUserAsync(PostgresTestDatabase fixture)
    {
        await using var graph = new KmsBusinessEntityDbContext(fixture.Options);
        var options = new DbContextOptionsBuilder<UserMiniAppDbContext>().UseNpgsql(graph.Database.GetConnectionString()).Options;
        await using var users = new UserMiniAppDbContext(options); await UserMiniAppStorageSchema.InitializeAsync(users, true, default); return options;
    }
    // Пакет содержит только собственный DTO User, конкретных классов в DataProvider нет.
    private static EntitySaveBatch RoleBatch(Guid id, string name = "роль") => new(Guid.NewGuid(), "UserMiniApp", Guid.NewGuid())
    { OwnedServiceChanges = [new("UserMiniApp", "UserFormsV1", JsonSerializer.Serialize(new[] { new UserTableMutation(nameof(UserRoleDto), id,
        JsonSerializer.Serialize(new UserRoleDto { Id = id, Name = name, Permissions = "100", DateCreated = DateTime.UtcNow, DateLastModified = DateTime.UtcNow }, GraphArchiveJson.CreateOptions())) }, GraphArchiveJson.CreateOptions()))] };
    // Системная операция не зависит от внешнего Authentik тестовой среды.
    private static BusRequestContext SystemActor() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), IsSystem: true);
}

using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Repositories.EfPostgres;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessEntity.Tests.Storage;

// Проверяет собственный рабочий набор User поверх реальных репозиториев и транзакции DataProvider.
public sealed class UserFormsWorkingSetTests
{
    // Две формы сохраняются одним SQL outcome; правка второй формы во время Save остаётся dirty.
    [PostgresFact]
    public async Task SavesTwoRoleFormsAndPreservesLaterInput()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var options = await UserOwnedTransactionTests.InitializeUserAsync(fixture);
        var repository = new UserRoleDtoEfRepository(options);
        var first = await repository.AddAsync(new UserRoleDto { Id = Guid.NewGuid(), Name = "first", Permissions = "100", DateCreated = DateTime.UtcNow, DateLastModified = DateTime.UtcNow });
        var second = await repository.AddAsync(new UserRoleDto { Id = Guid.NewGuid(), Name = "second", Permissions = "100", DateCreated = DateTime.UtcNow, DateLastModified = DateTime.UtcNow });
        var roles = new UserRoleCatalogService(repository, new UserRoleAssignmentDtoEfRepository(options));
        var scope = new BusRequestScope(); scope.MarkSystem();
        var transport = new GatedTransport(fixture, scope);
        var context = new MiniAppSaveContext("UserMiniApp", null, scope, transport);
        var work = context.GetOrCreateParticipant(() => new UserFormsWorkingSet(context, null!, roles, null!));
        var firstModel = work.Open(UserFormKind.Role, first.Id, new UserRoleSaveRequest { Name = first.Name, ViewPublished = true });
        var secondModel = work.Open(UserFormKind.Role, second.Id, new UserRoleSaveRequest { Name = second.Name, ViewPublished = true });
        firstModel.Name = "sent first"; work.Changed(UserFormKind.Role, first.Id);
        secondModel.Name = "sent second"; work.Changed(UserFormKind.Role, second.Id);
        var saving = context.SaveAsync(Guid.NewGuid()); await transport.Started.Task;
        secondModel.Name = "later second"; work.Changed(UserFormKind.Role, second.Id); transport.Release.TrySetResult();
        Assert.True((await saving).Success); Assert.True(context.IsDirty);
        Assert.Equal("sent first", (await repository.GetByIdAsync(first.Id))!.Name);
        Assert.Equal("sent second", (await repository.GetByIdAsync(second.Id))!.Name);
        Assert.Equal("later second", secondModel.Name);
        Assert.Equal("sent second", work.Result<UserRoleRecord>(UserFormKind.Role, second.Id).Name);
        await context.DiscardAsync(); Assert.False(context.IsDirty);
        Assert.True((await context.SaveAsync(Guid.NewGuid())).Success); Assert.Equal(1, transport.Calls);
    }
    // Пропускает реальную транзакцию после возможности отправить следующее событие UI.
    private sealed class GatedTransport(PostgresTestDatabase fixture, BusRequestScope scope) : ISaveBatchTransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        // Передаёт один frozen пакет в реальный writer после контрольной паузы.
        public async Task<SaveBatchResult> SaveAsync(EntitySaveBatch batch, CancellationToken ct)
        { Calls++; Started.TrySetResult(); await Release.Task; return await fixture.Writer.SaveAsync(batch, scope.Snapshot(), ct); }
        // Проверяет исход в той же одноразовой БД теста.
        public Task<StoredOperationOutcome?> GetOutcomeAsync(Guid operationId, CancellationToken ct) => fixture.Writer.GetOutcomeAsync(operationId, scope.Snapshot(), ct);
    }
}

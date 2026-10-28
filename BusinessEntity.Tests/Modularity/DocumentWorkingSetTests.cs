using System.Reflection;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Types;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.DocumentsMiniApp.Internal;
using Xunit;

namespace BusinessEntity.Tests.Modularity;

// Проверяет предметный рабочий набор поверх общего сохранения, включая несколько сущностей и reentrant UI.
public sealed class DocumentWorkingSetTests
{
    // Save сохраняет обе сущности одним снимком; правка во время ожидания остаётся dirty.
    [Fact]
    public async Task SavesWholeWorksetWithoutClearingLaterChanges()
    {
        var transport = new DelayedTransport(); var scope = new BusRequestScope(); scope.MarkSystem();
        var context = new MiniAppSaveContext("DocumentsMiniApp", Guid.NewGuid(), scope, transport);
        var descriptor = new EntityTypeDescriptor<Document>(DocumentType.Id, "DocumentsMiniApp", "Document", true, true, "NewDocument", (id, name, text) => new(new Document()));
        var work = context.GetOrCreateParticipant(() => new DocumentsWorkingSet(context, new EntityTypeRegistry([descriptor]), DispatchProxy.Create<IUserConnector, Permissions>()));
        var first = new Document { Id = Guid.NewGuid(), Version = 1, Name = "Первый", Text = "a" };
        var second = new Document { Id = Guid.NewGuid(), Version = 1, Name = "Второй", Text = "b" };
        work.Open(first); work.Open(second); work.Update(first.Id, "Первый", "отправлено 1"); work.Update(second.Id, "Второй", "отправлено 2");
        var saving = context.SaveAsync(Guid.NewGuid()); await transport.Started.Task;
        work.Update(second.Id, "Второй", "новая правка во время Save"); transport.Release.TrySetResult();
        Assert.True((await saving).Success);
        Assert.Equal(2, transport.Sent!.Payloads.Count);
        Assert.Equal("отправлено 2", ((Document)transport.Sent.Payloads.Single(x => x.EntityId == second.Id).Data).Text);
        Assert.Equal("новая правка во время Save", work.Get(second.Id).Text); Assert.True(context.IsDirty);
        await context.DiscardAsync(); Assert.Equal("отправлено 2", work.Get(second.Id).Text); Assert.False(context.IsDirty);
        Assert.Same(work, context.GetOrCreateParticipant<DocumentsWorkingSet>(() => throw new InvalidOperationException()));
    }
    // Открытый без правок второй документ не откатывает чужое параллельное сохранение.
    [Fact]
    public void ExcludesUnmodifiedLoadedDocuments()
    {
        var scope = new BusRequestScope(); var context = new MiniAppSaveContext("DocumentsMiniApp", null, scope, new DelayedTransport());
        var descriptor = new EntityTypeDescriptor<Document>(DocumentType.Id, "DocumentsMiniApp", "Document", true, true, "NewDocument", (id, name, text) => new(new Document()));
        var work = new DocumentsWorkingSet(context, new EntityTypeRegistry([descriptor]), DispatchProxy.Create<IUserConnector, Permissions>());
        var first = new Document { Id = Guid.NewGuid(), Name = "first", Text = "a", Version = 1 };
        var second = new Document { Id = Guid.NewGuid(), Name = "second", Text = "b", Version = 1 };
        work.Open(first); work.Open(second); work.Update(first.Id, first.Name, "changed");
        Assert.Equal(first.Id, work.Capture(Guid.NewGuid(), context.EditSessionId, context.Revision).Payloads.Single().EntityId);
    }

    // Транспорт позволяет вмешаться в UI после фиксации пакета до подтверждения.
    private sealed class DelayedTransport : ISaveBatchTransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public EntitySaveBatch? Sent { get; private set; }
        // Возвращает один подтверждённый результат для всего набора.
        public async Task<SaveBatchResult> SaveAsync(EntitySaveBatch batch, CancellationToken ct)
        { Sent = batch; Started.TrySetResult(); await Release.Task; return new(batch.OperationId, DateTime.UtcNow, batch.Payloads.Select(x => new SavedEntityVersion(x.EntityId, 2, true)).ToArray(), [], [], [], []); }
        // Неопределённого исхода в этом сценарии нет.
        public Task<StoredOperationOutcome?> GetOutcomeAsync(Guid id, CancellationToken ct) => Task.FromResult<StoredOperationOutcome?>(null);
    }
    // Проверка бизнес-набора получает разрешение на правку; остальные запросы тест не допускает.
    public class Permissions : DispatchProxy
    {
        // Только действующий контракт проверки прав используется при Prepare рабочего набора.
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IUserConnector.GetEntityContentAccessAsync)
            ? Task.FromResult(new UserContentAccessDecision { CanEditDraft = true }) : throw new NotSupportedException();
    }
}

using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Xunit;

namespace BusinessEntity.Tests.Modularity;

// Проверяет решения навигационного guard с реальным менеджером и управляемой длительностью Save.
public sealed class NavigationSaveGuardTests
{
    // Cancel не вызывает ни Save, ни Discard, а Discard отбрасывает весь набор владельца.
    [Fact]
    public async Task CancelPreservesAndDiscardDropsWholeWorkingSet()
    {
        await using var fixture = Fixture.Create(); var module = await fixture.Manager.OpenAsync("owner:page", Guid.NewGuid());
        var participant = new Participant(); module.SaveContext!.RegisterParticipant(participant); module.SaveContext.MarkChanged();
        Assert.False((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Cancel)).CanLeave);
        Assert.True(module.IsDirty); Assert.Equal(0, fixture.Transport.Calls); Assert.Equal(0, participant.Discards);
        Assert.True((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Discard)).CanLeave);
        Assert.False(module.IsDirty); Assert.Equal(1, participant.Discards); Assert.Equal(0, fixture.Transport.Calls);
    }
    // Успешный Save разрешает уход только после подтверждения всех контекстов окна.
    [Fact]
    public async Task SaveUsesOwnersForEveryDirtyContext()
    {
        await using var fixture = Fixture.Create();
        var first = await fixture.Manager.OpenAsync("owner:page", Guid.NewGuid()); var second = await fixture.Manager.OpenAsync("owner:page", Guid.NewGuid());
        foreach (var module in new[] { first, second }) { module.SaveContext!.RegisterParticipant(new Participant()); module.SaveContext.MarkChanged(); }
        Assert.True((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Save)).CanLeave);
        Assert.Equal(2, fixture.Transport.Calls); Assert.Empty(fixture.Manager.DirtyContexts);
    }
    // Поздняя правка во время Save остаётся dirty и останавливает переход.
    [Fact]
    public async Task InputDuringSavePreventsNavigation()
    {
        await using var fixture = Fixture.Create(); var module = await fixture.Manager.OpenAsync("owner:page", Guid.NewGuid());
        module.SaveContext!.RegisterParticipant(new Participant()); module.SaveContext.MarkChanged(); fixture.Transport.Block = true;
        var leaving = fixture.Guard.ResolveAsync(NavigationSaveDecision.Save); await fixture.Transport.Started.Task;
        module.SaveContext.MarkChanged(); fixture.Transport.Release.TrySetResult();
        Assert.False((await leaving).CanLeave); Assert.True(module.IsDirty);
    }
    // Неустановленный исход нельзя скрыть Discard; повтор использует тот же пакет.
    [Fact]
    public async Task UnknownOutcomeBlocksDiscardAndRetryKeepsOperationId()
    {
        await using var fixture = Fixture.Create(); var module = await fixture.Manager.OpenAsync("owner:page", Guid.NewGuid());
        var participant = new Participant(); module.SaveContext!.RegisterParticipant(participant); module.SaveContext.MarkChanged(); fixture.Transport.Failure = new TimeoutException();
        Assert.False((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Save)).CanLeave);
        var operation = module.SaveContext.OperationId; Assert.NotNull(operation);
        Assert.False((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Discard)).CanLeave); Assert.Equal(0, participant.Discards);
        fixture.Transport.Failure = null; Assert.True((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Save)).CanLeave);
        Assert.Equal(operation, fixture.Transport.LastBatch!.OperationId); Assert.Equal(1, participant.Captures);
    }
    // Отозванное право и отсутствие владельца черновика не разрешают переход и не пишут данные.
    [Fact]
    public async Task SaveRechecksPermissionAndMissingParticipantRemainsDirty()
    {
        await using var fixture = Fixture.Create(); var module = await fixture.Manager.OpenAsync("owner:page", Guid.NewGuid());
        module.SaveContext!.MarkChanged();
        Assert.False((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Save)).CanLeave); Assert.True(module.IsDirty);
        module.SaveContext.RegisterParticipant(new Participant()); fixture.Access.Allowed = false;
        Assert.False((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Save)).CanLeave); Assert.True(module.IsDirty); Assert.Equal(0, fixture.Transport.Calls);
    }
    // Подтверждённое отсутствие удалённой сущности не оставляет вечную блокировку Discard.
    [Fact]
    public async Task DefiniteMissingRecordKeepsDraftButAllowsDiscard()
    {
        await using var fixture = Fixture.Create(); var module = await fixture.Manager.OpenAsync("owner:page", Guid.NewGuid());
        module.SaveContext!.RegisterParticipant(new Participant()); module.SaveContext.MarkChanged();
        fixture.Transport.Failure = new BusRequestFailedException(new("record.not_found", "Объект удалён."));
        Assert.False((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Save)).CanLeave); Assert.True(module.IsDirty);
        Assert.Null(module.SaveContext.OperationId); Assert.True((await fixture.Guard.ResolveAsync(NavigationSaveDecision.Discard)).CanLeave);
    }
    // Новые события UI во время асинхронного Discard также не очищаются чужим подтверждением.
    [Fact]
    public async Task NewInputDuringDiscardRemainsDirty()
    {
        await using var fixture = Fixture.Create(); var module = await fixture.Manager.OpenAsync("owner:page", Guid.NewGuid());
        var participant = new Participant { BlockDiscard = true }; module.SaveContext!.RegisterParticipant(participant); module.SaveContext.MarkChanged();
        var leaving = fixture.Guard.ResolveAsync(NavigationSaveDecision.Discard); await participant.DiscardStarted.Task;
        module.SaveContext.MarkChanged(); participant.DiscardRelease.TrySetResult();
        Assert.False((await leaving).CanLeave); Assert.True(module.IsDirty);
    }
    // Навигация не обгоняет ввод, остановившийся на асинхронной проверке до MarkChanged.
    [Fact]
    public async Task NavigationWaitsForPendingInputBeforeInspectingDirty()
    {
        await using var fixture = Fixture.Create(); var module = await fixture.Manager.OpenAsync("owner:page", Guid.NewGuid());
        module.SaveContext!.RegisterParticipant(new Participant());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task EditAsync()
        {
            using var pending = fixture.Manager.TrackEditEvent(); started.TrySetResult(); await release.Task;
            // Вложенный callback не ожидает сам себя.
            await fixture.Manager.WaitForPendingEditsAsync(); module.SaveContext.MarkChanged();
        }
        var editing = EditAsync(); await started.Task; Assert.True(fixture.Manager.HasPendingEdits);
        var leaving = fixture.Guard.ResolveAsync(NavigationSaveDecision.Save); Assert.False(leaving.IsCompleted);
        release.TrySetResult(); await editing; Assert.True((await leaving).CanLeave);
        Assert.Equal(1, fixture.Transport.Calls); Assert.False(fixture.Manager.HasPendingEdits);
    }
    // Изолирует менеджер окна, транспорт и доступ без SQL/Authentik/браузера.
    private sealed class Fixture : IAsyncDisposable
    {
        public WindowModularityManager Manager { get; }
        public WindowSaveGuard Guard { get; }
        public Transport Transport { get; } = new();
        public Access Access { get; } = new();
        private readonly ServiceProvider _provider = new ServiceCollection().BuildServiceProvider();
        private readonly ModuleCatalog _catalog;
        private Fixture()
        {
            var bus = new MessageBus(); _catalog = new(bus, _provider.GetRequiredService<IServiceScopeFactory>(), [], [], NullLogger<ModuleCatalog>.Instance);
            _catalog.Accept(new(Guid.NewGuid(), "owner", [new("owner", "page", "Editor", null, ModulePlacement.Page,
                ModuleInstancePolicy.PerContext, ComponentKey: "test", RequiredPermission: "Edit", HasSaveContext: true)]));
            Manager = new(_catalog, new BusRequestScope(), Transport, Access, bus); Guard = new(Manager);
        }
        // Создаёт новый независимый browser window runtime теста.
        public static Fixture Create() => new();
        // Закрывает все runtime-адреса и scoped ресурсы теста.
        public async ValueTask DisposeAsync() { await Manager.DisposeAsync(); await _catalog.DisposeAsync(); await _provider.DisposeAsync(); }
    }
    // Отзыв права после открытия моделирует обязательную повторную проверку сервером.
    private sealed class Access : IModuleAccessPolicy
    {
        public bool Allowed = true;
        public Task<bool> CanOpenAsync(ModuleDefinition definition, Guid? contextId, CancellationToken ct) => Task.FromResult(Allowed);
    }
    // Участник представляет один полный рабочий набор с наблюдаемыми вызовами.
    private sealed class Participant : IMiniAppSaveParticipant
    {
        public int Captures, Discards; public bool BlockDiscard;
        public TaskCompletionSource DiscardStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), DiscardRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public EntitySaveBatch Capture(Guid operationId, Guid editSessionId, long revision) { Captures++; return new(operationId, "owner", editSessionId); }
        public Task AcknowledgeAsync(EntitySaveBatch sent, SaveBatchResult result, long revision, CancellationToken ct) => Task.CompletedTask;
        public async Task DiscardAsync(CancellationToken ct) { Discards++; DiscardStarted.TrySetResult(); if (BlockDiscard) await DiscardRelease.Task; }
    }
    // Вносит управляемый отказ или задержку только между capture и подтверждением.
    private sealed class Transport : ISaveBatchTransport
    {
        public int Calls; public bool Block; public Exception? Failure; public EntitySaveBatch? LastBatch;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SaveBatchResult> SaveAsync(EntitySaveBatch batch, CancellationToken ct)
        { Calls++; LastBatch = batch; Started.TrySetResult(); if (Block) await Release.Task; if (Failure != null) throw Failure; return new(batch.OperationId, DateTime.UtcNow, [], [], [], [], []); }
        public Task<StoredOperationOutcome?> GetOutcomeAsync(Guid operationId, CancellationToken ct) => Task.FromResult<StoredOperationOutcome?>(null);
    }
}

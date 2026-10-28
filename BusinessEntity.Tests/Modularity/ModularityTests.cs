using System.Reactive.Linq;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Xunit;

namespace BusinessEntity.Tests.Modularity;

// Проверяет жизненный цикл, границы адресов и общий черновик независимо от Razor renderer.
public sealed class ModularityTests
{
    // Поздний bus-ответ расширяет каталог; неизвестные/дублированные UI-ключи дают отказ.
    [Fact]
    public async Task CatalogAcceptsLateDefinitionsAndValidatesUiRegistry()
    {
        var bus = new MessageBus(); await using var provider = new ServiceCollection().BuildServiceProvider();
        using var host = new MiniAppHostService(bus, "owner", [Definition("page")]);
        await using var catalog = new ModuleCatalog(bus, provider.GetRequiredService<IServiceScopeFactory>(), [host], [], NullLogger<ModuleCatalog>.Instance);
        await catalog.CollectAsync(); Assert.Single(catalog.Definitions);
        var changed = 0; catalog.DefinitionsChanged += () => changed++;
        bus.SendMessage(new HereAreModuleDefinitions(Guid.NewGuid(), "late", [Definition("late-page") with { MiniAppId = "late" }]));
        Assert.Equal(2, catalog.Definitions.Count); Assert.Equal(1, changed);
        Assert.Throws<InvalidOperationException>(() => catalog.Accept(new(Guid.NewGuid(), "owner", [Definition("page") with { Title = "different" }])));
        Assert.Throws<InvalidOperationException>(() => catalog.Accept(new(Guid.NewGuid(), "owner", [Definition("editor") with { InstancePolicy = ModuleInstancePolicy.AppSingleton }])));
        Assert.Throws<ArgumentException>(() => new ModuleUiFactory([new("same", typeof(TestComponent)), new("same", typeof(TestComponent))]));
        var ui = new ModuleUiFactory([new("ok", typeof(TestComponent))]); Assert.Equal(typeof(TestComponent), ui.Resolve("ok"));
        Assert.Throws<KeyNotFoundException>(() => ui.Resolve("missing"));
    }
    // Singleton runtime создаётся каталогом один раз; повтор startup не повторяет side effects.
    [Fact]
    public async Task CatalogStartsAndStopsHeadlessRuntimesInDependencyOrder()
    {
        var bus = new MessageBus(); var events = new List<string>();
        var services = new ServiceCollection(); services.AddBusinessEntityBus(); await using var provider = services.BuildServiceProvider();
        var definitions = new[] { Headless("second", 20), Headless("first", 10) };
        using var host = new MiniAppHostService(bus, "owner", definitions);
        await using var catalog = new ModuleCatalog(bus, provider.GetRequiredService<IServiceScopeFactory>(), [host],
            definitions.Select(d => new ModuleRuntimeRegistration(d.Key, _ => new TestRuntime(d.ModuleDefinitionId, events))), NullLogger<ModuleCatalog>.Instance);
        await catalog.StartAsync(default); await catalog.StartAsync(default);
        Assert.Equal(new[] { "start:first", "start:second" }, events);
        await catalog.StopAsync(default); Assert.Equal(new[] { "start:first", "start:second", "stop:second", "stop:first" }, events);
    }
    // Два компонента разделяют контекст владельца, другое окно имеет собственный; закрытые адреса не исполняются.
    [Fact]
    public async Task PoliciesLifecycleAndSharedDraftAreWindowScoped()
    {
        var bus = new MessageBus(); var events = new List<ModuleLifecycleChanged>(); using var sub = bus.Listen<ModuleLifecycleChanged>().Subscribe(events.Add);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var catalog = new ModuleCatalog(bus, provider.GetRequiredService<IServiceScopeFactory>(), [], [], NullLogger<ModuleCatalog>.Instance);
        catalog.Accept(new(Guid.NewGuid(), "owner", [Definition("page"), Definition("helper"),
            Definition("window") with { InstancePolicy = ModuleInstancePolicy.WindowSingleton, HasSaveContext = false },
            Definition("multi") with { InstancePolicy = ModuleInstancePolicy.MultiInstance }]));
        var transport = new TestTransport(); var firstScope = new BusRequestScope(); firstScope.SetUser(Guid.NewGuid());
        var secondScope = new BusRequestScope(); secondScope.SetUser(firstScope.UserId);
        await using var first = new WindowModularityManager(catalog, firstScope, transport, new AllowAccess(), bus);
        await using var second = new WindowModularityManager(catalog, secondScope, transport, new AllowAccess(), bus);
        var context = Guid.NewGuid(); var page = await first.OpenAsync("owner:page", context);
        Assert.Same(page, await first.OpenAsync("owner:page", context));
        var helper = await first.OpenAsync("owner:helper", context, page.EditSessionId);
        var other = await second.OpenAsync("owner:page", context);
        Assert.Same(page.SaveContext, helper.SaveContext); Assert.NotEqual(page.EditSessionId, other.EditSessionId);
        Assert.Same(await first.OpenAsync("owner:window", context), await first.OpenAsync("owner:window", Guid.NewGuid()));
        Assert.NotEqual((await first.OpenAsync("owner:multi", context)).ModuleId, (await first.OpenAsync("owner:multi", context)).ModuleId);
        page.SaveContext!.MarkChanged(); Assert.True(helper.IsDirty); Assert.False(other.IsDirty);
        await first.CloseAsync(helper.ModuleId); Assert.True(page.IsDirty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.CloseAsync(page.ModuleId));
        await first.ReleaseViewAsync(page.ModuleId); Assert.Throws<KeyNotFoundException>(() => first.GetLive(page.ModuleId));
        Assert.Same(page, await first.OpenAsync("owner:page", context)); Assert.True(page.IsDirty);
        await first.DisposeAsync(); Assert.Throws<ObjectDisposedException>(() => first.GetLive(page.ModuleId));
        Assert.Contains(events, x => x.ModuleId == page.ModuleId && x.Lifecycle == ModuleLifecycle.Closed);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => second.OpenAsync("owner:helper", context, page.EditSessionId));
    }
    // Источник команды не протекает между async-ветками общего circuit scope.
    [Fact]
    public async Task SourceAddressesAreAsyncLocalAndRestoreNestedScopes()
    {
        var scope = new BusRequestScope(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var taskA = Task.Run(async () => { using var address = scope.Enter(a, a, a); entered.SetResult(); await release.Task; Assert.Equal(a, scope.Snapshot().ModuleId); });
        await entered.Task;
        using (scope.Enter(b, b, b)) { Assert.Equal(b, scope.ModuleId); release.SetResult(); await taskA; Assert.Equal(b, scope.ModuleId); }
        Assert.Null(scope.ModuleId);
    }
    // Общий Save подтверждает отправленную ревизию, сохраняя новые правки dirty.
    [Fact]
    public async Task SaveAcknowledgesOnlyCapturedRevisionAndSerializesRetries()
    {
        var transport = new TestTransport { Block = true }; var scope = new BusRequestScope(); var context = new MiniAppSaveContext("owner", Guid.NewGuid(), scope, transport);
        var participant = new TestParticipant(); context.RegisterParticipant(participant); context.MarkChanged();
        var save = context.SaveAsync(Guid.NewGuid()); await transport.Started.Task;
        context.MarkChanged(); transport.Release.SetResult(); Assert.True((await save).Success); Assert.True(context.IsDirty);
        Assert.Equal(1, participant.LastAcknowledged); Assert.Equal(1, participant.Captures);
        transport.Block = false; Assert.True((await context.SaveAsync(Guid.NewGuid())).Success); Assert.False(context.IsDirty);
        context.MarkChanged(); await context.DiscardAsync(); Assert.False(context.IsDirty); Assert.Equal(1, participant.Discards);
    }
    // Потерянный ответ восстанавливается из durable outcome; неизвестный исход не меняет пакет при повторе.
    [Fact]
    public async Task UnknownOutcomeRetainsFrozenBatchAndMissingParticipantCannotClearDirty()
    {
        var transport = new TestTransport { Fail = true }; var context = new MiniAppSaveContext("owner", null, new BusRequestScope(), transport);
        context.MarkChanged(); Assert.False((await context.SaveAsync(Guid.NewGuid())).Success); Assert.True(context.IsDirty);
        var participant = new TestParticipant(); context.RegisterParticipant(participant);
        Assert.Equal(MiniAppSaveStatus.Unknown, (await context.SaveAsync(Guid.NewGuid())).Status);
        var operation = context.OperationId; Assert.NotNull(operation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.DiscardAsync());
        context.MarkChanged(); transport.Fail = false;
        Assert.True((await context.SaveAsync(Guid.NewGuid())).Success); Assert.True(context.IsDirty);
        Assert.Equal(operation, transport.LastBatch!.OperationId); Assert.Equal(1, participant.Captures);
        transport.Fail = true; transport.ReturnOutcome = true;
        Assert.True((await context.SaveAsync(Guid.NewGuid())).Success); Assert.False(context.IsDirty);
    }
    // Отказ права открытия не создаёт модуль и не публикует lifecycle.
    [Fact]
    public async Task DeniedModuleCannotOpen()
    {
        var bus = new MessageBus(); await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var catalog = new ModuleCatalog(bus, provider.GetRequiredService<IServiceScopeFactory>(), [], [], NullLogger<ModuleCatalog>.Instance);
        catalog.Accept(new(Guid.NewGuid(), "owner", [Definition("page")]));
        await using var manager = new WindowModularityManager(catalog, new(), new TestTransport(), new DenyAccess(), bus);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.OpenAsync("owner:page", Guid.NewGuid())); Assert.Empty(manager.Modules);
    }
    // Настоящий Razor host разрешает декларацию и создаёт адресованный компонент, а не строку имени выражения.
    [Fact]
    public async Task ShellHostRendersRegisteredComponentThroughWindowManager()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddBusinessEntityBus(); services.AddModularity();
        services.AddScoped<ISaveBatchTransport, TestTransport>(); services.AddScoped<IModuleAccessPolicy, AllowAccess>();
        services.AddModuleComponent<TestComponent>("test-ui");
        services.AddMiniAppHost("owner", Definition("shell") with { Placement = ModulePlacement.ShellModuleHostArea,
            InstancePolicy = ModuleInstancePolicy.WindowSingleton, HostArea = "TestArea", OpenOnStartup = true, HasSaveContext = false });
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ModuleCatalog>().CollectAsync();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(scope.ServiceProvider,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<ShellModuleHostArea>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Area"] = "TestArea" }));
            Assert.Contains("module-content", rendered.ToHtmlString());
            Assert.Single(scope.ServiceProvider.GetRequiredService<WindowModularityManager>().Modules);
        });
    }
    // Dirty и исход Save публикуются адресными immutable значениями, окна одного пользователя независимы.
    [Fact]
    public async Task SaveStateEventsHaveWindowAddressAndMonotonicRevision()
    {
        var bus = new MessageBus(); await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var catalog = new ModuleCatalog(bus, provider.GetRequiredService<IServiceScopeFactory>(), [], [], NullLogger<ModuleCatalog>.Instance);
        catalog.Accept(new(Guid.NewGuid(), "owner", [Definition("page")]));
        var user = Guid.NewGuid(); var firstScope = new BusRequestScope(); firstScope.SetUser(user);
        var secondScope = new BusRequestScope(); secondScope.SetUser(user);
        await using var first = new WindowModularityManager(catalog, firstScope, new TestTransport(), new AllowAccess(), bus);
        await using var second = new WindowModularityManager(catalog, secondScope, new TestTransport(), new AllowAccess(), bus);
        var states = new List<MiniAppSaveStateChanged>(); using var subscription = bus.Listen<MiniAppSaveStateChanged>().Subscribe(states.Add);
        var id = Guid.NewGuid(); var a = await first.OpenAsync("owner:page", id); var b = await second.OpenAsync("owner:page", id);
        a.SaveContext!.RegisterParticipant(new TestParticipant()); b.SaveContext!.RegisterParticipant(new TestParticipant());
        a.SaveContext.MarkChanged(); b.SaveContext.MarkChanged(); await a.SaveContext.SaveAsync(a.ModuleId);
        var own = states.Where(x => x.LogicalWindowId == first.LogicalWindowId).ToArray();
        Assert.All(own, x => { Assert.Equal(user, x.UserId); Assert.Equal(a.EditSessionId, x.EditSessionId); Assert.Equal(id, x.ContextId); });
        Assert.True(own.Zip(own.Skip(1), (x, y) => x.StateRevision < y.StateRevision).All(x => x));
        Assert.False(own.Last().IsDirty); Assert.Equal(MiniAppSaveStatus.Saved, own.Last().Status);
        Assert.True(states.Last(x => x.LogicalWindowId == second.LogicalWindowId).IsDirty);
    }
    // Общая тестовая декларация редактора не зависит от предметного типа.
    private static ModuleDefinition Definition(string id) => new("owner", id, id, null, ModulePlacement.Page, ModuleInstancePolicy.PerContext, "test-ui", HasSaveContext: true);
    // Startup definition для проверки порядка.
    private static ModuleDefinition Headless(string id, int order) => new("owner", id, id, null, ModulePlacement.Headless, ModuleInstancePolicy.AppSingleton, Order: order, OpenOnStartup: true);
    // Тестовый компонент для реестра UI.
    public sealed class TestComponent : ComponentBase
    {
        // Рендерит наблюдаемый результат разрешения ComponentKey.
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        { builder.AddContent(0, "module-content"); }
    }
    // Разрешение в unit-сценарии не заменяет production политику User.
    private sealed class AllowAccess : IModuleAccessPolicy
    {
        // Разрешает открытие тестового модуля.
        public Task<bool> CanOpenAsync(ModuleDefinition definition, Guid? contextId, CancellationToken ct) => Task.FromResult(true);
    }
    // Явный отказ без предметных зависимостей.
    private sealed class DenyAccess : IModuleAccessPolicy
    {
        // Запрещает открытие до создания адреса.
        public Task<bool> CanOpenAsync(ModuleDefinition definition, Guid? contextId, CancellationToken ct) => Task.FromResult(false);
    }
    // Наблюдает фактическое создание/завершение runtime.
    private sealed class TestRuntime(string id, List<string> events) : IModuleRuntime
    {
        // Фиксирует порядок запуска.
        public Task StartAsync(CancellationToken ct) { events.Add("start:" + id); return Task.CompletedTask; }
        // Фиксирует обратный порядок остановки.
        public Task StopAsync(CancellationToken ct) { events.Add("stop:" + id); return Task.CompletedTask; }
    }
    // Участник представляет весь набор сущностей одного владельца.
    private sealed class TestParticipant : IMiniAppSaveParticipant
    {
        public int Captures, Discards; public long LastAcknowledged;
        // Независимый пакет одного снимка.
        public EntitySaveBatch Capture(Guid operationId, Guid editSessionId, long revision) { Captures++; return new(operationId, "owner", editSessionId); }
        // Отмечает именно отправленную ревизию.
        public Task AcknowledgeAsync(EntitySaveBatch sent, SaveBatchResult result, long revision, CancellationToken ct) { LastAcknowledged = revision; return Task.CompletedTask; }
        // Убирает локальную правку.
        public Task DiscardAsync(CancellationToken ct) { Discards++; return Task.CompletedTask; }
    }
    // Управляемая потеря ответа и блокировка операции до новых правок.
    private sealed class TestTransport : ISaveBatchTransport
    {
        public bool Block, Fail, ReturnOutcome; public EntitySaveBatch? LastBatch;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Эмулирует передачу frozen package и длительное сохранение.
        public async Task<SaveBatchResult> SaveAsync(EntitySaveBatch batch, CancellationToken ct)
        { LastBatch = batch; Started.TrySetResult(); if (Block) await Release.Task; if (Fail) throw new TimeoutException(); return Result(batch.OperationId); }
        // Durable результат либо ещё неизвестен, либо уже записан.
        public Task<StoredOperationOutcome?> GetOutcomeAsync(Guid operationId, CancellationToken ct) => Task.FromResult(ReturnOutcome ? new StoredOperationOutcome(operationId, null, DateTime.UtcNow, Result(operationId)) : null);
        // Не содержит предметных данных.
        private static SaveBatchResult Result(Guid id) => new(id, DateTime.UtcNow, [], [], [], [], []);
    }
}

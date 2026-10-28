using BusinessEntity.Core.Saving;
using System.Collections.Immutable;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.Infrastructure.Selection;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Xunit;

namespace BusinessEntity.Tests.Modularity;

// Проверяет master/helper на общей шине с настоящим жизненным циклом модулей.
public sealed class SelectionTests
{
    // Поздний helper получает текущий immutable снимок; исходный список не разделяется.
    [Fact]
    public async Task LateHelperGetsCopiedCurrentSnapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var master = await fixture.Manager.OpenAsync("owner:a"); var helper = await fixture.Manager.OpenAsync("owner:helper");
        using var publisher = new SelectionPublisher(fixture.Bus, fixture.Manager, master.ModuleId);
        var list = new List<SelectedObjectSnapshot> { Object("first") }; publisher.Publish(list); list.Clear();
        using var observer = new SelectionObserver(fixture.Bus, fixture.Manager, helper.ModuleId);
        Assert.Equal("first", Assert.Single(observer.Snapshot!.Objects).Title);
        Assert.Equal(master.ModuleId, observer.Snapshot.OriginModuleId);
    }
    // Старые ревизии, поздние события прежнего master и его прежнего поколения отбрасываются.
    [Fact]
    public async Task SourceSwitchAndRevisionOrderingRejectStaleValues()
    {
        await using var fixture = await Fixture.CreateAsync();
        var a = await fixture.Manager.OpenAsync("owner:a"); var b = await fixture.Manager.OpenAsync("owner:b");
        var helper = await fixture.Manager.OpenAsync("owner:helper");
        using var first = new SelectionPublisher(fixture.Bus, fixture.Manager, a.ModuleId);
        using var second = new SelectionPublisher(fixture.Bus, fixture.Manager, b.ModuleId);
        using var observer = new SelectionObserver(fixture.Bus, fixture.Manager, helper.ModuleId);
        first.Publish([Object("a1")]); var oldA = observer.Snapshot!;
        first.Publish([Object("a2")]); fixture.Bus.SendMessage(oldA); Assert.Equal("a2", observer.Snapshot!.Objects[0].Title);
        fixture.Manager.ActivateSelectionSource(b.ModuleId); Assert.Null(observer.Snapshot);
        second.Publish([Object("b1")]); fixture.Bus.SendMessage(oldA with { SelectionRevision = 999 });
        Assert.Equal("b1", observer.Snapshot!.Objects[0].Title);
        first.Publish([Object("a3")]); fixture.Bus.SendMessage(oldA with { SelectionRevision = 1000 });
        Assert.Equal("a3", observer.Snapshot!.Objects[0].Title);
    }
    // Закрытие источника очищает helper; после disposal подписок он не принимает новые значения.
    [Fact]
    public async Task ClosedSourceAndDisposedHelperCannotReceiveSnapshots()
    {
        await using var fixture = await Fixture.CreateAsync();
        var master = await fixture.Manager.OpenAsync("owner:a"); var helper = await fixture.Manager.OpenAsync("owner:helper");
        using var publisher = new SelectionPublisher(fixture.Bus, fixture.Manager, master.ModuleId);
        using var observer = new SelectionObserver(fixture.Bus, fixture.Manager, helper.ModuleId);
        publisher.Publish([Object("selected")]); var old = observer.Snapshot!;
        await fixture.Manager.CloseAsync(master.ModuleId); Assert.Null(observer.Snapshot);
        fixture.Bus.SendMessage(old with { SelectionRevision = 999 }); Assert.Null(observer.Snapshot);
        await fixture.Manager.CloseAsync(helper.ModuleId); fixture.Bus.SendMessage(old); Assert.Null(observer.Snapshot);
    }
    // Одинаковый UserId не объединяет контексты выделения разных browser windows.
    [Fact]
    public async Task TwoWindowsOfOneUserRemainIndependent()
    {
        var bus = new MessageBus(); var user = Guid.NewGuid();
        await using var first = await Fixture.CreateAsync(bus, user); await using var second = await Fixture.CreateAsync(bus, user);
        var a = await first.Manager.OpenAsync("owner:a"); var h1 = await first.Manager.OpenAsync("owner:helper");
        var b = await second.Manager.OpenAsync("owner:a"); var h2 = await second.Manager.OpenAsync("owner:helper");
        using var p1 = new SelectionPublisher(bus, first.Manager, a.ModuleId); using var p2 = new SelectionPublisher(bus, second.Manager, b.ModuleId);
        using var o1 = new SelectionObserver(bus, first.Manager, h1.ModuleId); using var o2 = new SelectionObserver(bus, second.Manager, h2.ModuleId);
        p1.Publish([Object("window 1")]); p2.Publish([Object("window 2")]);
        Assert.Equal("window 1", o1.Snapshot!.Objects[0].Title); Assert.Equal("window 2", o2.Snapshot!.Objects[0].Title);
        await first.Manager.CloseAsync(a.ModuleId); Assert.Null(o1.Snapshot); Assert.NotNull(o2.Snapshot);
    }
    // Значения теста состоят только из immutable metadata объекта.
    private static SelectedObjectSnapshot Object(string title) => new(Guid.NewGuid(), title, "Test", DateTime.UtcNow, DateTime.UtcNow);
    // Создаёт независимый менеджер окна на заданной общей шине.
    private sealed class Fixture : IAsyncDisposable
    {
        public MessageBus Bus { get; }
        public WindowModularityManager Manager { get; }
        private readonly ModuleCatalog _catalog;
        private readonly ServiceProvider _provider;
        private Fixture(MessageBus bus, WindowModularityManager manager, ModuleCatalog catalog, ServiceProvider provider)
        { Bus = bus; Manager = manager; _catalog = catalog; _provider = provider; }
        // Каталог содержит два master и helper, не связанные с предметными типами.
        public static Task<Fixture> CreateAsync(MessageBus? shared = null, Guid? user = null)
        {
            var bus = shared ?? new MessageBus(); var provider = new ServiceCollection().BuildServiceProvider();
            var catalog = new ModuleCatalog(bus, provider.GetRequiredService<IServiceScopeFactory>(), [], [], NullLogger<ModuleCatalog>.Instance);
            catalog.Accept(new(Guid.NewGuid(), "owner", new[] { "a", "b", "helper" }.Select(id => new ModuleDefinition("owner", id, id, null,
                ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, ComponentKey: "test")).ToArray()));
            var scope = new BusRequestScope(); scope.SetUser(user);
            return Task.FromResult(new Fixture(bus, new(catalog, scope, new Transport(), new Access(), bus), catalog, provider));
        }
        // Освобождает runtime в том же порядке, что завершение browser circuit.
        public async ValueTask DisposeAsync() { await Manager.DisposeAsync(); await _catalog.DisposeAsync(); await _provider.DisposeAsync(); }
    }
    // Проверка выделения не выполняет SQL или операций сохранения.
    private sealed class Transport : ISaveBatchTransport
    {
        public Task<SaveBatchResult> SaveAsync(EntitySaveBatch batch, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredOperationOutcome?> GetOutcomeAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
    // В изолированном lifecycle тесте открытие всех объявленных модулей разрешено.
    private sealed class Access : IModuleAccessPolicy
    { public Task<bool> CanOpenAsync(ModuleDefinition definition, Guid? context, CancellationToken ct) => Task.FromResult(true); }
}

using System.Reactive.Linq;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessEntity.Tests.Modularity;

// Проверяет команду от UI через общую шину к точному runtime binding своего окна.
public sealed class CommandTests
{
    // Два окна, две кнопки одной команды и снятый binding не смешивают callbacks.
    [Fact]
    public async Task CommandsUseWindowModuleAndBindingAndRejectClosedOrDisabledTargets()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddBusinessEntityBus(); services.AddModularity();
        services.AddScoped<ISaveBatchTransport, UnusedTransport>(); services.AddScoped<IModuleAccessPolicy, TestAccess>();
        services.AddMiniAppHost("owner", new ModuleDefinition("owner", "page", "Page", null, ModulePlacement.Page,
            ModuleInstancePolicy.PerContext, "unused"));
        services.AddMiniAppButtons(new ButtonDefinition("owner", "save", "Document.Toolbar", "Сохранить"));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.GetRequiredService<ModuleCatalog>().CollectAsync();
        var definition = provider.GetRequiredService<ButtonCatalog>().Get("owner", "save", "Document.Toolbar");
        await using var first = provider.CreateAsyncScope(); await using var second = provider.CreateAsyncScope();
        var one = first.ServiceProvider; var two = second.ServiceProvider;
        var firstModule = await one.GetRequiredService<WindowModularityManager>().OpenAsync("owner:page", Guid.NewGuid());
        var secondModule = await two.GetRequiredService<WindowModularityManager>().OpenAsync("owner:page", firstModule.ContextId);
        var snapshots = new List<ButtonStateSnapshot>();
        using var stateSubscription = provider.GetRequiredService<ReactiveUI.IMessageBus>().Listen<ButtonStateSnapshot>().Subscribe(snapshots.Add);
        var a = 0; var b = 0; var helper = 0; var firstBinding = Guid.NewGuid(); var secondBinding = Guid.NewGuid(); var helperBinding = Guid.NewGuid();
        using var bindingA = one.GetRequiredService<ModuleCommandRegistry>().Bind(firstModule, definition, new(true, true),
            () => { a++; return Task.CompletedTask; }, firstBinding);
        using var bindingB = two.GetRequiredService<ModuleCommandRegistry>().Bind(secondModule, definition, new(true, true),
            () => { b++; return Task.CompletedTask; }, secondBinding);
        using var bindingHelper = one.GetRequiredService<ModuleCommandRegistry>().Bind(firstModule, definition, new(true, true),
            () => { helper++; return Task.CompletedTask; }, helperBinding);
        Assert.Equal(3, snapshots.Select(snapshot => snapshot.BindingId).Distinct().Count());
        Assert.Equal(firstModule.LogicalWindowId, snapshots.Single(snapshot => snapshot.BindingId == firstBinding).LogicalWindowId);
        Assert.Equal(secondModule.LogicalWindowId, snapshots.Single(snapshot => snapshot.BindingId == secondBinding).LogicalWindowId);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, ButtonState>)snapshots[0].States).Add("foreign", new(true, true)));
        await Invoke(one, firstModule, firstBinding); await Invoke(two, secondModule, secondBinding); await Invoke(one, firstModule, helperBinding);
        Assert.Equal((1, 1, 1), (a, b, helper));
        await Assert.ThrowsAsync<BusRequestFailedException>(() => Invoke(two, firstModule, firstBinding));
        bindingA.Dispose(); await Assert.ThrowsAsync<BusRequestFailedException>(() => Invoke(one, firstModule, firstBinding));
        using var disabled = one.GetRequiredService<ModuleCommandRegistry>().Bind(firstModule, definition, new(true, false),
            () => { a++; return Task.CompletedTask; }, firstBinding);
        await Assert.ThrowsAsync<BusRequestFailedException>(() => Invoke(one, firstModule, firstBinding));
        await one.GetRequiredService<WindowModularityManager>().CloseAsync(firstModule.ModuleId);
        await Assert.ThrowsAsync<BusRequestFailedException>(() => Invoke(one, firstModule, helperBinding));
        Assert.Equal((1, 1, 1), (a, b, helper));
    }
    // Шина может доставить UI-команду с фонового потока; владелец выполняется на dispatcher своего renderer.
    [Fact]
    public async Task BackgroundCommandDispatchesToOwningRenderer()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddBusinessEntityBus(); services.AddModularity();
        services.AddScoped<ISaveBatchTransport, UnusedTransport>(); services.AddScoped<IModuleAccessPolicy, TestAccess>();
        services.AddScoped<Counter>(); services.AddModuleComponent<CommandPage>("test.command-page");
        services.AddMiniAppHost("owner", new ModuleDefinition("owner", "page", "Page", null, ModulePlacement.Page,
            ModuleInstancePolicy.PerContext, "test.command-page"));
        services.AddMiniAppButtons(new ButtonDefinition("owner", "save", "Document.Toolbar", "Сохранить"));
        await using var provider = services.BuildServiceProvider(); await provider.GetRequiredService<ModuleCatalog>().CollectAsync();
        await using var scope = provider.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var snapshots = new List<ButtonStateSnapshot>(); using var subscription = provider.GetRequiredService<ReactiveUI.IMessageBus>()
            .Listen<ButtonStateSnapshot>().Subscribe(snapshots.Add);
        await using var renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(sp,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<PageModuleHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["DefinitionKey"] = "owner:page", ["ContextId"] = Guid.NewGuid() })));
        var module = sp.GetRequiredService<WindowModularityManager>().Modules.Single(); var bindingId = snapshots.Last().BindingId;
        await Task.Run(() => Invoke(sp, module, bindingId));
        Assert.Equal(1, sp.GetRequiredService<Counter>().Value);
    }
    // Scoped состояние тестового владельца не связано с UI-кэшем команды.
    public sealed class Counter { public int Value; }
    // Настоящий компонент даёт callback через декларативный приёмник.
    public sealed class CommandPage : ComponentBase
    {
        [Inject] public Counter State { get; set; } = default!;
        // Изменение состояния требует renderer context при вызове EventCallback.
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<DeclaredButton>(0); builder.AddAttribute(1, "MiniAppId", "owner"); builder.AddAttribute(2, "CommandId", "save");
            builder.AddAttribute(3, "OnInvoked", EventCallback.Factory.Create(this, () => State.Value++)); builder.CloseComponent();
        }
    }
    // Контекст запроса устанавливает manager, а не поля browser callback.
    private static async Task Invoke(IServiceProvider services, MiniAppModule module, Guid bindingId)
    {
        using var address = services.GetRequiredService<BusRequestScope>().Enter(module.ModuleId, module.EditSessionId, module.ContextId);
        await services.GetRequiredService<BusRequestClient>().SendAsync<ButtonCommandInvoked, ButtonCommandResult>(new(Guid.NewGuid(), "owner", "save", bindingId));
    }
    // Тест разрешает доступ; ограничения disabled и адресов проверяет сам runtime.
    private sealed class TestAccess : IModuleAccessPolicy
    {
        // Не зависит от production identity.
        public Task<bool> CanOpenAsync(ModuleDefinition definition, Guid? contextId, CancellationToken ct) => Task.FromResult(true);
    }
    // Командный тест не вызывает storage или Save-контекст.
    private sealed class UnusedTransport : ISaveBatchTransport
    {
        // Неожиданное сохранение означает нарушение границ теста.
        public Task<SaveBatchResult> SaveAsync(EntitySaveBatch batch, CancellationToken ct) => throw new NotSupportedException();
        // Исход не требуется для командного сценария.
        public Task<StoredOperationOutcome?> GetOutcomeAsync(Guid operationId, CancellationToken ct) => throw new NotSupportedException();
    }
}

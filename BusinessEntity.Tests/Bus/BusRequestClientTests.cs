using BusinessEntity.Infrastructure.Bus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Xunit;

namespace BusinessEntity.Tests.Bus;

// Проверяет реальные обмены на одном MessageBus и независимость scoped-обработчиков.
public sealed class BusRequestClientTests
{
    // Ответ с чужой корреляцией не завершает собственный запрос.
    [Fact]
    public async Task IgnoresForeignRequestIdAndReturnsOwnResponse()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ProbeState>().EmitForeignResponse = true;
        var client = scope.ServiceProvider.GetRequiredService<BusRequestClient>();
        var response = await client.SendAsync<ProbeRequest, ProbeResponse>(new(Guid.NewGuid()));
        Assert.Equal("own", response.Result);
    }

    // Два окна одного пользователя имеют двух слушателей, но по одному исполнителю на адрес.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoutesWindowsAndUsersIndependently(bool differentUsers)
    {
        using var services = CreateServices();
        using var first = services.CreateScope();
        using var second = services.CreateScope();
        var user = Guid.NewGuid();
        first.ServiceProvider.GetRequiredService<BusRequestScope>().SetUser(user);
        var secondUser = differentUsers ? Guid.NewGuid() : user;
        second.ServiceProvider.GetRequiredService<BusRequestScope>().SetUser(secondUser);
        var firstState = first.ServiceProvider.GetRequiredService<ProbeState>();
        var secondState = second.ServiceProvider.GetRequiredService<ProbeState>();
        firstState.Result = "first";
        firstState.DelayMs = 25;
        secondState.DelayMs = 10;
        secondState.Result = "second";
        first.ServiceProvider.GetRequiredService<ProbeEndpoint>().EnsureSubscribed();
        second.ServiceProvider.GetRequiredService<ProbeEndpoint>().EnsureSubscribed();

        var sharedRequestId = Guid.NewGuid();
        var results = await Task.WhenAll(
            first.ServiceProvider.GetRequiredService<BusRequestClient>()
                .SendAsync<ProbeRequest, ProbeResponse>(new(sharedRequestId)),
            second.ServiceProvider.GetRequiredService<BusRequestClient>()
                .SendAsync<ProbeRequest, ProbeResponse>(new(sharedRequestId)));

        Assert.Equal(new[] { "first", "second" }, results.Select(x => x.Result));
        Assert.Equal(1, firstState.RequestCount);
        Assert.Equal(1, secondState.RequestCount);
        Assert.Equal(user, firstState.Actor);
        Assert.Equal(secondUser, secondState.Actor);
    }

    // Таймаут ограничивает ожидание зарегистрированного, но не ответившего обработчика.
    [Fact]
    public async Task TimesOutWithoutInventingSuccess()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<BusRequestClient>();
        await Assert.ThrowsAsync<BusRequestTimeoutException>(() =>
            client.SendAsync<SilentRequest, SilentResponse>(new(Guid.NewGuid()),
                timeout: TimeSpan.FromMilliseconds(40)));
    }

    // Отмена до отправки не создаёт и не исполняет обработчик.
    [Fact]
    public async Task CancellationBeforeSendDoesNotExecuteOperation()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<BusRequestClient>()
                .SendAsync<ProbeRequest, ProbeResponse>(new(Guid.NewGuid()), cancellation.Token));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<ProbeState>().RequestCount);
    }

    // Отмена ожидающего запроса различается с истечением таймаута.
    [Fact]
    public async Task CancellationDuringWaitReturnsCancellation()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<BusRequestClient>()
                .SendAsync<SilentRequest, SilentResponse>(new(Guid.NewGuid()), cancellation.Token));
    }

    // Типизированный отказ сохраняет стабильный код.
    [Fact]
    public async Task PreservesTypedFailure()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ProbeState>().Fail = true;
        var error = await Assert.ThrowsAsync<BusRequestFailedException>(() =>
            scope.ServiceProvider.GetRequiredService<BusRequestClient>()
                .SendAsync<ProbeRequest, ProbeResponse>(new(Guid.NewGuid())));
        Assert.Equal("probe.rejected", error.Error.Code);
    }

    // Завершённый scope больше не обрабатывает публикации по прежнему адресу.
    [Fact]
    public void ScopeDisposalUnsubscribesEndpoint()
    {
        using var services = CreateServices();
        var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusRequestScope>().Snapshot();
        var state = scope.ServiceProvider.GetRequiredService<ProbeState>();
        scope.ServiceProvider.GetRequiredService<ProbeEndpoint>().EnsureSubscribed();
        scope.Dispose();
        services.GetRequiredService<IMessageBus>()
            .SendMessage(new ProbeRequest(Guid.NewGuid()) { Context = context });
        Assert.Equal(0, state.RequestCount);
    }

    // Неизвестный маршрут даёт явный отказ вместо ожидания 30 секунд.
    [Fact]
    public async Task RejectsMissingEndpoint()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        var failure = await Assert.ThrowsAsync<BusRequestFailedException>(() =>
            scope.ServiceProvider.GetRequiredService<BusRequestClient>()
                .SendAsync<UnknownRequest, SilentResponse>(new(Guid.NewGuid())));
        Assert.Equal("bus.endpoint_missing", failure.Error.Code);
    }

    // Повторная и конкурентная инициализация не создаёт второй обработчик адреса.
    [Fact]
    public void ConcurrentInitializationSubscribesOnce()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<ProbeEndpoint>();
        Parallel.For(0, 40, _ => endpoint.EnsureSubscribed());
        var context = scope.ServiceProvider.GetRequiredService<BusRequestScope>().Snapshot();
        services.GetRequiredService<IMessageBus>().SendMessage(new ProbeRequest(Guid.NewGuid()) { Context = context });
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<ProbeState>().RequestCount);
    }

    // Контекст сохранения и модуля передаётся сервером, входной UserId запроса перезаписывается.
    [Fact]
    public async Task ReplacesUntrustedContextWithServerSource()
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusRequestScope>();
        var actor = Guid.NewGuid();
        context.SetUser(actor);
        var module = Guid.NewGuid(); var editSession = Guid.NewGuid(); var entity = Guid.NewGuid();
        context.SetSource(module, editSession, entity);
        await scope.ServiceProvider.GetRequiredService<BusRequestClient>().SendAsync<ProbeRequest, ProbeResponse>(
            new(Guid.NewGuid()) { Context = new(Guid.NewGuid(), Guid.NewGuid()) });
        var received = scope.ServiceProvider.GetRequiredService<ProbeState>().Context!;
        Assert.Equal(actor, received.UserId);
        Assert.Equal(context.LogicalWindowId, received.LogicalWindowId);
        Assert.Equal(module, received.ModuleId);
        Assert.Equal(editSession, received.EditSessionId);
        Assert.Equal(entity, received.ContextId);
    }

    // Собирает два тестовых предметных маршрута на одном транспорте.
    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBusinessEntityBus();
        services.AddScoped<ProbeState>();
        services.AddScoped<ProbeEndpoint>();
        services.AddScoped<SilentEndpoint>();
        services.AddBusEndpoint<ProbeEndpoint>(typeof(ProbeRequest));
        services.AddBusEndpoint<SilentEndpoint>(typeof(SilentRequest));
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Предметный тестовый запрос.
    private sealed record ProbeRequest(Guid RequestId) : BusRequest(RequestId);
    // Предметный тестовый ответ.
    private sealed record ProbeResponse(Guid RequestId, string? Result, BusError? Error = null) : BusResponse(RequestId, Error);
    // Запрос без ответившего исполнителя.
    private sealed record SilentRequest(Guid RequestId) : BusRequest(RequestId);
    // Ответ для теста ожидания.
    private sealed record SilentResponse(Guid RequestId, BusError? Error = null) : BusResponse(RequestId, Error);
    // Запрос без декларации маршрута.
    private sealed record UnknownRequest(Guid RequestId) : BusRequest(RequestId);

    // Состояние собственного экземпляра тестового домена.
    private sealed class ProbeState
    {
        public string Result { get; set; } = "own";
        public bool EmitForeignResponse { get; set; }
        public bool Fail { get; set; }
        public int DelayMs { get; set; }
        public int RequestCount { get; set; }
        public Guid? Actor { get; set; }
        public BusRequestContext? Context { get; set; }
    }

    // Исполнитель фильтруется общим scoped-адаптером до доступа к своему состоянию.
    private sealed class ProbeEndpoint : ScopedBusEndpoint
    {
        private readonly ProbeState _state;
        private readonly IMessageBus _bus;
        // Получает зависимости собственного scope.
        public ProbeEndpoint(ProbeState state, IMessageBus bus, BusRequestScope scope, ILogger<ProbeEndpoint> logger)
            : base(bus, scope, logger) { _state = state; _bus = bus; }
        // Подключает одну пару предметных сообщений.
        protected override void SubscribeRequests()
        {
                Subscribe<ProbeRequest, ProbeResponse>(async request =>
            {
                _state.RequestCount++;
                _state.Actor = request.Context.UserId;
                _state.Context = request.Context;
                if (_state.EmitForeignResponse) _bus.SendMessage(new ProbeResponse(Guid.NewGuid(), "foreign"));
                if (_state.EmitForeignResponse) _bus.SendMessage(new ProbeResponse(request.RequestId, "foreign-window"));
                if (_state.DelayMs > 0) await Task.Delay(_state.DelayMs);
                return new ProbeResponse(request.RequestId, _state.Result,
                    _state.Fail ? new BusError("probe.rejected", "Отказ тестовой операции") : null);
            }, static (id, error) => new ProbeResponse(id, null, error));
        }
    }

    // Имитирует принятый маршрут, который не прислал результат вовремя.
    private sealed class SilentEndpoint : IBusEndpoint
    {
        // Тест намеренно не создаёт источник ответов.
        public void EnsureSubscribed() { }
    }
}

using ReactiveUI;
using System.Reactive.Linq;

namespace BusinessEntity.Infrastructure.Bus;

// Выполняет коррелированные запросы через единственную шину приложения.
public sealed class BusRequestClient
{
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(30);
    private readonly IMessageBus _bus;
    private readonly BusRequestScope _scope;
    private readonly IServiceProvider _services;
    private readonly IReadOnlyDictionary<Type, Type> _routes;

    // Получает транспорт и scoped-контекст; обработчики создаются лениво по декларациям.
    public BusRequestClient(IMessageBus bus, BusRequestScope scope, IServiceProvider services,
        IEnumerable<BusEndpointRegistration> registrations)
    {
        _bus = bus;
        _scope = scope;
        _services = services;
        _routes = registrations.GroupBy(r => r.RequestType).ToDictionary(g => g.Key, g =>
            g.Select(r => r.EndpointType).Distinct().Single());
    }

    // Подписывается до отправки, проверяет корреляцию и освобождает ожидание всегда.
    public async Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        where TRequest : BusRequest
        where TResponse : BusResponse
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deadline = timeout ?? DefaultTimeout;
        if (deadline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!_routes.TryGetValue(typeof(TRequest), out var endpoint))
            throw new BusRequestFailedException(new BusError("bus.endpoint_missing", "Обработчик запроса не зарегистрирован."));
        ((IBusEndpoint)_services.GetRequiredService(endpoint)).EnsureSubscribed();

        var addressed = (TRequest)(request with { Context = _scope.Snapshot() });
        var completion = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = _bus.Listen<TResponse>()
            .Where(response => response.RequestId == request.RequestId && response.Context == addressed.Context)
            .Take(1)
            .Subscribe(response => completion.TrySetResult(response),
                exception => completion.TrySetException(exception));
        using var timeoutSource = new CancellationTokenSource(deadline);
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        using var expiry = timeoutSource.Token.Register(() =>
            completion.TrySetException(new BusRequestTimeoutException(request.RequestId, deadline)));
        _bus.SendMessage(addressed);
        var response = await completion.Task.ConfigureAwait(false);
        if (response.Error is not null) throw new BusRequestFailedException(response.Error);
        return response;
    }
}

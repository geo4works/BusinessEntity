using ReactiveUI;
using System.Reactive.Linq;

namespace BusinessEntity.Infrastructure.Bus;

// Общая механика подписок и ответов; предметные обработчики объявляет владелец.
public abstract class BusEndpoint : IBusEndpoint, IDisposable
{
    private readonly IMessageBus _bus;
    private readonly ILogger _logger;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _gate = new();
    private bool _disposed;

    // Привязывает обработчик к одному server scope на общем транспорте.
    protected BusEndpoint(IMessageBus bus, ILogger logger)
    {
        _bus = bus;
        _logger = logger;
    }

    // Владелец перечисляет только свои предметные запросы.
    public void EnsureSubscribed()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_subscriptions.Count == 0) SubscribeRequests();
        }
    }

    // Подключает собственные пары запросов под блокировкой инициализации.
    protected abstract void SubscribeRequests();

    // Общий singleton-обработчик принимает свой тип запроса без выбора случайного circuit.
    protected virtual bool OwnsRequest(BusRequest request) => true;

    // Подписывает конкретную пару Request/Response с фильтром до выполнения.
    protected void Subscribe<TRequest, TResponse>(Func<TRequest, Task<TResponse>> execute,
        Func<Guid, BusError, TResponse> failure)
        where TRequest : BusRequest
        where TResponse : BusResponse
    {
        _subscriptions.Add(_bus.Listen<TRequest>().Where(OwnsRequest)
            .Subscribe(request => _ = HandleAsync(request, execute, failure)));
    }

    // Возвращает ровно один предметный ответ или типизированный отказ.
    private async Task HandleAsync<TRequest, TResponse>(TRequest request,
        Func<TRequest, Task<TResponse>> execute, Func<Guid, BusError, TResponse> failure)
        where TRequest : BusRequest
        where TResponse : BusResponse
    {
        TResponse response;
        try { response = await execute(request).ConfigureAwait(false); }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Bus request {RequestId} failed in {Endpoint}",
                request.RequestId, GetType().Name);
            response = failure(request.RequestId, BusError.FromException(exception));
        }
        _bus.SendMessage((TResponse)(response with { Context = request.Context }));
    }

    // Освобождает все подписки при завершении circuit/HTTP scope.
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
        }
        GC.SuppressFinalize(this);
    }
}

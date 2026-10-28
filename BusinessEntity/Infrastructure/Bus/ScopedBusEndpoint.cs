using ReactiveUI;

namespace BusinessEntity.Infrastructure.Bus;

// Scoped-обработчик добавляет обязательную проверку адреса до доступа к своему состоянию.
public abstract class ScopedBusEndpoint : BusEndpoint
{
    private readonly BusRequestScope _scope;
    // Привязывает подписки к одному окну/HTTP scope на общем транспорте.
    protected ScopedBusEndpoint(IMessageBus bus, BusRequestScope scope, ILogger logger) : base(bus, logger)
        => _scope = scope;
    // Отсекает чужой адрес до начала предметной работы.
    protected override bool OwnsRequest(BusRequest request) => _scope.Owns(request);
}

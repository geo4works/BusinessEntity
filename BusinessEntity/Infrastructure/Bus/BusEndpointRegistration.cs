using Microsoft.Extensions.DependencyInjection.Extensions;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Bus;

// Связывает предметный Request с собственным обработчиком миниаппа.
public sealed record BusEndpointRegistration(Type RequestType, Type EndpointType);

// Подключает единственный транспорт, клиентов и декларации маршрутов.
public static class BusRegistration
{
    // Создаёт общие и scoped-механизмы без знания предметных миниаппов.
    public static IServiceCollection AddBusinessEntityBus(this IServiceCollection services)
    {
        services.TryAddSingleton<IMessageBus, MessageBus>();
        services.TryAddScoped<BusRequestScope>();
        services.TryAddScoped<BusRequestClient>();
        return services;
    }

    // Регистрирует маршруты обработчика; его время жизни задаёт владелец.
    public static IServiceCollection AddBusEndpoint<TEndpoint>(
        this IServiceCollection services, params Type[] requestTypes)
        where TEndpoint : class, IBusEndpoint
    {
        foreach (var requestType in requestTypes)
            services.AddSingleton(new BusEndpointRegistration(requestType, typeof(TEndpoint)));
        return services;
    }
}

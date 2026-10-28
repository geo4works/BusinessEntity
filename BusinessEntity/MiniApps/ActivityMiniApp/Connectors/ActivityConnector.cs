using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts.Messages;

namespace BusinessEntity.MiniApps.ActivityMiniApp.Connectors;

// Публичный клиент предметных запросов через единственную шину.
public sealed class ActivityConnector : IActivityConnector
{
    private readonly BusRequestClient _client;
    private readonly IUserConnector _user;

    // Получает клиент и серверный контекст без фасадов и внутренних сервисов.
    public ActivityConnector(BusRequestClient client, IUserConnector user)
    { _client = client; _user = user; }

    // Выполняет GetComments через адресный request/response.
    public async Task<IReadOnlyList<BusinessEntityCommentRecord>> GetCommentsAsync(Guid businessEntityId, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendAsync<GetCommentsRequest, GetCommentsResponse>(
            new GetCommentsRequest(Guid.NewGuid(), businessEntityId), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }

    // Выполняет CreateComment через адресный request/response.
    public async Task<BusinessEntityCommentRecord> CreateCommentAsync(BusinessEntityCommentCreateRequest request, CancellationToken cancellationToken = default)
    {
        await _user.EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<CreateCommentRequest, CreateCommentResponse>(
            new CreateCommentRequest(Guid.NewGuid(), request), cancellationToken);
        return response.Result ?? throw new BusRequestFailedException(new BusError("bus.invalid_response", "Обработчик не вернул обязательный результат."));
    }
}

using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;

namespace BusinessEntity.MiniApps.TreeMiniApp.Connectors;

// Адресный клиент дерева без фасада и собственной реализации ожидания.
public sealed class TreeConnector : ITreeConnector
{
    private readonly BusRequestClient _client;
    private readonly IUserConnector _user;
    // Получает общий клиент текущего scope.
    public TreeConnector(BusRequestClient client, IUserConnector user) { _client = client; _user = user; }
    // Запрашивает видимое пользователю дерево своего окна.
    public async Task<TreeSpaceSnapshot?> GetTreeForSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        await _user.EnsureCurrentUserAsync(cancellationToken);
        var response = await _client.SendAsync<GetTreeForSpaceRequest, GetTreeForSpaceResponse>(
            new GetTreeForSpaceRequest(Guid.NewGuid(), spaceId), cancellationToken);
        return response.Snapshot;
    }
    // Внешний владелец вызывает дерево только типизированным сообщением.
    public async Task DeleteEntitiesAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        await _user.EnsureCurrentUserAsync(cancellationToken);
        await _client.SendAsync<DeleteTreeEntitiesRequest, DeleteTreeEntitiesResponse>(new(Guid.NewGuid(), entityIds.ToArray()), cancellationToken);
    }
}

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Единственный сервер сериализует административные изменения конфигурации и активации планов.
internal sealed class IdentityMutationCoordinator
{
    private readonly SemaphoreSlim _gate = new(1,1);
    // Сериализация принадлежит владельцу identity и не является глобальным Save пространства.
    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct)
    { await _gate.WaitAsync(ct); try { return await action(); } finally { _gate.Release(); } }
}

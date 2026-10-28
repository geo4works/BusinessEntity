using BusinessEntity.Services;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Изолирует текущую identity-сессию от общих операций пользователей и ролей.
internal sealed class UserIdentitySession : IDisposable
{
    private readonly BusinessEntityUserFactory _factory;
    private readonly AuthentikSessionManager _auth;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BusinessEntityUser? _currentUser;
    private bool _loaded;

    // Получает только инфраструктуру текущей сессии.
    public UserIdentitySession(BusinessEntityUserFactory factory, AuthentikSessionManager auth)
    { _factory = factory; _auth = auth; }

    // Загружает identity один раз в пределах доверенного server scope.
    public async Task<BusinessEntityUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_loaded)
            {
                _currentUser = await _factory.CreateAsync(cancellationToken);
                _loaded = true;
            }
            return _currentUser;
        }
        finally { _gate.Release(); }
    }

    // Делегирует проверку текущего пароля инфраструктуре Authentik.
    public Task<bool> ValidatePasswordAsync(string username, string password, CancellationToken cancellationToken = default)
        => _auth.ValidatePasswordAsync(username, password, cancellationToken);

    // Освобождает примитив синхронизации вместе с текущей сессией.
    public void Dispose() => _gate.Dispose();
}

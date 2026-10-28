using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using BusinessEntity.Services;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Обеспечивает OIDC bootstrap до синхронизации локальных пользователей.
internal sealed class IdentityStartupRuntime : IModuleRuntime
{
    private readonly AuthentikBootstrapService _bootstrap;
    // Единственная зависимость — инфраструктура identity.
    public IdentityStartupRuntime(AuthentikBootstrapService bootstrap) => _bootstrap = bootstrap;
    // Ошибка обязательной конфигурации прерывает startup.
    public Task StartAsync(CancellationToken ct) => _bootstrap.EnsureAsync(ct);
    // Внешний identity server не останавливается вместе с приложением.
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
// User владеет своей схемой и defaults, каталог задаёт только порядок.
internal sealed class UserStartupRuntime : IModuleRuntime
{
    private readonly DbContextOptions<UserMiniAppDbContext> _options;
    private readonly ApplicationStartupState _state;
    private readonly UserMiniAppService _users;
    // Не использует собственный connector для вызова своего сервиса.
    public UserStartupRuntime(DbContextOptions<UserMiniAppDbContext> options, ApplicationStartupState state, UserMiniAppService users)
    { _options = options; _state = state; _users = users; }
    // Schema проверяется раньше первого чтения локальных прав и пользователей.
    public async Task StartAsync(CancellationToken ct)
    {
        await using var db = new UserMiniAppDbContext(_options);
        await UserMiniAppStorageSchema.InitializeAsync(db, _state.FreshEntityStorage, ct);
        await _users.EnsureSystemDefaultsAsync(ct);
    }
    // Scoped ресурсы освобождает каталог.
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
// Реализация общей проверки открытия принадлежит владельцу прав.
internal sealed class UserModuleAccessPolicy : IModuleAccessPolicy
{
    private readonly UserMiniAppService _users;
    private readonly IUserContextService _space;
    private readonly BusRequestScope _scope;
    // Проверяет identity напрямую внутри User, без самозапроса по шине.
    public UserModuleAccessPolicy(UserMiniAppService users, IUserContextService space, BusRequestScope scope)
    { _users = users; _space = space; _scope = scope; }
    // Устанавливает доверенного автора рабочего экземпляра и применяет право декларации.
    public async Task<bool> CanOpenAsync(ModuleDefinition definition, Guid? contextId, CancellationToken ct)
    {
        var identity = await _users.GetCurrentUserAsync(ct);
        if (identity?.IsAuthenticated == true) _scope.SetUser((await _users.EnsureCurrentUserAsync(ct))?.Id);
        if (string.IsNullOrEmpty(definition.RequiredPermission)) return true;
        if (identity?.IsAuthenticated != true) return false;
        if (definition.RequiredPermission == "Authenticated") return true;
        if (identity.IsAkadmin || identity.IsGeneralAdmin) return true;
        var rights = await _users.GetCurrentUserPermissionsForSpaceAsync(contextId ?? _space.CurrentSpaceId ?? Guid.Empty, ct);
        return definition.RequiredPermission switch
        {
            "GlobalAdmin" => rights.CanGlobalAdmin,
            "Administration" or "AdminSpace" => rights.CanGlobalAdmin || rights.CanAdminSpace,
            _ => throw new InvalidOperationException($"Неизвестное право модуля '{definition.RequiredPermission}'.")
        };
    }
}

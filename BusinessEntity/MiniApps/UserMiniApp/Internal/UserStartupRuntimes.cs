using BusinessEntity.Services;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
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
    private readonly UserDefaultsService UserDefaultsService;
    // Не использует собственный connector для вызова своего сервиса.
    public UserStartupRuntime(DbContextOptions<UserMiniAppDbContext> options, ApplicationStartupState state, UserDefaultsService userDefaultsService)
    { _options = options; _state = state; UserDefaultsService = userDefaultsService; }
    // Schema проверяется раньше первого чтения локальных прав и пользователей.
    public async Task StartAsync(CancellationToken ct)
    {
        await using var db = new UserMiniAppDbContext(_options);
        await UserMiniAppStorageSchema.InitializeAsync(db, _state.FreshEntityStorage, ct);
        await UserDefaultsService.EnsureSystemDefaultsAsync(ct);
    }
    // Scoped ресурсы освобождает каталог.
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
// Реализация общей проверки открытия принадлежит владельцу прав.
internal sealed class UserModuleAccessPolicy : IModuleAccessPolicy
{
    private readonly UserAccessService UserAccessService;
    private readonly UserIdentityService UserIdentityService;
    private readonly IUserContextService _space;
    private readonly BusRequestScope _scope;
    // Проверяет identity напрямую внутри User, без самозапроса по шине.
    public UserModuleAccessPolicy(UserAccessService userAccessService, UserIdentityService userIdentityService, IUserContextService space, BusRequestScope scope)
    { UserAccessService = userAccessService; UserIdentityService = userIdentityService; _space = space; _scope = scope; }
    // Устанавливает доверенного автора рабочего экземпляра и применяет право декларации.
    public async Task<bool> CanOpenAsync(ModuleDefinition definition, Guid? contextId, CancellationToken ct)
    {
        var identity = await UserIdentityService.GetCurrentUserAsync(ct);
        if (identity?.IsAuthenticated == true)
        {
            _scope.SetUser((await UserIdentityService.EnsureCurrentUserAsync(ct))?.Id);
            _scope.SetApplicationAdminAuthority(UserValueRules.IsAccessAdmin(identity));
        }
        else _scope.SetUser(null);
        if (string.IsNullOrEmpty(definition.RequiredPermission)) return true;
        if (definition.RequiredPermission is "ReadContent" or "EditContent" or "PublishContent" or "DeleteContent")
        {
            if (!contextId.HasValue) return false;
            var content = await UserAccessService.GetContentAccessForEntityIdAsync(contextId.Value, ct);
            return definition.RequiredPermission switch
            {
                "ReadContent" => content.CanRead,
                "EditContent" => content.CanEditDraft,
                "PublishContent" => content.CanPublishDraft,
                "DeleteContent" => content.IsAccessAdmin || (await UserAccessService.GetCurrentUserPermissionsForEntityAsync(contextId.Value, ct)).CanAdminItems,
                _ => false
            };
        }
        if (identity?.IsAuthenticated != true) return false;
        if (definition.RequiredPermission == "Authenticated") return true;
        if (identity.IsAkadmin || identity.IsGeneralAdmin) return true;
        if (definition.RequiredPermission == "AdminItems")
        {
            var itemRights = contextId.HasValue ? await UserAccessService.GetCurrentUserPermissionsForEntityAsync(contextId.Value, ct)
                : await UserAccessService.GetCurrentUserPermissionsForSpaceAsync(_space.CurrentSpaceId ?? Guid.Empty, ct);
            return itemRights.CanAdminItems || itemRights.CanAdminSpace || itemRights.CanGlobalAdmin;
        }
        var rights = await UserAccessService.GetCurrentUserPermissionsForSpaceAsync(contextId ?? _space.CurrentSpaceId ?? Guid.Empty, ct);
        return definition.RequiredPermission switch
        {
            "GlobalAdmin" => rights.CanGlobalAdmin,
            "Administration" or "AdminSpace" => rights.CanGlobalAdmin || rights.CanAdminSpace,
            _ => throw new InvalidOperationException($"Неизвестное право модуля '{definition.RequiredPermission}'.")
        };
    }
}

using static BusinessEntity.MiniApps.UserMiniApp.Internal.UserValueRules;
using BusinessEntity.Services;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using System.Text.Json;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Выполняет startup-bootstrap стартовых администраторов внутри границы UserMiniApp.
internal sealed class UserMiniAppStartupAdminBootstrapper
{
    private const string SystemAdminRoleName = "Админ";
    private const string StartupAkadminUserName = "akadmin";
    private const string StartupAkadminPassword = "akadmin";
    private const string StartupAdminUserName = "admin";
    private const string StartupAdminPassword = "admin";
    private const string StartupAdminMarkerGroupName = "BusinessEntityAdmins";

    private readonly UserBootstrapLocalStore _local;
    private readonly AuthentikManagementClient _authentikManagementClient;
    private readonly AuthentikSessionManager _authentikSessionManager;
    private readonly ILogger<UserMiniAppStartupAdminBootstrapper> _logger;
    private readonly bool _bootstrapRequired;

    // Получает Authentik API client и repositories UserMiniApp для startup-синхронизации администраторов.
    public UserMiniAppStartupAdminBootstrapper(
        UserBootstrapLocalStore local,
        AuthentikManagementClient authentikManagementClient,
        AuthentikSessionManager authentikSessionManager,
        ILogger<UserMiniAppStartupAdminBootstrapper> logger,
        IConfiguration configuration)
    {
        _local = local;
        _authentikManagementClient = authentikManagementClient;
        _authentikSessionManager = authentikSessionManager;
        _logger = logger;
        _bootstrapRequired = configuration.GetValue<bool>("EnsureAuthentikOnStartup");
    }

    // Гарантирует наличие стартовых администраторов в Authentik и локальном user-storage.
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (!_authentikManagementClient.IsConfigured)
        {
            if (_bootstrapRequired)
            {
                throw new InvalidOperationException("Authentik Admin API token is required for startup bootstrap.");
            }

            _logger.LogWarning("Startup admin users bootstrap skipped: Authentik Admin API token is not configured.");
            return;
        }

        try
        {
            var applicationGroupNames = BuildApplicationGroupNames();
            var adminMarkerGroupName = ResolveAdminMarkerGroupName();
            var adminMarkerGroup = await _authentikManagementClient.EnsureGroupAsync(
                adminMarkerGroupName,
                cancellationToken);
            var akadmin = await _authentikManagementClient.EnsureInternalUserAsync(
                StartupAkadminUserName,
                StartupAkadminPassword,
                applicationGroupNames,
                cancellationToken);
            var admin = await EnsureStartupAdminAsync(
                applicationGroupNames,
                adminMarkerGroup,
                cancellationToken);

            await EnsureAuthentikBootstrapPasswordAsync(akadmin, StartupAkadminPassword, cancellationToken);

            await _local.EnsureAsync(akadmin, admin, adminMarkerGroupName, cancellationToken);
            _logger.LogInformation("Startup admin users bootstrap completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup admin users bootstrap failed.");
            // Автоматическая установка не должна объявлять готовность без стартовых администраторов.
            if (_bootstrapRequired || cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }
    }

    // Возвращает startup-admin, создавая его только при отсутствии или добавляя в Authentik admin-group.
    private async Task<AuthentikUserRecord> EnsureStartupAdminAsync(
        IReadOnlyList<string> applicationGroupNames,
        AuthentikGroupRecord adminMarkerGroup,
        CancellationToken cancellationToken)
    {
        var admin = await _authentikManagementClient.FindInternalUserByUsernameAsync(
            StartupAdminUserName,
            cancellationToken);
        if (admin == null)
        {
            return await _authentikManagementClient.EnsureInternalUserAsync(
                StartupAdminUserName,
                StartupAdminPassword,
                applicationGroupNames.Append(adminMarkerGroup.Name),
                cancellationToken);
        }

        if (await _authentikManagementClient.IsInternalUserInGroupAsync(
                StartupAdminUserName,
                adminMarkerGroup.Name,
                cancellationToken))
        {
            return admin;
        }

        await _authentikManagementClient.EnsureUserInGroupAsync(
            adminMarkerGroup.Pk,
            admin.Pk,
            cancellationToken);
        return admin;
    }

    // Проверяет startup-пароль через Authentik password-flow и ставит его, если он не проходит.
    private async Task EnsureAuthentikBootstrapPasswordAsync(
        AuthentikUserRecord user,
        string password,
        CancellationToken cancellationToken)
    {
        if (await _authentikSessionManager.ValidatePasswordAsync(user.Username, password, cancellationToken))
        {
            return;
        }

        await _authentikManagementClient.SetPasswordAsync(user.Pk, password, cancellationToken);
    }

    // Возвращает список Authentik-групп, через которые пользователь считается пользователем приложения.
    private IReadOnlyList<string> BuildApplicationGroupNames()
    {
        var groupName = NormalizeOptionalText(_authentikManagementClient.ApplicationUsersGroupName);
        return string.IsNullOrWhiteSpace(groupName)
            ? Array.Empty<string>()
            : new[] { groupName };
    }

    // Возвращает имя группы-маркера общего административного доступа.
    private string ResolveAdminMarkerGroupName()
    {
        var groupName = NormalizeOptionalText(_authentikManagementClient.GeneralAdminsGroupName);
        return string.IsNullOrWhiteSpace(groupName) ? StartupAdminMarkerGroupName : groupName;
    }

    // Создает или обновляет локальную DTO по записи Authentik.


    // Создает или возвращает локальную группу администраторов приложения.


    // Добавляет локального пользователя в локальную группу идемпотентно.


    // Назначает локальной admin-группе системную роль Админ на все пространства.


    // Находит локального пользователя по стабильным Authentik-идентификаторам.


    // Собирает payload локального пользователя по записи Authentik.


    // Проверяет обязательный payload существующей локальной учётной записи.


    // Сериализует payload локального пользователя едиными JSON options UserMiniApp.


    // Проверяет равенство локальных payload-данных пользователя.


    // Нормализует обязательное короткое текстовое поле.


    // Нормализует пробелы в коротком текстовом поле.

}

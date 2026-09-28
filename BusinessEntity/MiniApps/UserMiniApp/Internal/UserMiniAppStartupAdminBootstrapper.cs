using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.Services;
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

    private readonly AuthentikManagementClient _authentikManagementClient;
    private readonly AuthentikSessionManager _authentikSessionManager;
    private readonly IUserMiniAppRepository<UserDto> _userRepository;
    private readonly IUserMiniAppRepository<UserRoleDto> _roleRepository;
    private readonly IUserMiniAppRepository<UserGroupDto> _groupRepository;
    private readonly IUserMiniAppRepository<UserGroupMemberDto> _groupMemberRepository;
    private readonly IUserMiniAppRepository<UserRoleAssignmentDto> _roleAssignmentRepository;
    private readonly ILogger<UserMiniAppStartupAdminBootstrapper> _logger;
    private readonly bool _bootstrapRequired;

    // Получает Authentik API client и repositories UserMiniApp для startup-синхронизации администраторов.
    public UserMiniAppStartupAdminBootstrapper(
        AuthentikManagementClient authentikManagementClient,
        AuthentikSessionManager authentikSessionManager,
        IUserMiniAppRepository<UserDto> userRepository,
        IUserMiniAppRepository<UserRoleDto> roleRepository,
        IUserMiniAppRepository<UserGroupDto> groupRepository,
        IUserMiniAppRepository<UserGroupMemberDto> groupMemberRepository,
        IUserMiniAppRepository<UserRoleAssignmentDto> roleAssignmentRepository,
        ILogger<UserMiniAppStartupAdminBootstrapper> logger,
        IConfiguration configuration)
    {
        _authentikManagementClient = authentikManagementClient;
        _authentikSessionManager = authentikSessionManager;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _groupRepository = groupRepository;
        _groupMemberRepository = groupMemberRepository;
        _roleAssignmentRepository = roleAssignmentRepository;
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

            var localUsers = (await _userRepository.GetAllAsync(null, cancellationToken)).ToList();
            await UpsertLocalUserFromAuthentikAsync(akadmin, localUsers, cancellationToken);
            var localAdmin = await UpsertLocalUserFromAuthentikAsync(admin, localUsers, cancellationToken);
            var localAdminGroup = await EnsureLocalAdminGroupAsync(adminMarkerGroupName, cancellationToken);

            await EnsureLocalGroupMemberAsync(localAdmin.Id, localAdminGroup.Id, cancellationToken);
            await EnsureLocalAdminRoleAssignmentAsync(localAdminGroup.Id, cancellationToken);
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
    private async Task<UserDto> UpsertLocalUserFromAuthentikAsync(
        AuthentikUserRecord authentikUser,
        List<UserDto> localUsers,
        CancellationToken cancellationToken)
    {
        var localUser = FindLocalUser(authentikUser, localUsers);
        if (localUser == null)
        {
            var now = DateTime.UtcNow;
            var created = new UserDto
            {
                Id = Guid.NewGuid(),
                ExternalId = authentikUser.Uid,
                Payload = SerializeUserData(BuildUserData(authentikUser, authentikUser.Username)),
                DateCreated = now,
                DateLastModified = now
            };

            created = await _userRepository.AddAsync(created, cancellationToken);
            localUsers.Add(created);
            return created;
        }

        var currentData = ReadUserData(localUser);
        var currentDisplayedName = NormalizeOptionalText(currentData.DisplayedName);
        var previousLogin = NormalizeOptionalText(currentData.AuthentikLogin);
        var displayedName = string.IsNullOrWhiteSpace(currentDisplayedName) ||
                            string.Equals(currentDisplayedName, previousLogin, StringComparison.Ordinal)
            ? authentikUser.Username
            : currentDisplayedName;
        var nextData = BuildUserData(authentikUser, displayedName);
        var shouldUpdate =
            !string.Equals(localUser.ExternalId, authentikUser.Uid, StringComparison.Ordinal) ||
            !UserDataEquals(currentData, nextData);

        if (!shouldUpdate)
        {
            return localUser;
        }

        localUser.ExternalId = authentikUser.Uid;
        localUser.Payload = SerializeUserData(nextData);
        localUser.DateLastModified = DateTime.UtcNow;
        await _userRepository.UpdateAsync(localUser, cancellationToken);
        return localUser;
    }

    // Создает или возвращает локальную группу администраторов приложения.
    private async Task<UserGroupDto> EnsureLocalAdminGroupAsync(
        string groupName,
        CancellationToken cancellationToken)
    {
        groupName = NormalizeRequiredText(groupName, "Имя группы администраторов");
        var groups = await _groupRepository.GetAllAsync(null, cancellationToken);
        var existingGroup = groups
            .OrderBy(group => group.DateCreated)
            .FirstOrDefault(group => string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (existingGroup == null)
        {
            var now = DateTime.UtcNow;
            return await _groupRepository.AddAsync(
                new UserGroupDto
                {
                    Id = Guid.NewGuid(),
                    Name = groupName,
                    DateCreated = now,
                    DateLastModified = now
                },
                cancellationToken);
        }

        if (string.Equals(existingGroup.Name, groupName, StringComparison.Ordinal))
        {
            return existingGroup;
        }

        existingGroup.Name = groupName;
        existingGroup.DateLastModified = DateTime.UtcNow;
        await _groupRepository.UpdateAsync(existingGroup, cancellationToken);
        return existingGroup;
    }

    // Добавляет локального пользователя в локальную группу идемпотентно.
    private async Task EnsureLocalGroupMemberAsync(
        Guid userId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || groupId == Guid.Empty)
        {
            return;
        }

        var existingMember = (await _groupMemberRepository.GetAllAsync(
                member => member.UserId == userId && member.GroupId == groupId,
                cancellationToken))
            .FirstOrDefault();
        if (existingMember != null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        await _groupMemberRepository.AddAsync(
            new UserGroupMemberDto
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GroupId = groupId,
                DateCreated = now,
                DateLastModified = now
            },
            cancellationToken);
    }

    // Назначает локальной admin-группе системную роль Админ на все пространства.
    private async Task EnsureLocalAdminRoleAssignmentAsync(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        if (groupId == Guid.Empty)
        {
            return;
        }

        var adminRole = (await _roleRepository.GetAllAsync(
                role => role.Name == SystemAdminRoleName,
                cancellationToken))
            .OrderBy(role => role.DateCreated)
            .FirstOrDefault();
        if (adminRole == null)
        {
            throw new InvalidOperationException("Системная роль Админ не найдена.");
        }

        var existingAssignment = (await _roleAssignmentRepository.GetAllAsync(
                assignment =>
                    assignment.SpaceId == Guid.Empty &&
                    assignment.SubjectId == groupId &&
                    assignment.AssignmentType == UserRoleAssignmentTypes.GroupToRole &&
                    assignment.RoleId == adminRole.Id,
                cancellationToken))
            .OrderBy(assignment => assignment.DateCreated)
            .FirstOrDefault();
        if (existingAssignment != null)
        {
            if (existingAssignment.Subject != UserRoleAssignmentSubjects.AllSpaces)
            {
                existingAssignment.Subject = UserRoleAssignmentSubjects.AllSpaces;
                existingAssignment.DateLastModified = DateTime.UtcNow;
                await _roleAssignmentRepository.UpdateAsync(existingAssignment, cancellationToken);
            }

            return;
        }

        var now = DateTime.UtcNow;
        await _roleAssignmentRepository.AddAsync(
            new UserRoleAssignmentDto
            {
                Id = Guid.NewGuid(),
                SpaceId = Guid.Empty,
                Subject = UserRoleAssignmentSubjects.AllSpaces,
                SubjectId = groupId,
                AssignmentType = UserRoleAssignmentTypes.GroupToRole,
                RoleId = adminRole.Id,
                DateCreated = now,
                DateLastModified = now
            },
            cancellationToken);
    }

    // Находит локального пользователя по стабильным Authentik-идентификаторам.
    private static UserDto? FindLocalUser(
        AuthentikUserRecord authentikUser,
        IEnumerable<UserDto> localUsers)
    {
        return localUsers.FirstOrDefault(user => string.Equals(user.ExternalId, authentikUser.Uid, StringComparison.OrdinalIgnoreCase))
               ?? localUsers.FirstOrDefault(user =>
               {
                   var userData = ReadUserData(user);
                   return userData.AuthentikUserPk == authentikUser.Pk ||
                          string.Equals(userData.AuthentikUserUuid, authentikUser.Uuid, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(userData.ExtId, authentikUser.Uid, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(userData.AuthentikLogin, authentikUser.Username, StringComparison.OrdinalIgnoreCase);
               });
    }

    // Собирает payload локального пользователя по записи Authentik.
    private static UserData BuildUserData(AuthentikUserRecord authentikUser, string displayedName)
    {
        return new UserData
        {
            AuthentikUserPk = authentikUser.Pk,
            AuthentikUserUuid = authentikUser.Uuid,
            AuthentikLogin = authentikUser.Username,
            DisplayedName = displayedName,
            ExtId = authentikUser.Uid
        };
    }

    // Читает UserData из локального payload tolerant-способом.
    private static UserData ReadUserData(UserDto user)
    {
        if (string.IsNullOrWhiteSpace(user.Payload))
        {
            return new UserData { ExtId = user.ExternalId };
        }

        try
        {
            var userData = JsonSerializer.Deserialize<UserData>(user.Payload, UserMiniAppJsonOptions.Default)
                           ?? new UserData { ExtId = user.ExternalId };
            userData.ExtId = string.IsNullOrWhiteSpace(userData.ExtId) ? user.ExternalId : userData.ExtId;
            return userData;
        }
        catch (JsonException)
        {
            return new UserData { ExtId = user.ExternalId };
        }
    }

    // Сериализует payload локального пользователя едиными JSON options UserMiniApp.
    private static string SerializeUserData(UserData userData)
    {
        return JsonSerializer.Serialize(userData, UserMiniAppJsonOptions.Default);
    }

    // Проверяет равенство локальных payload-данных пользователя.
    private static bool UserDataEquals(UserData left, UserData right)
    {
        return left.AuthentikUserPk == right.AuthentikUserPk &&
               string.Equals(left.AuthentikUserUuid, right.AuthentikUserUuid, StringComparison.Ordinal) &&
               string.Equals(left.AuthentikLogin, right.AuthentikLogin, StringComparison.Ordinal) &&
               string.Equals(left.DisplayedName, right.DisplayedName, StringComparison.Ordinal) &&
               string.Equals(left.ExtId, right.ExtId, StringComparison.Ordinal);
    }

    // Нормализует обязательное короткое текстовое поле.
    private static string NormalizeRequiredText(string? value, string fieldName)
    {
        var normalized = NormalizeOptionalText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{fieldName} не может быть пустым.", fieldName);
        }

        return normalized;
    }

    // Нормализует пробелы в коротком текстовом поле.
    private static string NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
    }
}

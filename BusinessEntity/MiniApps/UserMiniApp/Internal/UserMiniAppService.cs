using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.Services;
using System.Text.Json;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal
{
    // Инкапсулирует основную логику получения и кэширования текущего пользователя в пределах scope.
    internal sealed class UserMiniAppService
    {
        private const int MaxBookmarkTextLength = 500;
        private const int MaxBookmarkLabelLength = 80;
        private const int MaxPrintPresetNameLength = 80;
        private const string SystemAdminRoleName = "Админ";
        private const string GuestRoleName = "Гость";
        private const string ReadersRoleName = "Ридерс";
        private const string SystemAnonymousExternalId = "system-anonymous";
        private const string SystemAnonymousDisplayName = "Анонимус";
        private const string SystemAnonymousUserIdText = "00000000-0000-0000-0000-000000000002";
        private const string AllSpacesDisplayName = "[ВсеПространства]";

        private readonly UserMiniAppState _state;
        private readonly UserIdentitySession _identitySession;
        private readonly AuthentikManagementClient _authentikManagementClient;
        private readonly UserMiniAppStartupAdminBootstrapper _startupAdminBootstrapper;
        private readonly IUserMiniAppRepository<UserDto> _userRepository;
        private readonly IUserMiniAppRepository<UserPropertyDto> _userPropertyRepository;
        private readonly IUserMiniAppRepository<UserRoleDto> _roleRepository;
        private readonly IUserMiniAppRepository<UserGroupDto> _groupRepository;
        private readonly IUserMiniAppRepository<UserGroupMemberDto> _groupMemberRepository;
        private readonly IUserMiniAppRepository<UserRoleAssignmentDto> _roleAssignmentRepository;
        private readonly IDataProviderConnector _data;
        private readonly IUserContextService _userContextService;
        private readonly UserSpaceContentAccessHelper _spaceContentAccessHelper;

        // Получает state mini-app и фабрику, которая умеет собирать пользователя из Authentik principal.
        public UserMiniAppService(
            UserMiniAppState state,
            UserIdentitySession identitySession,
            AuthentikManagementClient authentikManagementClient,
            UserMiniAppStartupAdminBootstrapper startupAdminBootstrapper,
            IUserMiniAppRepository<UserDto> userRepository,
            IUserMiniAppRepository<UserPropertyDto> userPropertyRepository,
            IUserMiniAppRepository<UserRoleDto> roleRepository,
            IUserMiniAppRepository<UserGroupDto> groupRepository,
            IUserMiniAppRepository<UserGroupMemberDto> groupMemberRepository,
            IUserMiniAppRepository<UserRoleAssignmentDto> roleAssignmentRepository,
            IDataProviderConnector dataProvider,
            IUserContextService userContextService,
            UserSpaceContentAccessHelper spaceContentAccessHelper)
        {
            _state = state;
            _identitySession = identitySession;
            _authentikManagementClient = authentikManagementClient;
            _startupAdminBootstrapper = startupAdminBootstrapper;
            _userRepository = userRepository;
            _userPropertyRepository = userPropertyRepository;
            _roleRepository = roleRepository;
            _groupRepository = groupRepository;
            _groupMemberRepository = groupMemberRepository;
            _roleAssignmentRepository = roleAssignmentRepository;
            _data = dataProvider;
            _userContextService = userContextService;
            _spaceContentAccessHelper = spaceContentAccessHelper;
        }

        // Возвращает пользователя из state или один раз строит его через factory.
        public Task<BusinessEntityUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
            => _identitySession.GetCurrentUserAsync(cancellationToken);

        // Гарантирует наличие всех системных записей UserMiniApp.
        public async Task EnsureSystemDefaultsAsync(CancellationToken cancellationToken = default)
        {
            await EnsureSystemRolesAsync(cancellationToken);
            await EnsureSystemUsersAsync(cancellationToken);
            await _startupAdminBootstrapper.EnsureAsync(cancellationToken);
        }

        // Гарантирует наличие базовых ролей матрицы доступа.
        public async Task EnsureSystemRolesAsync(CancellationToken cancellationToken = default)
        {
            var roles = (await _roleRepository.GetAllAsync(null, cancellationToken)).ToList();
            await EnsureRoleAsync(roles, SystemAdminRoleName, BuildAllPermissionString(), isSystem: true, cancellationToken);
            await EnsureRoleAsync(roles, GuestRoleName, BuildReadPublishedPermissionString(), isSystem: false, cancellationToken);
            await EnsureRoleAsync(roles, ReadersRoleName, BuildReadPublishedPermissionString(), isSystem: false, cancellationToken);
        }

        // Гарантирует наличие системного пользователя anonymous без Authentik-учетки.
        public async Task EnsureSystemUsersAsync(CancellationToken cancellationToken = default)
        {
            await EnsureAnonymousUserAsync(cancellationToken);
        }

        public async Task<UserDto?> EnsureCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var currentUser = await GetCurrentUserAsync(cancellationToken);
            if (currentUser?.IsAuthenticated != true)
            {
                return null;
            }

            var externalId = currentUser.GetNameIdentifier();
            if (string.IsNullOrWhiteSpace(externalId))
            {
                externalId = currentUser.UserId;
            }

            if (string.IsNullOrWhiteSpace(externalId))
            {
                return null;
            }

            var existingUser = (await _userRepository.GetAllAsync(
                    user => user.ExternalId == externalId,
                    cancellationToken))
                .OrderBy(user => user.DateCreated)
                .FirstOrDefault();

            var authentikLogin = string.IsNullOrWhiteSpace(currentUser.UserName)
                ? externalId
                : currentUser.UserName;
            var authentikUserPk = ResolveAuthentikUserPk(currentUser);

            if (existingUser != null)
            {
                var userData = ReadUserData(existingUser);
                var shouldUpdate = false;

                if (string.IsNullOrWhiteSpace(userData.ExtId))
                {
                    userData.ExtId = externalId;
                    shouldUpdate = true;
                }

                if (string.IsNullOrWhiteSpace(userData.AuthentikLogin))
                {
                    userData.AuthentikLogin = authentikLogin;
                    shouldUpdate = true;
                }

                if (string.IsNullOrWhiteSpace(userData.DisplayedName))
                {
                    userData.DisplayedName = authentikLogin;
                    shouldUpdate = true;
                }

                if (userData.AuthentikUserPk <= 0 && authentikUserPk > 0)
                {
                    userData.AuthentikUserPk = authentikUserPk;
                    shouldUpdate = true;
                }

                if (shouldUpdate)
                {
                    existingUser.Payload = SerializeUserData(userData);
                    existingUser.DateLastModified = DateTime.UtcNow;
                    await _userRepository.UpdateAsync(existingUser, cancellationToken);
                }

                return existingUser;
            }

            var now = DateTime.UtcNow;
            var created = new UserDto
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Payload = SerializeUserData(new UserData
                {
                    AuthentikUserPk = authentikUserPk,
                    AuthentikLogin = authentikLogin,
                    DisplayedName = authentikLogin,
                    ExtId = externalId
                }),
                DateCreated = now,
                DateLastModified = now
            };

            return await _userRepository.AddAsync(created, cancellationToken);
        }

        // Создает или чинит локальную запись anonymous-пользователя.
        private async Task<UserDto> EnsureAnonymousUserAsync(CancellationToken cancellationToken)
        {
            var users = await _userRepository.GetAllAsync(null, cancellationToken);
            var existing = users
                .OrderBy(user => user.DateCreated)
                .FirstOrDefault(IsAnonymousUser);
            var now = DateTime.UtcNow;

            if (existing != null)
            {
                var anonymousData = ReadUserData(existing);
                var shouldUpdate =
                    !string.Equals(existing.ExternalId, SystemAnonymousExternalId, StringComparison.Ordinal) ||
                    !string.Equals(anonymousData.AuthentikLogin, SystemAnonymousExternalId, StringComparison.Ordinal) ||
                    !string.Equals(anonymousData.DisplayedName, SystemAnonymousDisplayName, StringComparison.Ordinal) ||
                    !string.Equals(anonymousData.ExtId, SystemAnonymousExternalId, StringComparison.Ordinal) ||
                    anonymousData.AuthentikUserPk != 0 ||
                    !string.IsNullOrWhiteSpace(anonymousData.AuthentikUserUuid);

                if (!shouldUpdate)
                {
                    return existing;
                }

                existing.ExternalId = SystemAnonymousExternalId;
                existing.Payload = SerializeUserData(BuildAnonymousUserData());
                existing.DateLastModified = now;
                await _userRepository.UpdateAsync(existing, cancellationToken);
                return existing;
            }

            var anonymousId = Guid.Parse(SystemAnonymousUserIdText);
            if (users.Any(user => user.Id == anonymousId))
            {
                anonymousId = Guid.NewGuid();
            }

            return await _userRepository.AddAsync(
                new UserDto
                {
                    Id = anonymousId,
                    ExternalId = SystemAnonymousExternalId,
                    Payload = SerializeUserData(BuildAnonymousUserData()),
                    DateCreated = now,
                    DateLastModified = now
                },
                cancellationToken);
        }

        // Удаляет локальную запись текущего Authentik-пользователя и все привязанные user properties.
        public async Task<bool> DeleteCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var currentUser = await GetCurrentUserAsync(cancellationToken);
            if (currentUser?.IsAuthenticated != true)
            {
                return false;
            }

            var externalId = ResolveExternalId(currentUser);
            if (string.IsNullOrWhiteSpace(externalId))
            {
                return false;
            }

            var users = (await _userRepository.GetAllAsync(
                    user => user.ExternalId == externalId,
                    cancellationToken))
                .OrderBy(user => user.DateCreated)
                .ToList();

            if (users.Count == 0)
            {
                return false;
            }

            foreach (var user in users)
            {
                var properties = await _userPropertyRepository.GetAllAsync(
                    property => property.ParentEntityId == user.Id,
                    cancellationToken);

                foreach (var property in properties)
                {
                    await _userPropertyRepository.DeleteAsync(property.Id, cancellationToken);
                }

                var memberships = await _groupMemberRepository.GetAllAsync(
                    membership => membership.UserId == user.Id,
                    cancellationToken);

                foreach (var membership in memberships)
                {
                    await _groupMemberRepository.DeleteAsync(membership.Id, cancellationToken);
                }

                var roleAssignments = await _roleAssignmentRepository.GetAllAsync(
                    assignment => assignment.SubjectId == user.Id &&
                                  assignment.AssignmentType == UserRoleAssignmentTypes.UserToRole,
                    cancellationToken);

                foreach (var assignment in roleAssignments)
                {
                    await _roleAssignmentRepository.DeleteAsync(assignment.Id, cancellationToken);
                }

                await _userRepository.DeleteAsync(user.Id, cancellationToken);
            }

            return true;
        }

        // Возвращает административный список пользователей только из локальной таблицы Users.
        public async Task<IReadOnlyList<UserAdministrationRecord>> GetAdministrationUsersAsync(CancellationToken cancellationToken = default)
        {
            await EnsureSystemUsersAsync(cancellationToken);
            var localUsers = await _userRepository.GetAllAsync(null, cancellationToken);
            return SortAdministrationRecords(localUsers.Select(MapLocalAdministrationRecord));
        }

        // Читает пользователей приложения из Authentik и материализует их локальные DTO.
        public async Task<IReadOnlyList<UserAdministrationRecord>> ReadAdministrationUsersFromAuthentikAsync(
            CancellationToken cancellationToken = default)
        {
            var authentikUsers = await _authentikManagementClient.GetApplicationUsersAsync(cancellationToken);
            var localUsers = (await _userRepository.GetAllAsync(null, cancellationToken)).ToList();
            var records = new List<UserAdministrationRecord>();

            foreach (var authentikUser in authentikUsers)
            {
                var localUser = await UpsertLocalUserFromAuthentikAsync(authentikUser, localUsers, cancellationToken);
                records.Add(MapAdministrationRecord(localUser, authentikUser));
            }

            var sortedRecords = SortAdministrationRecords(records);
            SetAdministrationUsersCache(sortedRecords, authentikUsers);
            return await GetAdministrationUsersAsync(cancellationToken);
        }

        // Создает пользователя в Authentik и материализует локальную DTO без повторного чтения списка Authentik.
        public async Task<UserAdministrationRecord> CreateAdministrationUserAsync(CancellationToken cancellationToken = default)
        {
            var localUsers = (await _userRepository.GetAllAsync(null, cancellationToken)).ToList();
            var reservedUsernames = localUsers
                .Select(user => ReadUserData(user).AuthentikLogin)
                .Concat(_state.AuthentikApplicationUsers.Select(user => user.Username));
            var createdAuthentikUser = await _authentikManagementClient.CreateApplicationUserAsync(
                reservedUsernames,
                cancellationToken);
            var localUser = await UpsertLocalUserFromAuthentikAsync(createdAuthentikUser, localUsers, cancellationToken);
            var record = MapAdministrationRecord(localUser, createdAuthentikUser);
            UpsertAdministrationUserCache(record, createdAuthentikUser);
            return record;
        }

        // Обновляет Authentik username и пароль при необходимости, затем сохраняет локальное отображаемое имя.
        public async Task<UserAdministrationRecord> UpdateAdministrationUserAsync(
            Guid userId,
            UserAdministrationSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentException("Пользователь не выбран.", nameof(userId));
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                throw new KeyNotFoundException("Пользователь не найден.");
            }

            if (IsSystemUser(user))
            {
                throw new InvalidOperationException("Системного пользователя нельзя редактировать.");
            }

            var authentikLogin = NormalizeRequiredText(request.AuthentikLogin, "Логин в аутентик");
            var displayedName = NormalizeOptionalText(request.DisplayedName);
            if (string.IsNullOrWhiteSpace(displayedName))
            {
                displayedName = authentikLogin;
            }

            var userData = ReadUserData(user);
            var authentikUser = await ResolveCachedOrStoredAuthentikUserAsync(
                userData,
                user.ExternalId,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                await _authentikManagementClient.SetPasswordAsync(
                    authentikUser.Pk,
                    request.Password,
                    cancellationToken);
            }

            if (!string.Equals(authentikUser.Username, authentikLogin, StringComparison.Ordinal))
            {
                authentikUser = await _authentikManagementClient.UpdateUsernameAsync(
                    authentikUser.Pk,
                    authentikLogin,
                    cancellationToken);
            }

            user.ExternalId = authentikUser.Uid;
            user.Payload = SerializeUserData(new UserData
            {
                AuthentikUserPk = authentikUser.Pk,
                AuthentikUserUuid = authentikUser.Uuid,
                AuthentikLogin = authentikUser.Username,
                DisplayedName = displayedName,
                ExtId = authentikUser.Uid
            });
            user.DateLastModified = DateTime.UtcNow;

            await _userRepository.UpdateAsync(user, cancellationToken);
            var record = MapAdministrationRecord(user, authentikUser);
            UpsertAdministrationUserCache(record, authentikUser);
            return record;
        }

        // Удаляет Authentik-пользователя, затем локальную user-запись и все ее user properties.
        public async Task<bool> DeleteAdministrationUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
            {
                return false;
            }

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
            {
                return false;
            }

            if (IsSystemUser(user))
            {
                throw new InvalidOperationException("Системного пользователя нельзя удалить.");
            }

            var userData = ReadUserData(user);
            var authentikUser = await ResolveCachedOrStoredAuthentikUserAsync(
                userData,
                user.ExternalId,
                cancellationToken);
            await _authentikManagementClient.DeleteUserAsync(authentikUser.Pk, cancellationToken);

            var properties = await _userPropertyRepository.GetAllAsync(
                property => property.ParentEntityId == userId,
                cancellationToken);

            // User properties подчинены UserDto и не должны оставаться orphan-строками.
            foreach (var property in properties)
            {
                await _userPropertyRepository.DeleteAsync(property.Id, cancellationToken);
            }

            var memberships = await _groupMemberRepository.GetAllAsync(
                membership => membership.UserId == userId,
                cancellationToken);

            foreach (var membership in memberships)
            {
                await _groupMemberRepository.DeleteAsync(membership.Id, cancellationToken);
            }

            var roleAssignments = await _roleAssignmentRepository.GetAllAsync(
                assignment => assignment.SubjectId == userId &&
                              assignment.AssignmentType == UserRoleAssignmentTypes.UserToRole,
                cancellationToken);

            foreach (var assignment in roleAssignments)
            {
                await _roleAssignmentRepository.DeleteAsync(assignment.Id, cancellationToken);
            }

            await _userRepository.DeleteAsync(userId, cancellationToken);
            RemoveAdministrationUserCache(userId, authentikUser.Pk);
            return true;
        }

        // Возвращает роли user mini-app для административного редактора ролей.
        public async Task<IReadOnlyList<UserRoleRecord>> GetRolesAsync(CancellationToken cancellationToken = default)
        {
            await EnsureSystemRolesAsync(cancellationToken);
            var roles = await _roleRepository.GetAllAsync(null, cancellationToken);
            return roles
                .OrderByDescending(role => role.IsSystem)
                .ThenBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
                .Select(MapRoleRecord)
                .ToList();
        }

        // Создает новую роль с уникальным именем и пустым набором прав.
        public async Task<UserRoleRecord> CreateRoleAsync(CancellationToken cancellationToken = default)
        {
            await EnsureSystemRolesAsync(cancellationToken);
            var roles = await _roleRepository.GetAllAsync(null, cancellationToken);
            var now = DateTime.UtcNow;
            var role = new UserRoleDto
            {
                Id = Guid.NewGuid(),
                Name = BuildUniqueRoleName(roles),
                Permissions = string.Empty,
                IsSystem = false,
                DateCreated = now,
                DateLastModified = now
            };

            await _roleRepository.AddAsync(role, cancellationToken);
            return MapRoleRecord(role);
        }

        // Обновляет имя и набор прав существующей пользовательской роли.
        public async Task<UserRoleRecord> UpdateRoleAsync(
            Guid roleId,
            UserRoleSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            if (roleId == Guid.Empty)
            {
                throw new ArgumentException("Роль не выбрана.", nameof(roleId));
            }

            await EnsureSystemRolesAsync(cancellationToken);
            var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken)
                       ?? throw new KeyNotFoundException("Роль не найдена.");
            if (role.IsSystem || IsBaselineRoleName(role.Name))
            {
                throw new InvalidOperationException("Базовую роль политики доступа нельзя редактировать.");
            }

            var name = NormalizeRequiredText(request.Name, "Имя роли");
            var roles = await _roleRepository.GetAllAsync(null, cancellationToken);
            var duplicate = roles.Any(existingRole =>
                existingRole.Id != roleId &&
                string.Equals(existingRole.Name, name, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                throw new ArgumentException("Роль с таким именем уже существует.", nameof(request.Name));
            }

            role.Name = name;
            role.Permissions = BuildPermissionString(request);
            role.DateLastModified = DateTime.UtcNow;
            await _roleRepository.UpdateAsync(role, cancellationToken);
            return MapRoleRecord(role);
        }

        // Удаляет пользовательскую роль, оставляя системные роли неизменными.
        public async Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
        {
            if (roleId == Guid.Empty)
            {
                return false;
            }

            await EnsureSystemRolesAsync(cancellationToken);
            var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
            if (role == null)
            {
                return false;
            }

            if (role.IsSystem || IsBaselineRoleName(role.Name))
            {
                throw new InvalidOperationException("Базовую роль политики доступа нельзя удалить.");
            }

            var assignments = await _roleAssignmentRepository.GetAllAsync(
                assignment => assignment.RoleId == roleId,
                cancellationToken);
            foreach (var assignment in assignments)
            {
                await _roleAssignmentRepository.DeleteAsync(assignment.Id, cancellationToken);
            }

            await _roleRepository.DeleteAsync(roleId, cancellationToken);
            return true;
        }

        // Возвращает группы пользователей с количеством участников.
        public async Task<IReadOnlyList<UserGroupRecord>> GetUserGroupsAsync(CancellationToken cancellationToken = default)
        {
            var groups = await _groupRepository.GetAllAsync(null, cancellationToken);
            var memberships = await _groupMemberRepository.GetAllAsync(null, cancellationToken);
            return groups
                .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => MapGroupRecord(group, memberships.Count(membership => membership.GroupId == group.Id)))
                .ToList();
        }

        // Создает новую группу пользователей с уникальным именем.
        public async Task<UserGroupRecord> CreateUserGroupAsync(CancellationToken cancellationToken = default)
        {
            var groups = await _groupRepository.GetAllAsync(null, cancellationToken);
            var now = DateTime.UtcNow;
            var group = new UserGroupDto
            {
                Id = Guid.NewGuid(),
                Name = BuildUniqueGroupName(groups),
                DateCreated = now,
                DateLastModified = now
            };

            await _groupRepository.AddAsync(group, cancellationToken);
            return MapGroupRecord(group, 0);
        }

        // Обновляет имя существующей группы пользователей.
        public async Task<UserGroupRecord> UpdateUserGroupAsync(
            Guid groupId,
            UserGroupSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            if (groupId == Guid.Empty)
            {
                throw new ArgumentException("Группа не выбрана.", nameof(groupId));
            }

            var group = await _groupRepository.GetByIdAsync(groupId, cancellationToken)
                        ?? throw new KeyNotFoundException("Группа не найдена.");
            var name = NormalizeRequiredText(request.Name, "Имя группы");
            var groups = await _groupRepository.GetAllAsync(null, cancellationToken);
            var duplicate = groups.Any(existingGroup =>
                existingGroup.Id != groupId &&
                string.Equals(existingGroup.Name, name, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                throw new ArgumentException("Группа с таким именем уже существует.", nameof(request.Name));
            }

            group.Name = name;
            group.DateLastModified = DateTime.UtcNow;
            await _groupRepository.UpdateAsync(group, cancellationToken);

            var count = (await _groupMemberRepository.GetAllAsync(
                    membership => membership.GroupId == groupId,
                    cancellationToken))
                .Count;

            return MapGroupRecord(group, count);
        }

        // Удаляет группу пользователей, если в ней нет участников.
        public async Task<bool> DeleteUserGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        {
            if (groupId == Guid.Empty)
            {
                return false;
            }

            var group = await _groupRepository.GetByIdAsync(groupId, cancellationToken);
            if (group == null)
            {
                return false;
            }

            var hasMembers = (await _groupMemberRepository.GetAllAsync(
                    membership => membership.GroupId == groupId,
                    cancellationToken))
                .Any();
            if (hasMembers)
            {
                throw new InvalidOperationException("Группу нельзя удалить, пока в ней есть пользователи.");
            }

            var assignments = await _roleAssignmentRepository.GetAllAsync(
                assignment => assignment.SubjectId == groupId &&
                              assignment.AssignmentType == UserRoleAssignmentTypes.GroupToRole,
                cancellationToken);
            foreach (var assignment in assignments)
            {
                await _roleAssignmentRepository.DeleteAsync(assignment.Id, cancellationToken);
            }

            await _groupRepository.DeleteAsync(groupId, cancellationToken);
            return true;
        }

        // Возвращает таблицу пользователей с признаком членства в выбранной группе.
        public async Task<IReadOnlyList<UserGroupMembershipRecord>> GetUserGroupMembershipsAsync(
            Guid groupId,
            CancellationToken cancellationToken = default)
        {
            await EnsureGroupExistsAsync(groupId, cancellationToken);
            await EnsureSystemUsersAsync(cancellationToken);
            var users = await _userRepository.GetAllAsync(null, cancellationToken);
            var members = await _groupMemberRepository.GetAllAsync(
                membership => membership.GroupId == groupId,
                cancellationToken);
            var memberUserIds = members
                .Select(membership => membership.UserId)
                .ToHashSet();

            return MapGroupMembershipRecords(users, memberUserIds);
        }

        // Сохраняет полный набор пользователей выбранной группы.
        public async Task<IReadOnlyList<UserGroupMembershipRecord>> UpdateUserGroupMembershipsAsync(
            Guid groupId,
            UserGroupMembershipSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            await EnsureGroupExistsAsync(groupId, cancellationToken);
            await EnsureSystemUsersAsync(cancellationToken);
            var selectedUserIds = request.UserIds
                .Where(userId => userId != Guid.Empty)
                .Distinct()
                .ToHashSet();
            var localUsers = await _userRepository.GetAllAsync(null, cancellationToken);
            var localUserIds = localUsers
                .Select(user => user.Id)
                .ToHashSet();
            var unknownUserIds = selectedUserIds
                .Where(userId => !localUserIds.Contains(userId))
                .ToList();
            if (unknownUserIds.Count > 0)
            {
                throw new ArgumentException("В списке группы есть неизвестные пользователи.", nameof(request.UserIds));
            }

            var existingMembers = (await _groupMemberRepository.GetAllAsync(
                    membership => membership.GroupId == groupId,
                    cancellationToken))
                .ToList();
            var now = DateTime.UtcNow;

            foreach (var member in existingMembers.Where(member => !selectedUserIds.Contains(member.UserId)))
            {
                await _groupMemberRepository.DeleteAsync(member.Id, cancellationToken);
            }

            var existingUserIds = existingMembers
                .Select(member => member.UserId)
                .ToHashSet();
            foreach (var userId in selectedUserIds.Where(userId => !existingUserIds.Contains(userId)))
            {
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

            return MapGroupMembershipRecords(localUsers, selectedUserIds);
        }

        // Возвращает пространства для комбобокса вкладки назначения ролей.
        public async Task<IReadOnlyList<UserSpaceRecord>> GetRoleAssignmentSpacesAsync(CancellationToken cancellationToken = default)
        {
            var currentSpaceId = _userContextService.CurrentSpaceId;
            var spaces = (await _data.GetAllAsync(cancellationToken)).Where(entity => entity.EntityType == BusinessEntityTypeEnum.Space).ToArray();
            var records = new List<UserSpaceRecord>
            {
                new()
                {
                    Id = Guid.Empty,
                    Name = AllSpacesDisplayName,
                    IsCurrent = false
                }
            };

            records.AddRange(spaces
                .OrderBy(space => space.CreatedDate)
                .ThenBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
                .Select(space => new UserSpaceRecord
                {
                    Id = space.Id,
                    Name = space.Name,
                    IsCurrent = currentSpaceId.HasValue && currentSpaceId.Value == space.Id
                }));

            return records;
        }

        // Возвращает назначения ролей для выбранного пространства.
        public async Task<IReadOnlyList<UserRoleAssignmentRecord>> GetRoleAssignmentsAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default)
        {
            var assignmentSubject = spaceId == Guid.Empty
                ? UserRoleAssignmentSubjects.AllSpaces
                : UserRoleAssignmentSubjects.Space;
            var spaceName = await GetRoleAssignmentSpaceNameAsync(spaceId, assignmentSubject, cancellationToken);
            var assignments = await _roleAssignmentRepository.GetAllAsync(
                assignment => assignment.SpaceId == spaceId &&
                              assignment.Subject == assignmentSubject,
                cancellationToken);
            var groups = await _groupRepository.GetAllAsync(null, cancellationToken);
            var users = await _userRepository.GetAllAsync(null, cancellationToken);
            var roles = await _roleRepository.GetAllAsync(null, cancellationToken);

            return MapRoleAssignmentRecords(assignments, spaceName, groups, users, roles);
        }

        // Создает назначение роли на группу или пользователя в выбранном пространстве.
        public async Task<UserRoleAssignmentRecord> CreateRoleAssignmentAsync(
            Guid spaceId,
            UserRoleAssignmentSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            var assignmentSubject = spaceId == Guid.Empty
                ? UserRoleAssignmentSubjects.AllSpaces
                : NormalizeAssignmentSubject(request.Subject);
            var assignmentSpaceId = assignmentSubject == UserRoleAssignmentSubjects.AllSpaces
                ? Guid.Empty
                : spaceId;
            await EnsureCurrentUserCanAdminRoleAssignmentsAsync(assignmentSpaceId, cancellationToken);

            var spaceName = await GetRoleAssignmentSpaceNameAsync(assignmentSpaceId, assignmentSubject, cancellationToken);
            var assignmentType = NormalizeAssignmentType(request.AssignmentType);
            await EnsureRoleExistsAsync(request.RoleId, cancellationToken);
            await EnsureAssignmentSubjectExistsAsync(request.SubjectId, assignmentType, cancellationToken);

            var existingAssignments = await _roleAssignmentRepository.GetAllAsync(
                assignment => assignment.SpaceId == assignmentSpaceId &&
                              assignment.Subject == assignmentSubject &&
                              assignment.SubjectId == request.SubjectId &&
                              assignment.AssignmentType == assignmentType &&
                              assignment.RoleId == request.RoleId,
                cancellationToken);
            var existingAssignment = existingAssignments.FirstOrDefault();
            if (existingAssignment != null)
            {
                var existingGroups = await _groupRepository.GetAllAsync(null, cancellationToken);
                var existingUsers = await _userRepository.GetAllAsync(null, cancellationToken);
                var existingRoles = await _roleRepository.GetAllAsync(null, cancellationToken);
                return MapRoleAssignmentRecords(
                        new[] { existingAssignment },
                        spaceName,
                        existingGroups,
                        existingUsers,
                        existingRoles)
                    .First();
            }

            var now = DateTime.UtcNow;
            var assignmentDto = new UserRoleAssignmentDto
            {
                Id = Guid.NewGuid(),
                SpaceId = assignmentSpaceId,
                Subject = assignmentSubject,
                SubjectId = request.SubjectId,
                AssignmentType = assignmentType,
                RoleId = request.RoleId,
                DateCreated = now,
                DateLastModified = now
            };

            await _roleAssignmentRepository.AddAsync(assignmentDto, cancellationToken);
            var groups = await _groupRepository.GetAllAsync(null, cancellationToken);
            var users = await _userRepository.GetAllAsync(null, cancellationToken);
            var roles = await _roleRepository.GetAllAsync(null, cancellationToken);
            return MapRoleAssignmentRecords(new[] { assignmentDto }, spaceName, groups, users, roles).First();
        }

        // Удаляет назначение роли по идентификатору.
        public async Task<bool> DeleteRoleAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default)
        {
            if (assignmentId == Guid.Empty)
            {
                return false;
            }

            var assignment = await _roleAssignmentRepository.GetByIdAsync(assignmentId, cancellationToken);
            if (assignment == null)
            {
                return false;
            }

            await EnsureCurrentUserCanAdminRoleAssignmentsAsync(assignment.SpaceId, cancellationToken);
            await _roleAssignmentRepository.DeleteAsync(assignmentId, cancellationToken);
            return true;
        }

        // Возвращает имена локальных групп текущего пользователя.
        public async Task<IReadOnlyList<string>> GetCurrentUserGroupNamesAsync(CancellationToken cancellationToken = default)
        {
            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Array.Empty<string>();
            }

            var memberships = await _groupMemberRepository.GetAllAsync(
                membership => membership.UserId == user.Id,
                cancellationToken);
            if (memberships.Count == 0)
            {
                return Array.Empty<string>();
            }

            var groupIds = memberships
                .Select(membership => membership.GroupId)
                .ToHashSet();
            var groups = await _groupRepository.GetAllAsync(
                group => groupIds.Contains(group.Id),
                cancellationToken);

            return groups
                .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Name)
                .ToList();
        }

        // Возвращает права текущего authenticated пользователя или anonymous fallback для публичного режима.
        public async Task<UserEffectivePermissions> GetCurrentUserPermissionsForSpaceAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default)
        {
            if (spaceId == Guid.Empty)
            {
                return UserEffectivePermissions.Deny(Guid.Empty, spaceId, isAnonymous: false);
            }

            var currentUser = await GetCurrentUserAsync(cancellationToken);
            if (currentUser?.IsAuthenticated == true)
            {
                var localUser = await EnsureCurrentUserAsync(cancellationToken);
                if (localUser != null)
                {
                    return await _spaceContentAccessHelper.GetEffectivePermissionsForSpaceAsync(
                        localUser.Id,
                        spaceId,
                        isAnonymous: false,
                        cancellationToken);
                }
            }

            return await GetAnonymousPermissionsForSpaceAsync(spaceId, cancellationToken);
        }

        // Возвращает права текущего или anonymous пользователя для пространства, содержащего сущность.
        public async Task<UserEffectivePermissions> GetCurrentUserPermissionsForEntityAsync(
            Guid entityId,
            CancellationToken cancellationToken = default)
        {
            var spaceId = await _spaceContentAccessHelper.ResolveContainingSpaceIdAsync(entityId, cancellationToken);
            if (!spaceId.HasValue)
            {
                return UserEffectivePermissions.Deny(Guid.Empty, Guid.Empty, isAnonymous: false);
            }

            return await GetCurrentUserPermissionsForSpaceAsync(spaceId.Value, cancellationToken);
        }

        // Возвращает готовое content-access решение для текущего authenticated пользователя или anonymous mode.
        public async Task<UserContentAccessDecision> GetCurrentUserContentAccessForEntityAsync(
            UserContentAccessRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var currentBusinessUser = await GetCurrentUserAsync(cancellationToken);
            var localUser = currentBusinessUser?.IsAuthenticated == true
                ? await EnsureCurrentUserAsync(cancellationToken)
                : null;
            var permissions = await GetCurrentUserPermissionsForEntityAsync(request.EntityId, cancellationToken);
            var currentUserId = permissions.IsAnonymous ? null : localUser?.Id;
            var isAccessAdmin = IsAccessAdmin(currentBusinessUser);
            var canViewDraft = ContentAccessPolicy.CanViewDraft(
                request.EntityType,
                request.IsCommon,
                request.CreatedByUserId,
                currentUserId,
                isAccessAdmin,
                permissions);
            var canViewPublished = ContentAccessPolicy.CanViewPublished(
                request.EntityType,
                request.IsCommon,
                request.CreatedByUserId,
                currentUserId,
                isAccessAdmin,
                permissions,
                request.PublishedVersion);

            return new UserContentAccessDecision
            {
                IsOwner = ContentAccessPolicy.IsOwner(request.CreatedByUserId, currentUserId),
                IsAccessAdmin = isAccessAdmin,
                CanViewDraft = canViewDraft,
                CanViewPublished = canViewPublished,
                CanRead = canViewDraft || canViewPublished,
                CanEditDraft = ContentAccessPolicy.CanEditDraft(
                    request.EntityType,
                    request.IsCommon,
                    request.CreatedByUserId,
                    currentUserId,
                    isAccessAdmin,
                    permissions),
                CanPublishDraft = ContentAccessPolicy.CanPublishDraft(
                    request.EntityType,
                    request.IsCommon,
                    request.CreatedByUserId,
                    currentUserId,
                    isAccessAdmin,
                    permissions),
                CanChangeCommonFlag = ContentAccessPolicy.CanChangeCommonFlag(
                    request.CreatedByUserId,
                    currentUserId,
                    isAccessAdmin,
                    permissions),
                CanViewSpaceContainer = ContentAccessPolicy.CanViewSpaceContainer(permissions, isAccessAdmin)
            };
        }

        // Возвращает права системного anonymous-пользователя для пространства.
        public async Task<UserEffectivePermissions> GetAnonymousPermissionsForSpaceAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default)
        {
            var anonymousUser = await EnsureAnonymousUserAsync(cancellationToken);
            return await _spaceContentAccessHelper.GetEffectivePermissionsForSpaceAsync(
                anonymousUser.Id,
                spaceId,
                isAnonymous: true,
                cancellationToken);
        }

        // Возвращает пространства, где anonymous имеет права и доступные объекты.
        public async Task<IReadOnlyList<UserSpaceRecord>> GetAnonymousAccessibleSpacesAsync(CancellationToken cancellationToken = default)
        {
            var anonymousUser = await EnsureAnonymousUserAsync(cancellationToken);
            return await _spaceContentAccessHelper.GetSpacesWithAccessibleObjectsAsync(
                anonymousUser.Id,
                isAnonymous: true,
                cancellationToken);
        }

        // Возвращает документы выбранного пространства, которые anonymous может открыть.
        public async Task<IReadOnlyList<UserAccessibleDocumentRecord>> GetAnonymousAccessibleDocumentsAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default)
        {
            var anonymousUser = await EnsureAnonymousUserAsync(cancellationToken);
            return await _spaceContentAccessHelper.GetAccessibleDocumentsInSpaceAsync(
                anonymousUser.Id,
                spaceId,
                isAnonymous: true,
                cancellationToken);
        }

        // Возвращает текущий профиль пользователя из локальной DTO и сохраненных Authentik-идентификаторов.
        public async Task<UserProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default)
        {
            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return null;
            }

            var userData = ReadUserData(user);
            var authentikUser = await ResolveCachedOrStoredAuthentikUserAsync(
                userData,
                user.ExternalId,
                cancellationToken);
            return MapProfile(user, authentikUser);
        }

        // Обновляет отображаемое имя и пароль текущего пользователя.
        public async Task<UserProfileDto> UpdateProfileAsync(
            UserProfileUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            var user = await EnsureCurrentUserAsync(cancellationToken)
                       ?? throw new InvalidOperationException("Пользователь не найден.");
            var userData = ReadUserData(user);
            var authentikUser = await ResolveCachedOrStoredAuthentikUserAsync(
                userData,
                user.ExternalId,
                cancellationToken);
            var displayedName = NormalizeOptionalText(request.DisplayedName);
            if (string.IsNullOrWhiteSpace(displayedName))
            {
                displayedName = authentikUser.Username;
            }

            if (HasPasswordChange(request))
            {
                await ChangeCurrentUserPasswordAsync(authentikUser, request, cancellationToken);
            }

            user.ExternalId = authentikUser.Uid;
            user.Payload = SerializeUserData(BuildUserData(authentikUser, displayedName));
            user.DateLastModified = DateTime.UtcNow;
            await _userRepository.UpdateAsync(user, cancellationToken);
            return MapProfile(user, authentikUser);
        }

        public async Task<IReadOnlyList<RichTextDocumentBookmark>> GetRichDocBookmarksAsync(
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Array.Empty<RichTextDocumentBookmark>();
            }

            var payload = await ReadBookmarksPayloadAsync(user.Id, cancellationToken);
            return payload.Bookmarks
                .Where(x => x.DocumentId == documentId)
                .OrderBy(x => x.ChunkSortOrder)
                .ThenBy(x => x.BlockIndex)
                .ThenBy(x => x.CreatedDate)
                .ToList();
        }

        public async Task<RichTextDocumentBookmark?> AddRichDocBookmarkAsync(
            Guid documentId,
            RichTextDocumentTextSelection? selection,
            CancellationToken cancellationToken = default)
        {
            if (documentId == Guid.Empty || selection?.Position == null)
            {
                return null;
            }

            var selectedText = NormalizeBookmarkText(selection.Text);
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                return null;
            }

            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return null;
            }

            var payload = await ReadBookmarksPayloadAsync(user.Id, cancellationToken);
            var bookmark = new RichTextDocumentBookmark
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkSortOrder = selection.Position.ChunkSortOrder,
                BlockIndex = selection.Position.BlockIndex,
                SelectedText = selectedText,
                Label = BuildBookmarkLabel(selectedText),
                CreatedDate = DateTime.UtcNow
            };

            payload.Bookmarks.Add(bookmark);
            await UpsertBookmarksPayloadAsync(user.Id, payload, cancellationToken);
            return bookmark;
        }

        public async Task<bool> DeleteRichDocBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken = default)
        {
            if (bookmarkId == Guid.Empty)
            {
                return false;
            }

            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return false;
            }

            var payload = await ReadBookmarksPayloadAsync(user.Id, cancellationToken);
            var removedCount = payload.Bookmarks.RemoveAll(x => x.Id == bookmarkId);
            if (removedCount == 0)
            {
                return false;
            }

            await UpsertBookmarksPayloadAsync(user.Id, payload, cancellationToken);
            return true;
        }

        public async Task<int> GetRichDocDisplayedLevelAsync(
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            if (documentId == Guid.Empty)
            {
                return 1;
            }

            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return 1;
            }

            var property = await ReadDisplayedLevelPropertyAsync(user.Id, documentId, cancellationToken);
            return NormalizeDisplayedLevel(property?.DisplayLevelCount ?? 1);
        }

        public async Task SaveRichDocDisplayedLevelAsync(
            Guid documentId,
            int displayLevelCount,
            CancellationToken cancellationToken = default)
        {
            if (documentId == Guid.Empty)
            {
                return;
            }

            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return;
            }

            await UpsertDisplayedLevelPropertyAsync(
                user.Id,
                new RichDocDisplayedLevelProperty
                {
                    DocumentId = documentId,
                    DisplayLevelCount = NormalizeDisplayedLevel(displayLevelCount)
                },
                cancellationToken);
        }

        public async Task<IReadOnlyList<UserPropertyDto>> GetCurrentUserPropertiesAsync(
            int propertyType,
            CancellationToken cancellationToken = default)
        {
            if (propertyType <= (int)UserPropertyTypeEnum.Undefined)
            {
                return Array.Empty<UserPropertyDto>();
            }

            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Array.Empty<UserPropertyDto>();
            }

            return (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == user.Id && x.PropertyType == propertyType,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .Select(CloneUserProperty)
                .ToList();
        }

        public async Task<UserPropertyDto?> UpsertCurrentUserPropertyAsync(
            Guid? propertyId,
            int propertyType,
            string data,
            string metadata,
            CancellationToken cancellationToken = default)
        {
            if (propertyType <= (int)UserPropertyTypeEnum.Undefined)
            {
                throw new ArgumentOutOfRangeException(nameof(propertyType), "Тип пользовательского свойства должен быть положительным.");
            }

            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            UserPropertyDto? property = null;
            if (propertyId.HasValue && propertyId.Value != Guid.Empty)
            {
                property = (await _userPropertyRepository.GetAllAsync(
                        x => x.Id == propertyId.Value && x.ParentEntityId == user.Id,
                        cancellationToken))
                    .FirstOrDefault();

                if (property != null && property.PropertyType != propertyType)
                {
                    throw new InvalidOperationException("Нельзя обновить пользовательское свойство другим типом.");
                }
            }

            if (property == null)
            {
                property = new UserPropertyDto
                {
                    Id = Guid.NewGuid(),
                    DateCreated = now,
                    DateLastModified = now,
                    ParentEntityId = user.Id,
                    PropertyType = propertyType,
                    Data = data ?? string.Empty,
                    Metadata = metadata ?? string.Empty
                };

                await _userPropertyRepository.AddAsync(property, cancellationToken);
                return CloneUserProperty(property);
            }

            property.DateLastModified = now;
            property.Data = data ?? string.Empty;
            property.Metadata = metadata ?? string.Empty;
            await _userPropertyRepository.UpdateAsync(property, cancellationToken);
            return CloneUserProperty(property);
        }

        public async Task<bool> DeleteCurrentUserPropertyAsync(
            Guid propertyId,
            CancellationToken cancellationToken = default)
        {
            if (propertyId == Guid.Empty)
            {
                return false;
            }

            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return false;
            }

            var property = (await _userPropertyRepository.GetAllAsync(
                    x => x.Id == propertyId && x.ParentEntityId == user.Id,
                    cancellationToken))
                .FirstOrDefault();

            if (property == null)
            {
                return false;
            }

            await _userPropertyRepository.DeleteAsync(property.Id, cancellationToken);
            return true;
        }

        // Возвращает коллекцию пользовательских пресетов печати документов.
        public async Task<DocPrintSettingsPresetCollection> GetDocPrintPresetsAsync(CancellationToken cancellationToken = default)
        {
            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return new DocPrintSettingsPresetCollection();
            }

            return await ReadDocPrintPresetsPayloadAsync(user.Id, cancellationToken);
        }

        // Сохраняет или перезаписывает пользовательский пресет печати документов.
        public async Task<DocPrintSettingsPreset> SaveDocPrintPresetAsync(
            DocPrintSettingsPreset preset,
            CancellationToken cancellationToken = default)
        {
            var normalizedPreset = NormalizeDocPrintPreset(preset);
            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                throw new InvalidOperationException("Текущий пользователь не найден.");
            }

            var payload = await ReadDocPrintPresetsPayloadAsync(user.Id, cancellationToken);
            payload.Presets.RemoveAll(x => string.Equals(x.Name, normalizedPreset.Name, StringComparison.OrdinalIgnoreCase));
            payload.Presets.Add(normalizedPreset);
            payload.SelectedPresetName = normalizedPreset.Name;
            payload = NormalizeDocPrintPresetsPayload(payload);

            await UpsertDocPrintPresetsPayloadAsync(user.Id, payload, cancellationToken);
            return normalizedPreset;
        }

        // Удаляет пользовательский пресет печати документов по имени.
        public async Task<bool> DeleteDocPrintPresetAsync(string presetName, CancellationToken cancellationToken = default)
        {
            var normalizedName = NormalizeOptionalText(presetName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return false;
            }

            normalizedName = NormalizePrintPresetName(normalizedName);

            var user = await EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return false;
            }

            var payload = await ReadDocPrintPresetsPayloadAsync(user.Id, cancellationToken);
            var removedCount = payload.Presets.RemoveAll(x => string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (removedCount == 0)
            {
                return false;
            }

            if (string.Equals(payload.SelectedPresetName, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                payload.SelectedPresetName = payload.Presets.FirstOrDefault()?.Name ?? string.Empty;
            }

            payload = NormalizeDocPrintPresetsPayload(payload);
            await UpsertDocPrintPresetsPayloadAsync(user.Id, payload, cancellationToken);
            return true;
        }

        // Читает обязательные данные локального пользователя без молчаливого восстановления.
        private static UserData ReadUserData(UserDto user)
        {
            if (string.IsNullOrWhiteSpace(user.Payload)) throw new InvalidDataException("Отсутствует payload пользователя.");
            return JsonSerializer.Deserialize<UserData>(user.Payload, UserMiniAppJsonOptions.Default)
                ?? throw new InvalidDataException("Пустой payload пользователя.");
        }

        // Создает payload системного anonymous-пользователя без Authentik-идентификаторов.
        private static UserData BuildAnonymousUserData()
        {
            return new UserData
            {
                AuthentikLogin = SystemAnonymousExternalId,
                DisplayedName = SystemAnonymousDisplayName,
                ExtId = SystemAnonymousExternalId
            };
        }

        // Проверяет, является ли локальный пользователь системным anonymous.
        private static bool IsAnonymousUser(UserDto user)
        {
            if (string.Equals(user.ExternalId, SystemAnonymousExternalId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var userData = ReadUserData(user);
            return string.Equals(userData.ExtId, SystemAnonymousExternalId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(userData.AuthentikLogin, SystemAnonymousExternalId, StringComparison.OrdinalIgnoreCase);
        }

        // Проверяет, является ли локальный пользователь системной записью UserMiniApp.
        private static bool IsSystemUser(UserDto user)
        {
            return IsAnonymousUser(user);
        }

        private static string SerializeUserData(UserData userData)
        {
            return JsonSerializer.Serialize(userData, UserMiniAppJsonOptions.Default);
        }

        // Сортирует административные строки пользователей единым способом для UI и cache.
        private static IReadOnlyList<UserAdministrationRecord> SortAdministrationRecords(
            IEnumerable<UserAdministrationRecord> records)
        {
            return records
                .OrderBy(user => string.IsNullOrWhiteSpace(user.DisplayedName) ? user.AuthentikLogin : user.DisplayedName)
                .ThenBy(user => user.AuthentikLogin)
                .ThenBy(user => user.ExternalId)
                .ToList();
        }

        // Перезаписывает cache административных пользователей после первого чтения Authentik.
        private void SetAdministrationUsersCache(
            IReadOnlyList<UserAdministrationRecord> records,
            IEnumerable<AuthentikUserRecord> authentikUsers)
        {
            _state.AdministrationUsers = records;
            _state.AuthentikApplicationUsers = authentikUsers.ToList();
            _state.AreAdministrationUsersLoaded = true;
        }

        // Обновляет cache результатом создания или редактирования пользователя.
        private void UpsertAdministrationUserCache(
            UserAdministrationRecord record,
            AuthentikUserRecord authentikUser)
        {
            if (!_state.AreAdministrationUsersLoaded)
            {
                return;
            }

            _state.AdministrationUsers = SortAdministrationRecords(
                _state.AdministrationUsers
                    .Where(user => user.Id != record.Id)
                    .Append(record));
            _state.AuthentikApplicationUsers = _state.AuthentikApplicationUsers
                .Where(user => user.Pk != authentikUser.Pk)
                .Append(authentikUser)
                .ToList();
        }

        // Удаляет пользователя из административного cache после успешного удаления.
        private void RemoveAdministrationUserCache(Guid userId, int authentikUserPk)
        {
            if (!_state.AreAdministrationUsersLoaded)
            {
                return;
            }

            _state.AdministrationUsers = _state.AdministrationUsers
                .Where(user => user.Id != userId)
                .ToList();
            _state.AuthentikApplicationUsers = _state.AuthentikApplicationUsers
                .Where(user => user.Pk != authentikUserPk)
                .ToList();
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

        // Находит Authentik-пользователя из cache или локального payload без повторного чтения списка.
        private Task<AuthentikUserRecord> ResolveCachedOrStoredAuthentikUserAsync(
            UserData userData,
            string externalId,
            CancellationToken cancellationToken)
        {
            if (_state.AreAdministrationUsersLoaded)
            {
                var cachedUser = FindAuthentikUser(userData, externalId, _state.AuthentikApplicationUsers);
                if (cachedUser != null)
                {
                    return Task.FromResult(cachedUser);
                }
            }

            var storedUser = TryBuildStoredAuthentikUser(userData, externalId);
            if (storedUser != null)
            {
                return Task.FromResult(storedUser);
            }

            throw new InvalidOperationException(
                "Пользователь не синхронизирован с Authentik. Нажмите 'Прочитать' и повторите действие.");
        }

        // Ищет Authentik-запись по всем стабильным идентификаторам локального payload.
        private static AuthentikUserRecord? FindAuthentikUser(
            UserData userData,
            string externalId,
            IEnumerable<AuthentikUserRecord> authentikUsers)
        {
            var authentikUser = authentikUsers.FirstOrDefault(user =>
                userData.AuthentikUserPk > 0 && user.Pk == userData.AuthentikUserPk);

            authentikUser ??= authentikUsers.FirstOrDefault(user =>
                !string.IsNullOrWhiteSpace(userData.AuthentikUserUuid) &&
                string.Equals(user.Uuid, userData.AuthentikUserUuid, StringComparison.OrdinalIgnoreCase));
            authentikUser ??= authentikUsers.FirstOrDefault(user =>
                !string.IsNullOrWhiteSpace(userData.ExtId) &&
                string.Equals(user.Uid, userData.ExtId, StringComparison.OrdinalIgnoreCase));
            authentikUser ??= authentikUsers.FirstOrDefault(user =>
                !string.IsNullOrWhiteSpace(externalId) &&
                string.Equals(user.Uid, externalId, StringComparison.OrdinalIgnoreCase));
            authentikUser ??= authentikUsers.FirstOrDefault(user =>
                !string.IsNullOrWhiteSpace(userData.AuthentikLogin) &&
                string.Equals(user.Username, userData.AuthentikLogin, StringComparison.OrdinalIgnoreCase));

            return authentikUser;
        }

        // Восстанавливает минимальную Authentik-запись из локального payload для операций по pk.
        private static AuthentikUserRecord? TryBuildStoredAuthentikUser(UserData userData, string externalId)
        {
            if (userData.AuthentikUserPk <= 0)
            {
                return null;
            }

            var uid = NormalizeOptionalText(userData.ExtId);
            if (string.IsNullOrWhiteSpace(uid))
            {
                uid = NormalizeOptionalText(externalId);
            }

            var username = NormalizeOptionalText(userData.AuthentikLogin);
            if (string.IsNullOrWhiteSpace(username))
            {
                username = uid;
            }

            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            return new AuthentikUserRecord(
                userData.AuthentikUserPk,
                username,
                username,
                uid,
                NormalizeOptionalText(userData.AuthentikUserUuid),
                true,
                string.Empty,
                "internal",
                string.Empty);
        }

        // Формирует DTO административного UI из Authentik user и локального payload.
        private static UserAdministrationRecord MapAdministrationRecord(
            UserDto user,
            AuthentikUserRecord authentikUser)
        {
            var userData = ReadUserData(user);
            var displayedName = NormalizeOptionalText(userData.DisplayedName);

            return new UserAdministrationRecord
            {
                Id = user.Id,
                AuthentikUserPk = authentikUser.Pk,
                AuthentikUserUuid = authentikUser.Uuid,
                ExternalId = authentikUser.Uid,
                AuthentikLogin = authentikUser.Username,
                DisplayedName = string.IsNullOrWhiteSpace(displayedName) ? authentikUser.Username : displayedName,
                IsActive = authentikUser.IsActive,
                IsSystem = IsSystemUser(user),
                DateCreated = user.DateCreated,
                DateLastModified = user.DateLastModified
            };
        }

        // Формирует DTO административного UI только из локального пользователя Users.
        private static UserAdministrationRecord MapLocalAdministrationRecord(UserDto user)
        {
            var userData = ReadUserData(user);
            var externalId = NormalizeOptionalText(userData.ExtId);
            if (string.IsNullOrWhiteSpace(externalId))
            {
                externalId = user.ExternalId;
            }

            var authentikLogin = NormalizeOptionalText(userData.AuthentikLogin);
            if (string.IsNullOrWhiteSpace(authentikLogin))
            {
                authentikLogin = externalId;
            }

            var displayedName = NormalizeOptionalText(userData.DisplayedName);
            if (string.IsNullOrWhiteSpace(displayedName))
            {
                displayedName = authentikLogin;
            }

            return new UserAdministrationRecord
            {
                Id = user.Id,
                AuthentikUserPk = userData.AuthentikUserPk,
                AuthentikUserUuid = NormalizeOptionalText(userData.AuthentikUserUuid),
                ExternalId = externalId,
                AuthentikLogin = authentikLogin,
                DisplayedName = displayedName,
                IsActive = true,
                IsSystem = IsSystemUser(user),
                DateCreated = user.DateCreated,
                DateLastModified = user.DateLastModified
            };
        }

        // Формирует DTO страницы профиля из Authentik user и локального payload.
        private static UserProfileDto MapProfile(UserDto user, AuthentikUserRecord authentikUser)
        {
            var userData = ReadUserData(user);
            var displayedName = NormalizeOptionalText(userData.DisplayedName);

            return new UserProfileDto
            {
                UserId = user.Id,
                ExternalId = authentikUser.Uid,
                AuthentikLogin = authentikUser.Username,
                DisplayedName = string.IsNullOrWhiteSpace(displayedName) ? authentikUser.Username : displayedName
            };
        }

        // Формирует DTO роли для административного редактора ролей.
        private static UserRoleRecord MapRoleRecord(UserRoleDto role)
        {
            var permissions = ParsePermissionCodes(role.Permissions);
            return new UserRoleRecord
            {
                Id = role.Id,
                Name = role.Name,
                ViewPublished = permissions.Contains(UserRolePermissionCodes.ViewPublished),
                ViewDraft = permissions.Contains(UserRolePermissionCodes.ViewDraft),
                EditDraft = permissions.Contains(UserRolePermissionCodes.EditDraft),
                PublishDraft = permissions.Contains(UserRolePermissionCodes.PublishDraft),
                AdminItems = permissions.Contains(UserRolePermissionCodes.AdminItems),
                AdminSpace = permissions.Contains(UserRolePermissionCodes.AdminSpace),
                GlobalAdmin = permissions.Contains(UserRolePermissionCodes.GlobalAdmin),
                IsSystem = role.IsSystem,
                DateCreated = role.DateCreated,
                DateLastModified = role.DateLastModified
            };
        }

        // Формирует DTO группы для административного редактора групп.
        private static UserGroupRecord MapGroupRecord(UserGroupDto group, int userCount)
        {
            return new UserGroupRecord
            {
                Id = group.Id,
                Name = group.Name,
                UserCount = userCount,
                DateCreated = group.DateCreated,
                DateLastModified = group.DateLastModified
            };
        }

        // Формирует отсортированную таблицу назначения пользователей в выбранную группу из локальных DTO.
        private static IReadOnlyList<UserGroupMembershipRecord> MapGroupMembershipRecords(
            IEnumerable<UserDto> users,
            HashSet<Guid> memberUserIds)
        {
            return users
                .Select(user => MapGroupMembershipRecord(user, memberUserIds.Contains(user.Id)))
                .OrderBy(user => string.IsNullOrWhiteSpace(user.DisplayedName) ? user.AuthentikLogin : user.DisplayedName)
                .ThenBy(user => user.AuthentikLogin)
                .ThenBy(user => user.ExternalId)
                .ToList();
        }

        // Формирует DTO назначения пользователя в выбранную группу из локальной user DTO.
        private static UserGroupMembershipRecord MapGroupMembershipRecord(UserDto user, bool isMember)
        {
            var userData = ReadUserData(user);
            var externalId = NormalizeOptionalText(userData.ExtId);
            if (string.IsNullOrWhiteSpace(externalId))
            {
                externalId = user.ExternalId;
            }

            var authentikLogin = NormalizeOptionalText(userData.AuthentikLogin);
            if (string.IsNullOrWhiteSpace(authentikLogin))
            {
                authentikLogin = externalId;
            }

            var displayedName = NormalizeOptionalText(userData.DisplayedName);
            if (string.IsNullOrWhiteSpace(displayedName))
            {
                displayedName = authentikLogin;
            }

            return new UserGroupMembershipRecord
            {
                UserId = user.Id,
                DisplayedName = displayedName,
                AuthentikLogin = authentikLogin,
                ExternalId = externalId,
                IsMember = isMember
            };
        }

        // Формирует строки таблицы назначений ролей из хранимых DTO и справочников.
        private static IReadOnlyList<UserRoleAssignmentRecord> MapRoleAssignmentRecords(
            IEnumerable<UserRoleAssignmentDto> assignments,
            string spaceName,
            IEnumerable<UserGroupDto> groups,
            IEnumerable<UserDto> users,
            IEnumerable<UserRoleDto> roles)
        {
            var groupsById = groups.ToDictionary(group => group.Id);
            var usersById = users.ToDictionary(user => user.Id);
            var rolesById = roles.ToDictionary(role => role.Id);

            return assignments
                .Select(assignment => MapRoleAssignmentRecord(
                    assignment,
                    spaceName,
                    groupsById,
                    usersById,
                    rolesById))
                .OrderBy(assignment => assignment.Subject, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.AssignmentType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.SubjectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.RoleName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Формирует одну строку назначения роли с человекочитаемыми именами.
        private static UserRoleAssignmentRecord MapRoleAssignmentRecord(
            UserRoleAssignmentDto assignment,
            string spaceName,
            IReadOnlyDictionary<Guid, UserGroupDto> groupsById,
            IReadOnlyDictionary<Guid, UserDto> usersById,
            IReadOnlyDictionary<Guid, UserRoleDto> rolesById)
        {
            return new UserRoleAssignmentRecord
            {
                Id = assignment.Id,
                SpaceId = assignment.SpaceId,
                SpaceName = spaceName,
                Subject = NormalizeAssignmentSubject(assignment.Subject),
                SubjectId = assignment.SubjectId,
                SubjectName = ResolveAssignmentSubjectName(assignment, groupsById, usersById),
                AssignmentType = assignment.AssignmentType,
                RoleId = assignment.RoleId,
                RoleName = rolesById.TryGetValue(assignment.RoleId, out var role)
                    ? role.Name
                    : assignment.RoleId.ToString(),
                DateCreated = assignment.DateCreated,
                DateLastModified = assignment.DateLastModified
            };
        }

        // Определяет имя группы или пользователя для назначения роли.
        private static string ResolveAssignmentSubjectName(
            UserRoleAssignmentDto assignment,
            IReadOnlyDictionary<Guid, UserGroupDto> groupsById,
            IReadOnlyDictionary<Guid, UserDto> usersById)
        {
            if (assignment.AssignmentType == UserRoleAssignmentTypes.GroupToRole &&
                groupsById.TryGetValue(assignment.SubjectId, out var group))
            {
                return group.Name;
            }

            if (assignment.AssignmentType == UserRoleAssignmentTypes.UserToRole &&
                usersById.TryGetValue(assignment.SubjectId, out var user))
            {
                return MapLocalAdministrationRecord(user).DisplayedName;
            }

            return assignment.SubjectId.ToString();
        }

        // Собирает строковое представление всех прав системной роли.
        private static string BuildAllPermissionString()
        {
            return string.Join(
                string.Empty,
                new[]
                {
                    UserRolePermissionCodes.ViewPublished,
                    UserRolePermissionCodes.ViewDraft,
                    UserRolePermissionCodes.EditDraft,
                    UserRolePermissionCodes.PublishDraft,
                    UserRolePermissionCodes.AdminItems,
                    UserRolePermissionCodes.AdminSpace,
                    UserRolePermissionCodes.GlobalAdmin
                }.Select(code => code + ";"));
        }

        // Создает или синхронизирует базовую роль политики доступа.
        private async Task EnsureRoleAsync(
            IList<UserRoleDto> roles,
            string name,
            string permissions,
            bool isSystem,
            CancellationToken cancellationToken)
        {
            var role = roles
                .OrderBy(role => role.DateCreated)
                .FirstOrDefault(role => string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase));
            if (role == null)
            {
                var now = DateTime.UtcNow;
                var newRole = new UserRoleDto
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Permissions = permissions,
                    IsSystem = isSystem,
                    DateCreated = now,
                    DateLastModified = now
                };

                await _roleRepository.AddAsync(newRole, cancellationToken);
                roles.Add(newRole);
                return;
            }

            if (string.Equals(role.Name, name, StringComparison.Ordinal) &&
                string.Equals(role.Permissions, permissions, StringComparison.Ordinal) &&
                role.IsSystem == isSystem)
            {
                return;
            }

            role.Name = name;
            role.Permissions = permissions;
            role.IsSystem = isSystem;
            role.DateLastModified = DateTime.UtcNow;
            await _roleRepository.UpdateAsync(role, cancellationToken);
        }

        // Собирает строковое представление роли только для published-чтения.
        private static string BuildReadPublishedPermissionString()
        {
            return $"{UserRolePermissionCodes.ViewPublished};";
        }

        // Проверяет, является ли имя роли частью базовой матрицы доступа.
        private static bool IsBaselineRoleName(string? roleName)
        {
            return string.Equals(roleName, SystemAdminRoleName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(roleName, GuestRoleName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(roleName, ReadersRoleName, StringComparison.OrdinalIgnoreCase);
        }

        // Проверяет явный emergency/admin bypass для системных учеток приложения.
        private static bool IsAccessAdmin(BusinessEntityUser? user)
        {
            return user?.IsAkadmin == true ||
                   user?.IsGeneralAdmin == true ||
                   string.Equals(user?.UserName, "admin", StringComparison.OrdinalIgnoreCase);
        }

        // Гарантирует право текущего пользователя менять назначения ролей.
        private async Task EnsureCurrentUserCanAdminRoleAssignmentsAsync(
            Guid spaceId,
            CancellationToken cancellationToken)
        {
            var currentUser = await GetCurrentUserAsync(cancellationToken);
            if (IsAccessAdmin(currentUser))
            {
                return;
            }

            if (currentUser?.IsAuthenticated != true)
            {
                throw new UnauthorizedAccessException("Нет прав на администрирование пространства.");
            }

            var localUser = await EnsureCurrentUserAsync(cancellationToken);
            if (localUser == null)
            {
                throw new UnauthorizedAccessException("Нет прав на администрирование пространства.");
            }

            if (spaceId == Guid.Empty)
            {
                if (await CurrentUserHasGlobalAdminRoleAsync(localUser.Id, cancellationToken))
                {
                    return;
                }

                throw new UnauthorizedAccessException("Нет прав глобального администратора.");
            }

            var permissions = await _spaceContentAccessHelper.GetEffectivePermissionsForSpaceAsync(
                localUser.Id,
                spaceId,
                isAnonymous: false,
                cancellationToken);
            if (permissions.CanAdminSpace || permissions.CanGlobalAdmin)
            {
                return;
            }

            throw new UnauthorizedAccessException("Нет прав на администрирование пространства.");
        }

        // Проверяет наличие GlobalAdmin через прямые или групповые назначения ролей.
        private async Task<bool> CurrentUserHasGlobalAdminRoleAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty)
            {
                return false;
            }

            var groupIds = (await _groupMemberRepository.GetAllAsync(
                    membership => membership.UserId == userId,
                    cancellationToken))
                .Select(membership => membership.GroupId)
                .ToHashSet();
            var assignments = await _roleAssignmentRepository.GetAllAsync(
                assignment => assignment.Subject == UserRoleAssignmentSubjects.AllSpaces ||
                              assignment.SpaceId == Guid.Empty,
                cancellationToken);
            var roleIds = assignments
                .Where(assignment => IsRoleAssignmentApplicableToUser(assignment, userId, groupIds))
                .Select(assignment => assignment.RoleId)
                .Distinct()
                .ToHashSet();
            if (roleIds.Count == 0)
            {
                return false;
            }

            var roles = await _roleRepository.GetAllAsync(role => roleIds.Contains(role.Id), cancellationToken);
            return roles.Any(role => ParsePermissionCodes(role.Permissions).Contains(UserRolePermissionCodes.GlobalAdmin));
        }

        // Проверяет применимость назначения роли к пользователю напрямую или через группу.
        private static bool IsRoleAssignmentApplicableToUser(
            UserRoleAssignmentDto assignment,
            Guid userId,
            HashSet<Guid> groupIds)
        {
            if (assignment.AssignmentType == UserRoleAssignmentTypes.UserToRole)
            {
                return assignment.SubjectId == userId;
            }

            return assignment.AssignmentType == UserRoleAssignmentTypes.GroupToRole &&
                   groupIds.Contains(assignment.SubjectId);
        }

        // Собирает строковое представление выбранных прав роли.
        private static string BuildPermissionString(UserRoleSaveRequest request)
        {
            var codes = new List<int>();
            if (request.ViewPublished)
            {
                codes.Add(UserRolePermissionCodes.ViewPublished);
            }

            if (request.ViewDraft)
            {
                codes.Add(UserRolePermissionCodes.ViewDraft);
            }

            if (request.EditDraft)
            {
                codes.Add(UserRolePermissionCodes.EditDraft);
            }

            if (request.PublishDraft)
            {
                codes.Add(UserRolePermissionCodes.PublishDraft);
            }

            if (request.AdminItems)
            {
                codes.Add(UserRolePermissionCodes.AdminItems);
            }

            if (request.AdminSpace)
            {
                codes.Add(UserRolePermissionCodes.AdminSpace);
            }

            if (request.GlobalAdmin)
            {
                codes.Add(UserRolePermissionCodes.GlobalAdmin);
            }

            return string.Join(string.Empty, codes.Select(code => code + ";"));
        }

        // Разбирает строку прав роли в набор числовых кодов.
        private static HashSet<int> ParsePermissionCodes(string? value)
        {
            var result = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out var code))
                {
                    result.Add(code);
                }
            }

            return result;
        }

        // Генерирует уникальное имя для новой роли.
        private static string BuildUniqueRoleName(IReadOnlyList<UserRoleDto> existingRoles)
        {
            const string baseName = "Новая роль";
            var names = existingRoles
                .Select(role => role.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!names.Contains(baseName))
            {
                return baseName;
            }

            var index = 2;
            while (names.Contains($"{baseName} {index}"))
            {
                index++;
            }

            return $"{baseName} {index}";
        }

        // Генерирует уникальное имя для новой группы пользователей.
        private static string BuildUniqueGroupName(IReadOnlyList<UserGroupDto> existingGroups)
        {
            const string baseName = "Новая группа";
            var names = existingGroups
                .Select(group => group.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!names.Contains(baseName))
            {
                return baseName;
            }

            var index = 2;
            while (names.Contains($"{baseName} {index}"))
            {
                index++;
            }

            return $"{baseName} {index}";
        }

        // Проверяет, что выбранная группа существует.
        private async Task EnsureGroupExistsAsync(Guid groupId, CancellationToken cancellationToken)
        {
            if (groupId == Guid.Empty)
            {
                throw new ArgumentException("Группа не выбрана.", nameof(groupId));
            }

            var group = await _groupRepository.GetByIdAsync(groupId, cancellationToken);
            if (group == null)
            {
                throw new KeyNotFoundException("Группа не найдена.");
            }
        }

        // Проверяет, что выбранная роль существует.
        private async Task EnsureRoleExistsAsync(Guid roleId, CancellationToken cancellationToken)
        {
            if (roleId == Guid.Empty)
            {
                throw new ArgumentException("Роль не выбрана.", nameof(roleId));
            }

            var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
            if (role == null)
            {
                throw new KeyNotFoundException("Роль не найдена.");
            }
        }

        // Проверяет, что субъект назначения существует для заданного типа.
        private async Task EnsureAssignmentSubjectExistsAsync(
            Guid subjectId,
            string assignmentType,
            CancellationToken cancellationToken)
        {
            if (subjectId == Guid.Empty)
            {
                throw new ArgumentException("Группа или пользователь не выбраны.", nameof(subjectId));
            }

            if (assignmentType == UserRoleAssignmentTypes.GroupToRole)
            {
                await EnsureGroupExistsAsync(subjectId, cancellationToken);
                return;
            }

            if (assignmentType == UserRoleAssignmentTypes.UserToRole)
            {
                var user = await _userRepository.GetByIdAsync(subjectId, cancellationToken);
                if (user == null)
                {
                    throw new KeyNotFoundException("Пользователь не найден.");
                }

                return;
            }

            throw new ArgumentException("Тип назначения не поддерживается.", nameof(assignmentType));
        }

        // Возвращает имя существующего пространства или выбрасывает понятную ошибку.
        private async Task<string> GetRequiredSpaceNameAsync(Guid spaceId, CancellationToken cancellationToken)
        {
            if (spaceId == Guid.Empty)
            {
                throw new ArgumentException("Пространство не выбрано.", nameof(spaceId));
            }

            var space = await _data.GetByIdAsync(spaceId, cancellationToken);
            if (space == null || space.EntityType != BusinessEntityTypeEnum.Space)
            {
                throw new KeyNotFoundException("Пространство не найдено.");
            }

            return space.Name;
        }

        // Возвращает имя области назначения роли для обычного или глобального выбора пространств.
        private async Task<string> GetRoleAssignmentSpaceNameAsync(
            Guid spaceId,
            string assignmentSubject,
            CancellationToken cancellationToken)
        {
            if (assignmentSubject == UserRoleAssignmentSubjects.AllSpaces)
            {
                return AllSpacesDisplayName;
            }

            return await GetRequiredSpaceNameAsync(spaceId, cancellationToken);
        }

        // Нормализует строковый маркер области действия назначения роли.
        private static string NormalizeAssignmentSubject(string? assignmentSubject)
        {
            var normalized = NormalizeOptionalText(assignmentSubject);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return UserRoleAssignmentSubjects.Space;
            }

            if (string.Equals(normalized, UserRoleAssignmentSubjects.Space, StringComparison.OrdinalIgnoreCase))
            {
                return UserRoleAssignmentSubjects.Space;
            }

            if (string.Equals(normalized, UserRoleAssignmentSubjects.AllSpaces, StringComparison.OrdinalIgnoreCase))
            {
                return UserRoleAssignmentSubjects.AllSpaces;
            }

            throw new ArgumentException("Субъект назначения не поддерживается.", nameof(assignmentSubject));
        }

        // Нормализует строковый маркер типа назначения роли.
        private static string NormalizeAssignmentType(string? assignmentType)
        {
            var normalized = NormalizeOptionalText(assignmentType);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return UserRoleAssignmentTypes.GroupToRole;
            }

            if (string.Equals(normalized, UserRoleAssignmentTypes.GroupToRole, StringComparison.OrdinalIgnoreCase))
            {
                return UserRoleAssignmentTypes.GroupToRole;
            }

            if (string.Equals(normalized, UserRoleAssignmentTypes.UserToRole, StringComparison.OrdinalIgnoreCase))
            {
                return UserRoleAssignmentTypes.UserToRole;
            }

            throw new ArgumentException("Тип назначения не поддерживается.", nameof(assignmentType));
        }

        // Проверяет, заполнено ли хотя бы одно поле смены пароля.
        private static bool HasPasswordChange(UserProfileUpdateRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.OldPassword) ||
                   !string.IsNullOrWhiteSpace(request.NewPassword) ||
                   !string.IsNullOrWhiteSpace(request.RepeatPassword);
        }

        // Проверяет старый пароль и устанавливает новый пароль текущему Authentik-пользователю.
        private async Task ChangeCurrentUserPasswordAsync(
            AuthentikUserRecord authentikUser,
            UserProfileUpdateRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.OldPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword) ||
                string.IsNullOrWhiteSpace(request.RepeatPassword))
            {
                throw new ArgumentException("Для смены пароля заполните старый пароль, новый пароль и повторение.");
            }

            if (!string.Equals(request.NewPassword, request.RepeatPassword, StringComparison.Ordinal))
            {
                throw new ArgumentException("Новый пароль и повторение пароля не совпадают.");
            }

            var isOldPasswordValid = await _identitySession.ValidatePasswordAsync(
                authentikUser.Username,
                request.OldPassword,
                cancellationToken);
            if (!isOldPasswordValid)
            {
                throw new ArgumentException("Старый пароль указан неверно.");
            }

            await _authentikManagementClient.SetPasswordAsync(
                authentikUser.Pk,
                request.NewPassword,
                cancellationToken);
        }

        private static UserDto? FindLocalUser(AuthentikUserRecord authentikUser, IEnumerable<UserDto> localUsers)
        {
            foreach (var localUser in localUsers)
            {
                var userData = ReadUserData(localUser);
                if (userData.AuthentikUserPk > 0 && userData.AuthentikUserPk == authentikUser.Pk)
                {
                    return localUser;
                }

                if (!string.IsNullOrWhiteSpace(userData.AuthentikUserUuid) &&
                    string.Equals(userData.AuthentikUserUuid, authentikUser.Uuid, StringComparison.OrdinalIgnoreCase))
                {
                    return localUser;
                }

                if (string.Equals(localUser.ExternalId, authentikUser.Uid, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(userData.ExtId, authentikUser.Uid, StringComparison.OrdinalIgnoreCase))
                {
                    return localUser;
                }
            }

            return null;
        }

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

        // Определяет стабильный внешний идентификатор пользователя из нормализованной user-модели.
        private static string ResolveExternalId(BusinessEntityUser currentUser)
        {
            var externalId = currentUser.GetNameIdentifier();
            if (string.IsNullOrWhiteSpace(externalId))
            {
                externalId = currentUser.UserId;
            }

            return externalId;
        }

        // Извлекает внутренний pk Authentik из claims локального password-flow login.
        private static int ResolveAuthentikUserPk(BusinessEntityUser currentUser)
        {
            var value = currentUser.GetFirstClaimValue("authentik_user_pk");
            return int.TryParse(value, out var authentikUserPk) ? authentikUserPk : 0;
        }

        private static UserPropertyDto CloneUserProperty(UserPropertyDto property)
        {
            return new UserPropertyDto
            {
                Id = property.Id,
                DateCreated = property.DateCreated,
                DateLastModified = property.DateLastModified,
                ParentEntityId = property.ParentEntityId,
                PropertyType = property.PropertyType,
                Data = property.Data,
                Metadata = property.Metadata
            };
        }

        private async Task<RichTextDocumentBookmarksPayload> ReadBookmarksPayloadAsync(Guid userId, CancellationToken cancellationToken)
        {
            var property = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.RichDocBookmarks,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .FirstOrDefault();

            if (property == null)
            {
                return new RichTextDocumentBookmarksPayload();
            }

            var payload = JsonSerializer.Deserialize<RichTextDocumentBookmarksPayload>(property.Data, UserMiniAppJsonOptions.Default);
            if (payload?.SchemaVersion != 1 || payload.Kind != "RichDocBookmarks" || payload.Bookmarks == null)
                throw new InvalidDataException("Неизвестный формат закладок пользователя.");
            return payload;
        }

        // Читает payload пользовательских пресетов печати из единственной user-property.
        private async Task<DocPrintSettingsPresetCollection> ReadDocPrintPresetsPayloadAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            var property = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.DocPrintPresets,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .FirstOrDefault();

            if (property == null)
            {
                return new DocPrintSettingsPresetCollection();
            }

            var payload = JsonSerializer.Deserialize<DocPrintSettingsPresetCollection>(property.Data, UserMiniAppJsonOptions.Default);
            if (payload?.SchemaVersion != 1 || payload.Kind != nameof(DocPrintSettingsPresetCollection))
                throw new InvalidDataException("Неизвестный формат пресетов печати.");
            return NormalizeDocPrintPresetsPayload(payload);
        }

        private async Task<RichDocDisplayedLevelProperty?> ReadDisplayedLevelPropertyAsync(
            Guid userId,
            Guid documentId,
            CancellationToken cancellationToken)
        {
            var properties = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.RichDocDisplayedLevelProperty,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .ToList();

            foreach (var property in properties)
            {
                var payload = TryReadDisplayedLevelProperty(property.Data);
                if (payload?.DocumentId == documentId)
                {
                    return payload;
                }
            }

            return null;
        }

        private async Task UpsertBookmarksPayloadAsync(
            Guid userId,
            RichTextDocumentBookmarksPayload payload,
            CancellationToken cancellationToken)
        {
            var properties = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.RichDocBookmarks,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .ToList();

            var now = DateTime.UtcNow;
            var data = JsonSerializer.Serialize(payload, UserMiniAppJsonOptions.Default);
            var metadata = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    kind = "RichDocBookmarksMetadata",
                    bookmarkCount = payload.Bookmarks.Count
                },
                UserMiniAppJsonOptions.Default);

            var property = properties.FirstOrDefault();
            if (property == null)
            {
                await _userPropertyRepository.AddAsync(
                    new UserPropertyDto
                    {
                        Id = Guid.NewGuid(),
                        DateCreated = now,
                        DateLastModified = now,
                        ParentEntityId = userId,
                        PropertyType = (int)UserPropertyTypeEnum.RichDocBookmarks,
                        Data = data,
                        Metadata = metadata
                    },
                    cancellationToken);
                return;
            }

            property.DateLastModified = now;
            property.Data = data;
            property.Metadata = metadata;
            await _userPropertyRepository.UpdateAsync(property, cancellationToken);

            foreach (var duplicate in properties.Skip(1))
            {
                await _userPropertyRepository.DeleteAsync(duplicate.Id, cancellationToken);
            }
        }

        // Создает или обновляет единственную user-property с коллекцией пресетов печати.
        private async Task UpsertDocPrintPresetsPayloadAsync(
            Guid userId,
            DocPrintSettingsPresetCollection payload,
            CancellationToken cancellationToken)
        {
            var properties = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.DocPrintPresets,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .ToList();

            payload = NormalizeDocPrintPresetsPayload(payload);

            var now = DateTime.UtcNow;
            var data = JsonSerializer.Serialize(payload, UserMiniAppJsonOptions.Default);
            var metadata = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    kind = "DocPrintPresetsMetadata",
                    presetCount = payload.Presets.Count,
                    selectedPresetName = payload.SelectedPresetName
                },
                UserMiniAppJsonOptions.Default);

            var property = properties.FirstOrDefault();
            if (property == null)
            {
                await _userPropertyRepository.AddAsync(
                    new UserPropertyDto
                    {
                        Id = Guid.NewGuid(),
                        DateCreated = now,
                        DateLastModified = now,
                        ParentEntityId = userId,
                        PropertyType = (int)UserPropertyTypeEnum.DocPrintPresets,
                        Data = data,
                        Metadata = metadata
                    },
                    cancellationToken);
                return;
            }

            property.DateLastModified = now;
            property.Data = data;
            property.Metadata = metadata;
            await _userPropertyRepository.UpdateAsync(property, cancellationToken);

            foreach (var duplicate in properties.Skip(1))
            {
                await _userPropertyRepository.DeleteAsync(duplicate.Id, cancellationToken);
            }
        }

        private async Task UpsertDisplayedLevelPropertyAsync(
            Guid userId,
            RichDocDisplayedLevelProperty payload,
            CancellationToken cancellationToken)
        {
            var properties = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.RichDocDisplayedLevelProperty,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .ToList();

            payload.DisplayLevelCount = NormalizeDisplayedLevel(payload.DisplayLevelCount);

            var matchingProperties = properties
                .Where(property => TryReadDisplayedLevelProperty(property.Data)?.DocumentId == payload.DocumentId)
                .ToList();

            var now = DateTime.UtcNow;
            var data = JsonSerializer.Serialize(payload, UserMiniAppJsonOptions.Default);
            var metadata = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    kind = "RichDocDisplayedLevelPropertyMetadata",
                    documentId = payload.DocumentId,
                    displayLevelCount = payload.DisplayLevelCount
                },
                UserMiniAppJsonOptions.Default);

            var property = matchingProperties.FirstOrDefault();
            if (property == null)
            {
                await _userPropertyRepository.AddAsync(
                    new UserPropertyDto
                    {
                        Id = Guid.NewGuid(),
                        DateCreated = now,
                        DateLastModified = now,
                        ParentEntityId = userId,
                        PropertyType = (int)UserPropertyTypeEnum.RichDocDisplayedLevelProperty,
                        Data = data,
                        Metadata = metadata
                    },
                    cancellationToken);
                return;
            }

            property.DateLastModified = now;
            property.Data = data;
            property.Metadata = metadata;
            await _userPropertyRepository.UpdateAsync(property, cancellationToken);

            foreach (var duplicate in matchingProperties.Skip(1))
            {
                await _userPropertyRepository.DeleteAsync(duplicate.Id, cancellationToken);
            }
        }

        // Отсутствие свойства обрабатывается запросом; повреждённая строка не игнорируется.
        private static RichDocDisplayedLevelProperty TryReadDisplayedLevelProperty(string? data)
        {
            if (string.IsNullOrWhiteSpace(data)) throw new InvalidDataException("Отсутствует настройка отображения статьи.");
            var payload = JsonSerializer.Deserialize<RichDocDisplayedLevelProperty>(data, UserMiniAppJsonOptions.Default);
            if (payload?.SchemaVersion != 1 || payload.Kind != "RichDocDisplayedLevelProperty")
                throw new InvalidDataException("Неизвестный формат настройки отображения статьи.");
            payload.DisplayLevelCount = NormalizeDisplayedLevel(payload.DisplayLevelCount);
            return payload;
        }

        // Нормализует collection payload пресетов печати после чтения или перед записью.
        private static DocPrintSettingsPresetCollection NormalizeDocPrintPresetsPayload(DocPrintSettingsPresetCollection? payload)
        {
            var presetsByName = new Dictionary<string, DocPrintSettingsPreset>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in payload?.Presets ?? new List<DocPrintSettingsPreset>())
            {
                if (preset == null)
                {
                    continue;
                }

                var name = NormalizeOptionalText(preset.Name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var normalizedPreset = NormalizeDocPrintPreset(
                    new DocPrintSettingsPreset
                    {
                        Name = name,
                        Settings = preset.Settings
                    });
                presetsByName[normalizedPreset.Name] = normalizedPreset;
            }

            var presets = presetsByName.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selectedPresetName = NormalizeOptionalText(payload?.SelectedPresetName);
            if (!string.IsNullOrWhiteSpace(selectedPresetName))
            {
                selectedPresetName = presets
                    .FirstOrDefault(x => string.Equals(x.Name, selectedPresetName, StringComparison.OrdinalIgnoreCase))
                    ?.Name ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(selectedPresetName))
            {
                selectedPresetName = presets.FirstOrDefault()?.Name ?? string.Empty;
            }

            return new DocPrintSettingsPresetCollection
            {
                SchemaVersion = 1,
                Kind = nameof(DocPrintSettingsPresetCollection),
                SelectedPresetName = selectedPresetName,
                Presets = presets
            };
        }

        // Нормализует один именованный пресет печати.
        private static DocPrintSettingsPreset NormalizeDocPrintPreset(DocPrintSettingsPreset? preset)
        {
            return new DocPrintSettingsPreset
            {
                Name = NormalizePrintPresetName(preset?.Name),
                Settings = NormalizeDocPrintSettings(preset?.Settings)
            };
        }

        // Нормализует числовые настройки печати.
        private static DocPrintSettings NormalizeDocPrintSettings(DocPrintSettings? settings)
        {
            if (settings == null)
            {
                return new DocPrintSettings();
            }

            return new DocPrintSettings
            {
                SchemaVersion = settings.SchemaVersion > 0 ? settings.SchemaVersion : 1,
                Kind = string.IsNullOrWhiteSpace(settings.Kind) ? nameof(DocPrintSettings) : settings.Kind,
                FontScalePercent = settings.FontScalePercent,
                MarginTopMm = settings.MarginTopMm,
                MarginBottomMm = settings.MarginBottomMm,
                MarginRightMm = settings.MarginRightMm,
                MarginLeftMm = settings.MarginLeftMm
            };
        }

        // Нормализует имя пресета печати и ограничивает его длину.
        private static string NormalizePrintPresetName(string? value)
        {
            var normalized = NormalizeRequiredText(value, "Имя пресета печати");
            return normalized.Length <= MaxPrintPresetNameLength
                ? normalized
                : normalized[..MaxPrintPresetNameLength].Trim();
        }

        private static int NormalizeDisplayedLevel(int value)
        {
            return Math.Clamp(value, 1, 3);
        }

        private static string NormalizeBookmarkText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = string.Join(
                " ",
                text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));

            return normalized.Length <= MaxBookmarkTextLength
                ? normalized
                : normalized[..MaxBookmarkTextLength];
        }

        private static string BuildBookmarkLabel(string selectedText)
        {
            if (selectedText.Length <= MaxBookmarkLabelLength)
            {
                return selectedText;
            }

            return selectedText[..MaxBookmarkLabelLength].TrimEnd() + "...";
        }
    }
}

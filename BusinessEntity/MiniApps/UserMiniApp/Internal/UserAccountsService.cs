using BusinessEntity.Services;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using System.Text.Json;

using static BusinessEntity.MiniApps.UserMiniApp.Internal.UserValueRules;
namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// UserAccountsService владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserAccountsService
{
        private readonly UserMiniAppState _state;
        private readonly UserIdentitySession _identitySession;
        private readonly AuthentikManagementClient _authentikManagementClient;
        private readonly IUserMiniAppRepository<UserDto> _userRepository;
        private readonly UserIdentityService _identityService;
        private readonly UserAccountRemoval _removal;
        // Шесть зависимостей обслуживают один account workflow: identity, профиль, Authentik, локальная запись, cache и атомарное удаление.
        // Получает зависимости выделенного сценария.
        public UserAccountsService(UserMiniAppState state, UserIdentitySession identitySession, AuthentikManagementClient authentikManagementClient, IUserMiniAppRepository<UserDto> userRepository, UserIdentityService identityService, UserAccountRemoval removal)
        { _state = state; _identitySession = identitySession; _authentikManagementClient = authentikManagementClient; _userRepository = userRepository; _identityService = identityService; _removal = removal; }
        // Выполняет сценарий DeleteCurrentUserAsync внутри владельца User.
        public async Task<bool> DeleteCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var currentUser = await _identityService.GetCurrentUserAsync(cancellationToken);
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

            foreach (var user in users) await _removal.DeleteAsync(user.Id, cancellationToken);

            return true;
        }

        // Выполняет сценарий GetAdministrationUsersAsync внутри владельца User.
        public async Task<IReadOnlyList<UserAdministrationRecord>> GetAdministrationUsersAsync(CancellationToken cancellationToken = default)
        {
            await _identityService.EnsureSystemUsersAsync(cancellationToken);
            var localUsers = await _userRepository.GetAllAsync(null, cancellationToken);
            return SortAdministrationRecords(localUsers.Select(MapLocalAdministrationRecord));
        }

        // Выполняет сценарий ReadAdministrationUsersFromAuthentikAsync внутри владельца User.
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

        // Выполняет сценарий CreateAdministrationUserAsync внутри владельца User.
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

        // Выполняет сценарий UpdateAdministrationUserAsync внутри владельца User.
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

        // Выполняет сценарий DeleteAdministrationUserAsync внутри владельца User.
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

            await _removal.DeleteAsync(userId, cancellationToken);
            RemoveAdministrationUserCache(userId, authentikUser.Pk);
            return true;
        }

        // Выполняет сценарий GetProfileAsync внутри владельца User.
        public async Task<UserProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default)
        {
            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
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

        // Выполняет сценарий UpdateProfileAsync внутри владельца User.
        public async Task<UserProfileDto> UpdateProfileAsync(
            UserProfileUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken)
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

        // Выполняет сценарий SetAdministrationUsersCache внутри владельца User.
        internal void SetAdministrationUsersCache(
            IReadOnlyList<UserAdministrationRecord> records,
            IEnumerable<AuthentikUserRecord> authentikUsers)
        {
            _state.AdministrationUsers = records;
            _state.AuthentikApplicationUsers = authentikUsers.ToList();
            _state.AreAdministrationUsersLoaded = true;
        }

        // Выполняет сценарий UpsertAdministrationUserCache внутри владельца User.
        internal void UpsertAdministrationUserCache(
            UserAdministrationRecord record,
            AuthentikUserRecord authentikUser)
        {
            if (UserMutationCapture.Current is { } capture)
            { capture.PostCommit.Add(() => UpsertAdministrationUserCache(record, authentikUser)); return; }

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

        // Выполняет сценарий RemoveAdministrationUserCache внутри владельца User.
        internal void RemoveAdministrationUserCache(Guid userId, int authentikUserPk)
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

        // Выполняет сценарий UpsertLocalUserFromAuthentikAsync внутри владельца User.
        internal async Task<UserDto> UpsertLocalUserFromAuthentikAsync(
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

        // Выполняет сценарий ResolveCachedOrStoredAuthentikUserAsync внутри владельца User.
        internal Task<AuthentikUserRecord> ResolveCachedOrStoredAuthentikUserAsync(
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

        // Выполняет сценарий ChangeCurrentUserPasswordAsync внутри владельца User.
        internal async Task ChangeCurrentUserPasswordAsync(
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
}

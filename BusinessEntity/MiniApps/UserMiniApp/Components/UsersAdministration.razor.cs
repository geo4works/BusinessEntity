using BusinessEntity.MiniApps.UserMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;
using Microsoft.AspNetCore.Components;
using ReactiveUI;

namespace BusinessEntity.MiniApps.UserMiniApp.Components
{
    // Компонент административного CRUD локальных пользователей UserMiniApp.
    public partial class UsersAdministration
    {
        [Inject] private UserFormsWorkingSetFactory WorkingSets { get; set; } = default!;
        private UserFormsWorkingSet Working => WorkingSets.Get(Module!);
        // Save всегда охватывает весь рабочий экземпляр, а не только видимую строку.
        private async Task SaveWorkingSetAsync()
        {
            var result = await Module!.SaveContext!.SaveAsync(Module.ModuleId);
            if (!result.Success) throw new InvalidOperationException(result.Error);
        }
        // События ввода отмечают dirty в владельце и сохраняют выбранные формы между переключениями.
        private void AccountChanged() { if (SelectedUserId.HasValue) Working.Changed(UserFormKind.Account, SelectedUserId.Value); }
        private void RoleChanged() { if (SelectedRoleId.HasValue) Working.Changed(UserFormKind.Role, SelectedRoleId.Value); }
        private void GroupChanged() { if (SelectedGroupId.HasValue) Working.Changed(UserFormKind.Group, SelectedGroupId.Value); }
        private void MembersChanged() { if (MembershipGroupId.HasValue) Working.Changed(UserFormKind.Members, MembershipGroupId.Value); }
        [Inject] private UserAccountsService UserAccountsService { get; set; } = default!;
        [Inject] private UserGroupService UserGroupService { get; set; } = default!;
        [Inject] private UserIdentityService UserIdentityService { get; set; } = default!;
        [Inject] private UserRoleAssignmentDirectory UserRoleAssignmentDirectory { get; set; } = default!;
        [Inject] private UserRoleAssignmentService UserRoleAssignmentService { get; set; } = default!;
        [Inject] private UserRoleCatalogService UserRoleCatalogService { get; set; } = default!;
        [Inject] public IMessageBus MessageBus { get; set; } = default!;
        [Inject] public BusinessEntity.Infrastructure.Bus.BusRequestScope BusScope { get; set; } = default!;

        private IReadOnlyList<UserAdministrationRecord> Users { get; set; } = Array.Empty<UserAdministrationRecord>();
        private IReadOnlyList<UserRoleRecord> Roles { get; set; } = Array.Empty<UserRoleRecord>();
        private IReadOnlyList<UserGroupRecord> Groups { get; set; } = Array.Empty<UserGroupRecord>();
        private IReadOnlyList<UserGroupMembershipRecord> GroupMemberships { get; set; } = Array.Empty<UserGroupMembershipRecord>();
        private IReadOnlyList<UserSpaceRecord> AccessSpaces { get; set; } = Array.Empty<UserSpaceRecord>();
        private IReadOnlyList<UserRoleAssignmentRecord> RoleAssignments { get; set; } = Array.Empty<UserRoleAssignmentRecord>();
        private UserAdministrationSaveRequest EditModel { get; set; } = new();
        private UserRoleSaveRequest RoleEditModel { get; set; } = new();
        private UserGroupSaveRequest GroupEditModel { get; set; } = new();
        private Guid? SelectedUserId { get; set; }
        private Guid? SelectedRoleId { get; set; }
        private Guid? SelectedGroupId { get; set; }
        private Guid? MembershipGroupId { get; set; }
        private Guid? AccessSpaceId { get; set; }
        private Guid? AccessGroupId { get; set; }
        private Guid? AccessRoleId { get; set; }
        private bool ShowAllAccessAssignments { get; set; } = true;
        private Guid? DeleteConfirmationUserId { get; set; }
        private Guid? DeleteConfirmationRoleId { get; set; }
        private Guid? DeleteConfirmationGroupId { get; set; }
        private bool IsLoading { get; set; }
        private bool IsRolesLoading { get; set; }
        private bool IsGroupsLoading { get; set; }
        private bool IsGroupMembershipsLoading { get; set; }
        private bool IsAccessRightsLoading { get; set; }
        private bool IsReadingAuthentik { get; set; }
        private bool IsRolesLoaded { get; set; }
        private bool IsGroupsLoaded { get; set; }
        private bool IsGroupMembershipsLoaded { get; set; }
        private bool IsAccessRightsLoaded { get; set; }
        private bool IsCreating { get; set; }
        private bool IsRoleCreating { get; set; }
        private bool IsGroupCreating { get; set; }
        private bool IsSaving { get; set; }
        private bool IsRoleSaving { get; set; }
        private bool IsGroupSaving { get; set; }
        private bool IsGroupMembershipSaving { get; set; }
        private bool IsRoleAssignmentSaving { get; set; }
        private bool IsDeleting { get; set; }
        private bool IsRoleDeleting { get; set; }
        private bool IsGroupDeleting { get; set; }
        private bool IsRoleAssignmentDeleting { get; set; }
        private bool IsPasswordVisible { get; set; }
        private Guid CurrentMessageUserId { get; set; }
        private string? StatusMessage
        {
            get => null;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                if (IsWarningStatus(value))
                {
                    PostWarningMessage(value);
                    return;
                }

                PostSuccessMessage(value);
            }
        }
        private string? ErrorMessage
        {
            get => null;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    PostErrorMessage(value);
                }
            }
        }
        private UserAdministrationSection ActiveSection { get; set; } = UserAdministrationSection.General;
        private bool IsBusy =>
            IsLoading ||
            IsRolesLoading ||
            IsGroupsLoading ||
            IsGroupMembershipsLoading ||
            IsAccessRightsLoading ||
            IsReadingAuthentik ||
            IsCreating ||
            IsRoleCreating ||
            IsGroupCreating ||
            IsSaving ||
            IsRoleSaving ||
            IsGroupSaving ||
            IsGroupMembershipSaving ||
            IsRoleAssignmentSaving ||
            IsDeleting ||
            IsRoleDeleting ||
            IsGroupDeleting ||
            IsRoleAssignmentDeleting;
        private UserRoleRecord? SelectedRole => SelectedRoleId.HasValue
            ? Roles.FirstOrDefault(role => role.Id == SelectedRoleId.Value)
            : null;
        private UserAdministrationRecord? SelectedUser => SelectedUserId.HasValue
            ? Users.FirstOrDefault(user => user.Id == SelectedUserId.Value)
            : null;
        private UserGroupRecord? SelectedGroup => SelectedGroupId.HasValue
            ? Groups.FirstOrDefault(group => group.Id == SelectedGroupId.Value)
            : null;
        private string PasswordInputType => IsPasswordVisible ? "text" : "password";
        private string PasswordToggleTitle => IsPasswordVisible ? "Скрыть пароль" : "Показать пароль";

        // Загружает пользователей при первом открытии компонента.
        protected override async Task OnInitializedAsync()
        {
            await ResolveCurrentMessageUserAsync();
            await LoadUsersAsync();
        }

        [Parameter] public string Section { get; set; } = "general";
        // URL определяет раздел; история браузера и прямые ссылки работают штатно.
        protected override Task OnParametersSetAsync() => LoadSectionAsync(Section switch
        {
            "general" => UserAdministrationSection.General,
            "access" => UserAdministrationSection.AccessRights,
            "roles" => UserAdministrationSection.Roles,
            "groups" => UserAdministrationSection.Groups,
            "members" => UserAdministrationSection.GroupMembers,
            _ => throw new KeyNotFoundException("Раздел пользователей не найден.")
        });

        // Запоминает текущего локального пользователя, чтобы сообщения администрирования попадали в его правую панель.
        private async Task ResolveCurrentMessageUserAsync()
        {
            var currentUser = await UserIdentityService.EnsureCurrentUserAsync();
            CurrentMessageUserId = currentUser?.Id ?? Guid.Empty;
        }

        // Публикует пользовательское сообщение из административного UI в правую колонку.
        private void PostUserAdministrationMessage(string message, UserMessageLevel level, string title)
        {
            if (CurrentMessageUserId == Guid.Empty || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            MessageBus.SendMessage(new PostUserMessage(
                CurrentMessageUserId, BusScope.LogicalWindowId, Guid.NewGuid(),
                message,
                level,
                title));
        }

        // Публикует успешное сообщение административного UI в правую колонку.
        private void PostSuccessMessage(string message)
        {
            PostUserAdministrationMessage(message, UserMessageLevel.Success, "Пользователи");
        }

        // Публикует предупреждение административного UI в правую колонку.
        private void PostWarningMessage(string message)
        {
            PostUserAdministrationMessage(message, UserMessageLevel.Warning, "Пользователи");
        }

        // Публикует ошибку административного UI в правую колонку.
        private void PostErrorMessage(string message)
        {
            PostUserAdministrationMessage(message, UserMessageLevel.Error, "Пользователи");
        }

        // Определяет статусные сообщения, которым в правой панели нужен предупреждающий акцент.
        private static bool IsWarningStatus(string message)
        {
            return message.StartsWith("Подтвердите", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("не найден", StringComparison.OrdinalIgnoreCase);
        }

        // Загружает список пользователей без принудительного выбора конкретной записи.
        private Task LoadUsersAsync()
        {
            return LoadUsersCoreAsync(null);
        }

        // Явно читает пользователей из Authentik и обновляет локальную таблицу Users.
        private async Task ReadUsersFromAuthentikAsync()
        {
            IsReadingAuthentik = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                Users = await UserAccountsService.ReadAdministrationUsersFromAuthentikAsync();
                var selectedUser = SelectedUserId.HasValue
                    ? Users.FirstOrDefault(user => user.Id == SelectedUserId.Value)
                    : Users.FirstOrDefault();

                if (selectedUser == null)
                {
                    ClearSelection();
                }
                else
                {
                    SelectUser(selectedUser);
                }

                if (ActiveSection == UserAdministrationSection.GroupMembers && MembershipGroupId.HasValue)
                {
                    await LoadGroupMembershipsAsync();
                }

                StatusMessage = "Пользователи прочитаны из Authentik.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsReadingAuthentik = false;
            }
        }

        // Загружает список ролей без принудительного выбора конкретной записи.
        private Task LoadRolesAsync()
        {
            return LoadRolesCoreAsync(null);
        }

        // Загружает список групп без принудительного выбора конкретной записи.
        private Task LoadGroupsAsync()
        {
            return LoadGroupsCoreAsync(null);
        }

        // Загружает состав выбранной группы без смены выбранной группы.
        private Task LoadGroupMembershipsAsync()
        {
            return LoadGroupMembershipsCoreAsync(MembershipGroupId);
        }

        // Загружает данные вкладки назначений ролей без смены выбранного пространства.
        private Task LoadAccessRightsAsync()
        {
            return LoadAccessRightsCoreAsync(AccessSpaceId);
        }

        // Загружает список локальных пользователей и выбирает нужную запись.
        private async Task LoadUsersCoreAsync(Guid? userIdToSelect)
        {
            IsLoading = true;
            ErrorMessage = null;

            try
            {
                Users = await UserAccountsService.GetAdministrationUsersAsync();
                var selectedId = userIdToSelect ?? SelectedUserId;
                var selectedUser = selectedId.HasValue
                    ? Users.FirstOrDefault(user => user.Id == selectedId.Value)
                    : Users.FirstOrDefault();

                if (selectedUser == null)
                {
                    ClearSelection();
                }
                else
                {
                    SelectUser(selectedUser);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        // Добавляет или заменяет пользователя в уже загруженном списке без повторного чтения Authentik.
        private void UpsertUserInList(UserAdministrationRecord user)
        {
            Users = SortUsers(
                Users
                    .Where(existingUser => existingUser.Id != user.Id)
                    .Append(user));
        }

        // Удаляет пользователя из локального списка и выбирает следующую доступную запись.
        private void RemoveUserFromList(Guid userId)
        {
            Users = SortUsers(Users.Where(user => user.Id != userId));
            var nextUser = Users.FirstOrDefault();
            if (nextUser == null)
            {
                ClearSelection();
                return;
            }

            SelectUser(nextUser);
        }

        // Сортирует локальный список пользователей так же, как серверный административный DTO.
        private static IReadOnlyList<UserAdministrationRecord> SortUsers(
            IEnumerable<UserAdministrationRecord> users)
        {
            return users
                .OrderBy(user => string.IsNullOrWhiteSpace(user.DisplayedName) ? user.AuthentikLogin : user.DisplayedName)
                .ThenBy(user => user.AuthentikLogin)
                .ThenBy(user => user.ExternalId)
                .ToList();
        }

        // Загружает роли UserMiniApp и выбирает нужную запись.
        private async Task LoadRolesCoreAsync(Guid? roleIdToSelect)
        {
            IsRolesLoading = true;
            ErrorMessage = null;

            try
            {
                Roles = await UserRoleCatalogService.GetRolesAsync();
                IsRolesLoaded = true;
                var selectedId = roleIdToSelect ?? SelectedRoleId;
                var selectedRole = selectedId.HasValue
                    ? Roles.FirstOrDefault(role => role.Id == selectedId.Value)
                    : Roles.FirstOrDefault();

                if (selectedRole == null)
                {
                    ClearRoleSelection();
                }
                else
                {
                    SelectRole(selectedRole);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsRolesLoading = false;
            }
        }

        // Загружает группы UserMiniApp и выбирает нужную запись.
        private async Task LoadGroupsCoreAsync(Guid? groupIdToSelect)
        {
            IsGroupsLoading = true;
            ErrorMessage = null;

            try
            {
                Groups = await UserGroupService.GetUserGroupsAsync();
                IsGroupsLoaded = true;
                var selectedId = groupIdToSelect ?? SelectedGroupId;
                var selectedGroup = selectedId.HasValue
                    ? Groups.FirstOrDefault(group => group.Id == selectedId.Value)
                    : Groups.FirstOrDefault();

                if (selectedGroup == null)
                {
                    ClearGroupSelection();
                }
                else
                {
                    SelectGroup(selectedGroup);
                }

                if (!MembershipGroupId.HasValue || Groups.All(group => group.Id != MembershipGroupId.Value))
                {
                    MembershipGroupId = Groups.FirstOrDefault()?.Id;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsGroupsLoading = false;
            }
        }

        // Загружает таблицу назначений пользователей для выбранной группы.
        private async Task LoadGroupMembershipsCoreAsync(Guid? groupId)
        {
            if (!groupId.HasValue)
            {
                GroupMemberships = Array.Empty<UserGroupMembershipRecord>();
                IsGroupMembershipsLoaded = false;
                return;
            }

            IsGroupMembershipsLoading = true;
            ErrorMessage = null;

            try
            {
                MembershipGroupId = groupId;
                GroupMemberships = await UserGroupService.GetUserGroupMembershipsAsync(groupId.Value);
                GroupMemberships = Working.Open(UserFormKind.Members, groupId.Value, GroupMemberships.ToList(), GroupMemberships);
                IsGroupMembershipsLoaded = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsGroupMembershipsLoading = false;
            }
        }

        // Загружает пространства и таблицу назначений ролей с учетом режима показа.
        private async Task LoadAccessRightsCoreAsync(Guid? preferredSpaceId)
        {
            IsAccessRightsLoading = true;
            ErrorMessage = null;

            try
            {
                AccessSpaces = await UserRoleAssignmentDirectory.GetRoleAssignmentSpacesAsync();
                var nextSpaceId = preferredSpaceId.HasValue &&
                                  AccessSpaces.Any(space => space.Id == preferredSpaceId.Value)
                    ? preferredSpaceId
                    : AccessSpaces.FirstOrDefault(space => space.IsCurrent)?.Id ??
                      AccessSpaces.FirstOrDefault()?.Id;

                AccessSpaceId = nextSpaceId;
                RoleAssignments = await LoadRoleAssignmentsForCurrentViewAsync();
                EnsureAccessAssignmentSelections();
                IsAccessRightsLoaded = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsAccessRightsLoading = false;
            }
        }

        // Загружает назначения ролей либо по всем пространствам, либо по выбранному пространству.
        private async Task<IReadOnlyList<UserRoleAssignmentRecord>> LoadRoleAssignmentsForCurrentViewAsync()
        {
            if (ShowAllAccessAssignments)
            {
                var assignments = new List<UserRoleAssignmentRecord>();
                foreach (var space in AccessSpaces)
                {
                    var spaceAssignments = await UserRoleAssignmentService.GetRoleAssignmentsAsync(space.Id);
                    assignments.AddRange(spaceAssignments);
                }

                return assignments
                    .OrderBy(assignment => assignment.SpaceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(assignment => assignment.Subject, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(assignment => assignment.SubjectName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(assignment => assignment.RoleName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return AccessSpaceId.HasValue
                ? await LoadRoleAssignmentsForSelectedSpaceAsync(AccessSpaceId.Value)
                : Array.Empty<UserRoleAssignmentRecord>();
        }

        // Загружает назначения, которые действуют на выбранное пространство.
        private async Task<IReadOnlyList<UserRoleAssignmentRecord>> LoadRoleAssignmentsForSelectedSpaceAsync(Guid spaceId)
        {
            if (spaceId == Guid.Empty)
            {
                return await UserRoleAssignmentService.GetRoleAssignmentsAsync(spaceId);
            }

            var allSpacesAssignments = await UserRoleAssignmentService.GetRoleAssignmentsAsync(Guid.Empty);
            var selectedSpaceAssignments = await UserRoleAssignmentService.GetRoleAssignmentsAsync(spaceId);

            return allSpacesAssignments
                .Concat(selectedSpaceAssignments)
                .OrderBy(assignment => assignment.SpaceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.Subject, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.SubjectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.RoleName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Создает нового Authentik-пользователя приложения и выбирает его в списке.
        private async Task CreateUserAsync()
        {
            IsCreating = true;
            StatusMessage = null;
            ErrorMessage = null;

            try
            {
                var createdUser = await UserAccountsService.CreateAdministrationUserAsync();
                UpsertUserInList(createdUser);
                SelectUser(createdUser);
                if (ActiveSection == UserAdministrationSection.GroupMembers && MembershipGroupId.HasValue)
                {
                    await LoadGroupMembershipsAsync();
                }

                StatusMessage = $"Пользователь {createdUser.AuthentikLogin} создан в Authentik.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsCreating = false;
            }
        }

        // Создает новую роль и выбирает ее в редакторе ролей.
        private async Task CreateRoleAsync()
        {
            IsRoleCreating = true;
            StatusMessage = null;
            ErrorMessage = null;

            try
            {
                var createdRole = await UserRoleCatalogService.CreateRoleAsync();
                await LoadRolesCoreAsync(createdRole.Id);
                EnsureAccessAssignmentSelections();
                StatusMessage = $"Роль {createdRole.Name} создана.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsRoleCreating = false;
            }
        }

        // Создает новую группу и выбирает ее в редакторе групп.
        private async Task CreateGroupAsync()
        {
            IsGroupCreating = true;
            StatusMessage = null;
            ErrorMessage = null;

            try
            {
                var createdGroup = await UserGroupService.CreateUserGroupAsync();
                await LoadGroupsCoreAsync(createdGroup.Id);
                MembershipGroupId = createdGroup.Id;
                AccessGroupId = createdGroup.Id;
                await LoadGroupMembershipsCoreAsync(createdGroup.Id);
                StatusMessage = $"Группа {createdGroup.Name} создана.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsGroupCreating = false;
            }
        }

        // Выбирает пользователя из списка и переносит его данные в форму справа.
        private void SelectUser(UserAdministrationRecord user)
        {
            SelectedUserId = user.Id;
            DeleteConfirmationUserId = null;
            IsPasswordVisible = false;
            StatusMessage = null;
            ErrorMessage = null;
            EditModel = new UserAdministrationSaveRequest
            {
                ExternalId = user.ExternalId,
                AuthentikLogin = user.AuthentikLogin,
                DisplayedName = user.DisplayedName
            };
            EditModel = Working.Open(UserFormKind.Account, user.Id, EditModel, user);
        }

        // Выбирает роль из таблицы и переносит ее данные в форму редактора ролей.
        private void SelectRole(UserRoleRecord role)
        {
            SelectedRoleId = role.Id;
            DeleteConfirmationRoleId = null;
            StatusMessage = null;
            ErrorMessage = null;
            RoleEditModel = new UserRoleSaveRequest
            {
                Name = role.Name,
                ViewPublished = role.ViewPublished,
                ViewDraft = role.ViewDraft,
                EditDraft = role.EditDraft,
                PublishDraft = role.PublishDraft,
                AdminItems = role.AdminItems,
                AdminSpace = role.AdminSpace,
                GlobalAdmin = role.GlobalAdmin
            };
            RoleEditModel = Working.Open(UserFormKind.Role, role.Id, RoleEditModel, role);
        }

        // Выбирает группу из таблицы и переносит ее данные в форму редактора групп.
        private void SelectGroup(UserGroupRecord group)
        {
            SelectedGroupId = group.Id;
            DeleteConfirmationGroupId = null;
            StatusMessage = null;
            ErrorMessage = null;
            GroupEditModel = new UserGroupSaveRequest
            {
                Name = group.Name
            };
            GroupEditModel = Working.Open(UserFormKind.Group, group.Id, GroupEditModel, group);
        }

        // Обновляет Authentik-логин и локальное отображаемое имя через UserMiniApp connector.
        private async Task SaveUserAsync()
        {
            if (!SelectedUserId.HasValue)
            {
                return;
            }

            IsSaving = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                var isPasswordChanged = !string.IsNullOrWhiteSpace(EditModel.Password);
                AccountChanged(); await SaveWorkingSetAsync();
                var savedUser = Working.Result<UserAdministrationRecord>(UserFormKind.Account, SelectedUserId.Value);

                UpsertUserInList(savedUser);
                SelectUser(savedUser);
                if (ActiveSection == UserAdministrationSection.GroupMembers && MembershipGroupId.HasValue)
                {
                    await LoadGroupMembershipsAsync();
                }

                StatusMessage = isPasswordChanged
                    ? "Пользователь сохранен. Пароль изменен."
                    : "Пользователь сохранен.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsSaving = false;
            }
        }

        // Сохраняет имя и права выбранной роли.
        private async Task SaveRoleAsync()
        {
            if (!SelectedRoleId.HasValue)
            {
                return;
            }

            IsRoleSaving = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                RoleChanged(); await SaveWorkingSetAsync();
                var savedRole = Working.Result<UserRoleRecord>(UserFormKind.Role, SelectedRoleId.Value);
                await LoadRolesCoreAsync(savedRole.Id);
                if (IsAccessRightsLoaded)
                {
                    await LoadAccessRightsAsync();
                }

                StatusMessage = "Роль сохранена.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsRoleSaving = false;
            }
        }

        // Сохраняет имя выбранной группы пользователей.
        private async Task SaveGroupAsync()
        {
            if (!SelectedGroupId.HasValue)
            {
                return;
            }

            IsGroupSaving = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                GroupChanged(); await SaveWorkingSetAsync();
                var savedGroup = Working.Result<UserGroupRecord>(UserFormKind.Group, SelectedGroupId.Value);
                await LoadGroupsCoreAsync(savedGroup.Id);
                if (IsAccessRightsLoaded)
                {
                    await LoadAccessRightsAsync();
                }

                StatusMessage = "Группа сохранена.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsGroupSaving = false;
            }
        }

        // Сохраняет полный состав выбранной группы пользователей.
        private async Task SaveGroupMembershipsAsync()
        {
            if (!MembershipGroupId.HasValue)
            {
                return;
            }

            IsGroupMembershipSaving = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                var request = new UserGroupMembershipSaveRequest
                {
                    UserIds = GroupMemberships
                        .Where(membership => membership.IsMember)
                        .Select(membership => membership.UserId)
                        .ToList()
                };

                MembersChanged(); await SaveWorkingSetAsync();
                GroupMemberships = Working.Open(UserFormKind.Members, MembershipGroupId.Value, Working.Result<IReadOnlyList<UserGroupMembershipRecord>>(UserFormKind.Members, MembershipGroupId.Value).ToList());
                await LoadGroupsCoreAsync(SelectedGroupId);
                StatusMessage = "Состав группы сохранен.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsGroupMembershipSaving = false;
            }
        }

        // Создает назначение выбранной группы на выбранную роль в выбранном пространстве.
        private async Task AddGroupRoleAssignmentAsync()
        {
            if (!AccessSpaceId.HasValue || !AccessGroupId.HasValue || !AccessRoleId.HasValue)
            {
                ErrorMessage = "Выберите пространство, группу и роль.";
                return;
            }

            IsRoleAssignmentSaving = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                await UserRoleAssignmentService.CreateRoleAssignmentAsync(
                    AccessSpaceId.Value,
                    new UserRoleAssignmentSaveRequest
                    {
                        Subject = AccessSpaceId.Value == Guid.Empty
                            ? UserRoleAssignmentSubjects.AllSpaces
                            : UserRoleAssignmentSubjects.Space,
                        SubjectId = AccessGroupId.Value,
                        AssignmentType = UserRoleAssignmentTypes.GroupToRole,
                        RoleId = AccessRoleId.Value
                    });
                RoleAssignments = await LoadRoleAssignmentsForCurrentViewAsync();
                StatusMessage = "Назначение роли сохранено.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsRoleAssignmentSaving = false;
            }
        }

        // Удаляет назначение роли из таблицы прав выбранного пространства.
        private async Task DeleteRoleAssignmentAsync(Guid assignmentId)
        {
            IsRoleAssignmentDeleting = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                var deleted = await UserRoleAssignmentService.DeleteRoleAssignmentAsync(assignmentId);
                RoleAssignments = await LoadRoleAssignmentsForCurrentViewAsync();
                StatusMessage = deleted ? "Назначение роли удалено." : "Назначение роли не найдено.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsRoleAssignmentDeleting = false;
            }
        }

        // Включает второй шаг подтверждения удаления выбранного пользователя.
        private void RequestDeleteConfirmation()
        {
            DeleteConfirmationUserId = SelectedUserId;
            StatusMessage = "Подтвердите удаление пользователя.";
            ErrorMessage = null;
        }

        // Отменяет подтверждение удаления выбранного пользователя.
        private void CancelDeleteConfirmation()
        {
            DeleteConfirmationUserId = null;
            StatusMessage = null;
        }

        // Включает второй шаг подтверждения удаления выбранной роли.
        private void RequestRoleDeleteConfirmation()
        {
            DeleteConfirmationRoleId = SelectedRoleId;
            StatusMessage = "Подтвердите удаление роли.";
            ErrorMessage = null;
        }

        // Отменяет подтверждение удаления выбранной роли.
        private void CancelRoleDeleteConfirmation()
        {
            DeleteConfirmationRoleId = null;
            StatusMessage = null;
        }

        // Включает второй шаг подтверждения удаления выбранной группы.
        private void RequestGroupDeleteConfirmation()
        {
            DeleteConfirmationGroupId = SelectedGroupId;
            StatusMessage = "Подтвердите удаление группы.";
            ErrorMessage = null;
        }

        // Отменяет подтверждение удаления выбранной группы.
        private void CancelGroupDeleteConfirmation()
        {
            DeleteConfirmationGroupId = null;
            StatusMessage = null;
        }

        // Удаляет выбранного локального пользователя и его технические свойства.
        private async Task DeleteSelectedUserAsync()
        {
            if (!SelectedUserId.HasValue)
            {
                return;
            }

            IsDeleting = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                var deletedUserId = SelectedUserId.Value;
                var deleted = await UserAccountsService.DeleteAdministrationUserAsync(deletedUserId);
                var statusMessage = deleted ? "Пользователь удален." : "Пользователь не найден.";
                SelectedUserId = null;
                DeleteConfirmationUserId = null;
                RemoveUserFromList(deletedUserId);
                if (IsGroupsLoaded)
                {
                    await LoadGroupsAsync();
                }

                if (ActiveSection == UserAdministrationSection.GroupMembers && MembershipGroupId.HasValue)
                {
                    await LoadGroupMembershipsAsync();
                }

                StatusMessage = statusMessage;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsDeleting = false;
            }
        }

        // Удаляет выбранную роль из UserMiniApp storage.
        private async Task DeleteSelectedRoleAsync()
        {
            if (!SelectedRoleId.HasValue)
            {
                return;
            }

            IsRoleDeleting = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                var deleted = await UserRoleCatalogService.DeleteRoleAsync(SelectedRoleId.Value);
                var statusMessage = deleted ? "Роль удалена." : "Роль не найдена.";
                SelectedRoleId = null;
                DeleteConfirmationRoleId = null;
                await LoadRolesAsync();
                if (IsAccessRightsLoaded)
                {
                    await LoadAccessRightsAsync();
                }

                StatusMessage = statusMessage;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsRoleDeleting = false;
            }
        }

        // Удаляет выбранную группу, если в ней нет назначенных пользователей.
        private async Task DeleteSelectedGroupAsync()
        {
            if (!SelectedGroupId.HasValue)
            {
                return;
            }

            IsGroupDeleting = true;
            ErrorMessage = null;
            StatusMessage = null;

            try
            {
                var groupId = SelectedGroupId.Value;
                var deleted = await UserGroupService.DeleteUserGroupAsync(groupId);
                var statusMessage = deleted ? "Группа удалена." : "Группа не найдена.";
                SelectedGroupId = null;
                DeleteConfirmationGroupId = null;
                if (MembershipGroupId == groupId)
                {
                    MembershipGroupId = null;
                    GroupMemberships = Array.Empty<UserGroupMembershipRecord>();
                }

                await LoadGroupsAsync();
                await LoadGroupMembershipsAsync();
                if (IsAccessRightsLoaded)
                {
                    await LoadAccessRightsAsync();
                }

                StatusMessage = statusMessage;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsGroupDeleting = false;
            }
        }

        // Загружает данные раздела, выбранного маршрутом.
        private async Task LoadSectionAsync(UserAdministrationSection tab)
        {
            ActiveSection = tab;
            ErrorMessage = null;
            StatusMessage = null;
            StateHasChanged();

            if (tab == UserAdministrationSection.Roles && !IsRolesLoaded)
            {
                await LoadRolesAsync();
                return;
            }

            if (tab == UserAdministrationSection.AccessRights)
            {
                if (!IsRolesLoaded)
                {
                    await LoadRolesAsync();
                }

                if (!IsGroupsLoaded)
                {
                    await LoadGroupsAsync();
                }

                if (!IsAccessRightsLoaded)
                {
                    await LoadAccessRightsAsync();
                }
                else
                {
                    EnsureAccessAssignmentSelections();
                }

                return;
            }

            if (tab == UserAdministrationSection.Groups && !IsGroupsLoaded)
            {
                await LoadGroupsAsync();
                return;
            }

            if (tab == UserAdministrationSection.GroupMembers)
            {
                if (!IsGroupsLoaded)
                {
                    await LoadGroupsAsync();
                }

                if (!IsGroupMembershipsLoaded && MembershipGroupId.HasValue)
                {
                    await LoadGroupMembershipsAsync();
                }
            }
        }

        // Переключает выбранную группу во вкладке назначения пользователей.
        private async Task ChangeMembershipGroupAsync(ChangeEventArgs args)
        {
            var value = args.Value?.ToString();
            if (!Guid.TryParse(value, out var groupId))
            {
                MembershipGroupId = null;
                GroupMemberships = Array.Empty<UserGroupMembershipRecord>();
                return;
            }

            await LoadGroupMembershipsCoreAsync(groupId);
        }

        // Переключает пространство на вкладке назначения ролей.
        private async Task ChangeAccessSpaceAsync(ChangeEventArgs args)
        {
            var value = args.Value?.ToString();
            if (!Guid.TryParse(value, out var spaceId))
            {
                AccessSpaceId = null;
                RoleAssignments = Array.Empty<UserRoleAssignmentRecord>();
                return;
            }

            await LoadAccessRightsCoreAsync(spaceId);
        }

        // Переключает режим показа всех назначений или только выбранного пространства.
        private async Task ChangeShowAllAccessAssignmentsAsync(ChangeEventArgs args)
        {
            ShowAllAccessAssignments = args.Value is bool value
                ? value
                : bool.TryParse(args.Value?.ToString(), out var parsedValue) && parsedValue;
            RoleAssignments = await LoadRoleAssignmentsForCurrentViewAsync();
        }

        // Переключает группу для нового назначения роли.
        private void ChangeAccessGroup(ChangeEventArgs args)
        {
            var value = args.Value?.ToString();
            AccessGroupId = Guid.TryParse(value, out var groupId)
                ? groupId
                : null;
        }

        // Переключает роль для нового назначения.
        private void ChangeAccessRole(ChangeEventArgs args)
        {
            var value = args.Value?.ToString();
            AccessRoleId = Guid.TryParse(value, out var roleId)
                ? roleId
                : null;
        }

        // Изменяет локальное состояние чекбокса участника группы до сохранения.
        private void SetGroupMembership(Guid userId, bool isMember)
        {
            var membership = GroupMemberships.FirstOrDefault(item => item.UserId == userId);
            if (membership == null)
            {
                return;
            }

            membership.IsMember = isMember;
                MembersChanged();
        }

        // Отмечает всех локально загруженных пользователей как участников выбранной группы.
        private void SelectAllGroupMemberships()
        {
            foreach (var membership in GroupMemberships)
            {
                membership.IsMember = true;
            }

            MembersChanged();
        }

        // Выбирает доступные группу и роль для формы добавления назначения.
        private void EnsureAccessAssignmentSelections()
        {
            if (!AccessGroupId.HasValue || Groups.All(group => group.Id != AccessGroupId.Value))
            {
                AccessGroupId = Groups.FirstOrDefault()?.Id;
            }

            if (!AccessRoleId.HasValue || Roles.All(role => role.Id != AccessRoleId.Value))
            {
                AccessRoleId = Roles.FirstOrDefault()?.Id;
            }
        }

        // Возвращает CSS-класс кнопки пользователя в левом списке.
        private string GetUserButtonClass(UserAdministrationRecord user)
        {
            return SelectedUserId == user.Id
                ? "users-admin__user users-admin__user--selected"
                : "users-admin__user";
        }

        // Возвращает CSS-класс строки роли в таблице ролей.
        private string GetRoleRowClass(UserRoleRecord role)
        {
            return SelectedRoleId == role.Id
                ? "users-admin__role-row users-admin__role-row--selected"
                : "users-admin__role-row";
        }

        // Возвращает CSS-класс строки группы в таблице групп.
        private string GetGroupRowClass(UserGroupRecord group)
        {
            return SelectedGroupId == group.Id
                ? "users-admin__group-row users-admin__group-row--selected"
                : "users-admin__group-row";
        }

        // Возвращает отображаемое имя для строки пользователя в списке.
        private static string GetUserListName(UserAdministrationRecord user)
        {
            return string.IsNullOrWhiteSpace(user.DisplayedName)
                ? user.AuthentikLogin
                : user.DisplayedName;
        }

        // Очищает форму, когда в Authentik нет пользователей приложения.
        private void ClearSelection()
        {
            SelectedUserId = null;
            DeleteConfirmationUserId = null;
            ActiveSection = UserAdministrationSection.General;
            IsPasswordVisible = false;
            EditModel = new UserAdministrationSaveRequest();
        }

        // Очищает выбор роли и форму редактора ролей.
        private void ClearRoleSelection()
        {
            SelectedRoleId = null;
            DeleteConfirmationRoleId = null;
            RoleEditModel = new UserRoleSaveRequest();
        }

        // Очищает выбор группы и форму редактора групп.
        private void ClearGroupSelection()
        {
            SelectedGroupId = null;
            DeleteConfirmationGroupId = null;
            GroupEditModel = new UserGroupSaveRequest();
        }

        // Переключает режим отображения поля нового пароля.
        private void TogglePasswordVisibility()
        {
            IsPasswordVisible = !IsPasswordVisible;
        }

        // Возвращает текстовый маркер выбранного права роли.
        private static string RenderPermissionMark(bool value)
        {
            return value ? "x" : string.Empty;
        }

        // Разделы административных страниц пользователя.
        private enum UserAdministrationSection
        {
            General,
            AccessRights,
            Roles,
            Groups,
            GroupMembers
        }
    }
}

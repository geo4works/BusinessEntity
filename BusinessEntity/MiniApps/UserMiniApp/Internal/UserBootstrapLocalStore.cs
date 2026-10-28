using BusinessEntity.Services;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using System.Text.Json;

using static BusinessEntity.MiniApps.UserMiniApp.Internal.UserValueRules;
namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Владеет локальными записями bootstrap, отделяя их от внешней конфигурации Authentik.
internal sealed class UserBootstrapLocalStore
{
    private readonly IUserMiniAppRepository<UserDto> _userRepository;
    private readonly IUserMiniAppRepository<UserRoleDto> _roleRepository;
    private readonly IUserMiniAppRepository<UserGroupDto> _groupRepository;
    private readonly IUserMiniAppRepository<UserGroupMemberDto> _groupMemberRepository;
    private readonly IUserMiniAppRepository<UserRoleAssignmentDto> _roleAssignmentRepository;
    // Пять репозиториев образуют локальную структуру пользователей, групп и назначений.
    public UserBootstrapLocalStore(IUserMiniAppRepository<UserDto> userRepository, IUserMiniAppRepository<UserRoleDto> roleRepository, IUserMiniAppRepository<UserGroupDto> groupRepository, IUserMiniAppRepository<UserGroupMemberDto> groupMemberRepository, IUserMiniAppRepository<UserRoleAssignmentDto> roleAssignmentRepository)
    { _userRepository = userRepository; _roleRepository = roleRepository; _groupRepository = groupRepository; _groupMemberRepository = groupMemberRepository; _roleAssignmentRepository = roleAssignmentRepository; }
    // Синхронизирует только собственный локальный набор bootstrap.
    public async Task EnsureAsync(AuthentikUserRecord akadmin, AuthentikUserRecord admin, string groupName, CancellationToken ct)
    {
        var users = (await _userRepository.GetAllAsync(null, ct)).ToList();
        await UpsertLocalUserFromAuthentikAsync(akadmin, users, ct);
        var localAdmin = await UpsertLocalUserFromAuthentikAsync(admin, users, ct);
        var group = await EnsureLocalAdminGroupAsync(groupName, ct);
        await EnsureLocalGroupMemberAsync(localAdmin.Id, group.Id, ct);
        await EnsureLocalAdminRoleAssignmentAsync(group.Id, ct);
    }

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
}

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

// UserGroupService владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserGroupService
{
        private readonly IUserMiniAppRepository<UserDto> _userRepository;
        private readonly IUserMiniAppRepository<UserGroupDto> _groupRepository;
        private readonly IUserMiniAppRepository<UserGroupMemberDto> _groupMemberRepository;
        private readonly IUserMiniAppRepository<UserRoleAssignmentDto> _roleAssignmentRepository;
        private readonly UserIdentityService _identityService;
        // Получает зависимости выделенного сценария.
        public UserGroupService(IUserMiniAppRepository<UserDto> userRepository, IUserMiniAppRepository<UserGroupDto> groupRepository, IUserMiniAppRepository<UserGroupMemberDto> groupMemberRepository, IUserMiniAppRepository<UserRoleAssignmentDto> roleAssignmentRepository, UserIdentityService identityService)
        { _userRepository = userRepository; _groupRepository = groupRepository; _groupMemberRepository = groupMemberRepository; _roleAssignmentRepository = roleAssignmentRepository; _identityService = identityService; }
        // Выполняет сценарий GetUserGroupsAsync внутри владельца User.
        public async Task<IReadOnlyList<UserGroupRecord>> GetUserGroupsAsync(CancellationToken cancellationToken = default)
        {
            var groups = await _groupRepository.GetAllAsync(null, cancellationToken);
            var memberships = await _groupMemberRepository.GetAllAsync(null, cancellationToken);
            return groups
                .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => MapGroupRecord(group, memberships.Count(membership => membership.GroupId == group.Id)))
                .ToList();
        }

        // Выполняет сценарий CreateUserGroupAsync внутри владельца User.
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

        // Выполняет сценарий UpdateUserGroupAsync внутри владельца User.
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

        // Выполняет сценарий DeleteUserGroupAsync внутри владельца User.
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

        // Выполняет сценарий GetUserGroupMembershipsAsync внутри владельца User.
        public async Task<IReadOnlyList<UserGroupMembershipRecord>> GetUserGroupMembershipsAsync(
            Guid groupId,
            CancellationToken cancellationToken = default)
        {
            await EnsureGroupExistsAsync(groupId, cancellationToken);
            await _identityService.EnsureSystemUsersAsync(cancellationToken);
            var users = await _userRepository.GetAllAsync(null, cancellationToken);
            var members = await _groupMemberRepository.GetAllAsync(
                membership => membership.GroupId == groupId,
                cancellationToken);
            var memberUserIds = members
                .Select(membership => membership.UserId)
                .ToHashSet();

            return MapGroupMembershipRecords(users, memberUserIds);
        }

        // Выполняет сценарий UpdateUserGroupMembershipsAsync внутри владельца User.
        public async Task<IReadOnlyList<UserGroupMembershipRecord>> UpdateUserGroupMembershipsAsync(
            Guid groupId,
            UserGroupMembershipSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            await EnsureGroupExistsAsync(groupId, cancellationToken);
            await _identityService.EnsureSystemUsersAsync(cancellationToken);
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

        // Выполняет сценарий GetCurrentUserGroupNamesAsync внутри владельца User.
        public async Task<IReadOnlyList<string>> GetCurrentUserGroupNamesAsync(CancellationToken cancellationToken = default)
        {
            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
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

        // Выполняет сценарий EnsureGroupExistsAsync внутри владельца User.
        internal async Task EnsureGroupExistsAsync(Guid groupId, CancellationToken cancellationToken)
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
        // Выдаёт read-only список групп своему сценарию назначений.
        internal Task<IReadOnlyList<UserGroupDto>> ReadGroupsAsync(CancellationToken ct) => _groupRepository.GetAllAsync(null, ct);

}

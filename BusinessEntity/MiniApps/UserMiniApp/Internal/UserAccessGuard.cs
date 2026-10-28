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

// UserAccessGuard владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserAccessGuard
{
        private readonly IUserMiniAppRepository<UserRoleDto> _roleRepository;
        private readonly IUserMiniAppRepository<UserGroupMemberDto> _groupMemberRepository;
        private readonly IUserMiniAppRepository<UserRoleAssignmentDto> _roleAssignmentRepository;
        private readonly UserSpaceContentAccessHelper _spaceContentAccessHelper;
        private readonly UserIdentityService _identityService;
        // Получает зависимости выделенного сценария.
        public UserAccessGuard(IUserMiniAppRepository<UserRoleDto> roleRepository, IUserMiniAppRepository<UserGroupMemberDto> groupMemberRepository, IUserMiniAppRepository<UserRoleAssignmentDto> roleAssignmentRepository, UserSpaceContentAccessHelper spaceContentAccessHelper, UserIdentityService identityService)
        { _roleRepository = roleRepository; _groupMemberRepository = groupMemberRepository; _roleAssignmentRepository = roleAssignmentRepository; _spaceContentAccessHelper = spaceContentAccessHelper; _identityService = identityService; }
        // Выполняет сценарий EnsureCurrentUserCanAdminRoleAssignmentsAsync внутри владельца User.
        internal async Task EnsureCurrentUserCanAdminRoleAssignmentsAsync(
            Guid spaceId,
            CancellationToken cancellationToken)
        {
            var currentUser = await _identityService.GetCurrentUserAsync(cancellationToken);
            if (IsAccessAdmin(currentUser))
            {
                return;
            }

            if (currentUser?.IsAuthenticated != true)
            {
                throw new UnauthorizedAccessException("Нет прав на администрирование пространства.");
            }

            var localUser = await _identityService.EnsureCurrentUserAsync(cancellationToken);
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

        // Выполняет сценарий CurrentUserHasGlobalAdminRoleAsync внутри владельца User.
        internal async Task<bool> CurrentUserHasGlobalAdminRoleAsync(
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
}

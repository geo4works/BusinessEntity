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

// UserRoleAssignmentService владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserRoleAssignmentService
{
        private readonly IUserMiniAppRepository<UserRoleAssignmentDto> _roleAssignmentRepository;
        private readonly UserRoleCatalogService _roleCatalogService;
        private readonly UserRoleAssignmentDirectory _roleAssignmentDirectory;
        private readonly UserAccessGuard _accessGuard;
        // Получает зависимости выделенного сценария.
        public UserRoleAssignmentService(IUserMiniAppRepository<UserRoleAssignmentDto> roleAssignmentRepository, UserRoleCatalogService roleCatalogService, UserRoleAssignmentDirectory roleAssignmentDirectory, UserAccessGuard accessGuard)
        { _roleAssignmentRepository = roleAssignmentRepository; _roleCatalogService = roleCatalogService; _roleAssignmentDirectory = roleAssignmentDirectory; _accessGuard = accessGuard; }
        // Выполняет сценарий GetRoleAssignmentsAsync внутри владельца User.
        public async Task<IReadOnlyList<UserRoleAssignmentRecord>> GetRoleAssignmentsAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default)
        {
            var assignmentSubject = spaceId == Guid.Empty
                ? UserRoleAssignmentSubjects.AllSpaces
                : UserRoleAssignmentSubjects.Space;
            var spaceName = await _roleAssignmentDirectory.GetRoleAssignmentSpaceNameAsync(spaceId, assignmentSubject, cancellationToken);
            var assignments = await _roleAssignmentRepository.GetAllAsync(
                assignment => assignment.SpaceId == spaceId &&
                              assignment.Subject == assignmentSubject,
                cancellationToken);
            var groups = await _roleAssignmentDirectory.ReadGroupsAsync(cancellationToken);
            var users = await _roleAssignmentDirectory.ReadUsersAsync(cancellationToken);
            var roles = await _roleAssignmentDirectory.ReadRolesAsync(cancellationToken);

            return MapRoleAssignmentRecords(assignments, spaceName, groups, users, roles);
        }

        // Выполняет сценарий CreateRoleAssignmentAsync внутри владельца User.
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
            await _accessGuard.EnsureCurrentUserCanAdminRoleAssignmentsAsync(assignmentSpaceId, cancellationToken);

            var spaceName = await _roleAssignmentDirectory.GetRoleAssignmentSpaceNameAsync(assignmentSpaceId, assignmentSubject, cancellationToken);
            var assignmentType = NormalizeAssignmentType(request.AssignmentType);
            await _roleCatalogService.EnsureRoleExistsAsync(request.RoleId, cancellationToken);
            await _roleAssignmentDirectory.EnsureAssignmentSubjectExistsAsync(request.SubjectId, assignmentType, cancellationToken);

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
                var existingGroups = await _roleAssignmentDirectory.ReadGroupsAsync(cancellationToken);
                var existingUsers = await _roleAssignmentDirectory.ReadUsersAsync(cancellationToken);
                var existingRoles = await _roleAssignmentDirectory.ReadRolesAsync(cancellationToken);
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
            var groups = await _roleAssignmentDirectory.ReadGroupsAsync(cancellationToken);
            var users = await _roleAssignmentDirectory.ReadUsersAsync(cancellationToken);
            var roles = await _roleAssignmentDirectory.ReadRolesAsync(cancellationToken);
            return MapRoleAssignmentRecords(new[] { assignmentDto }, spaceName, groups, users, roles).First();
        }

        // Выполняет сценарий DeleteRoleAssignmentAsync внутри владельца User.
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

            await _accessGuard.EnsureCurrentUserCanAdminRoleAssignmentsAsync(assignment.SpaceId, cancellationToken);
            await _roleAssignmentRepository.DeleteAsync(assignmentId, cancellationToken);
            return true;
        }
}

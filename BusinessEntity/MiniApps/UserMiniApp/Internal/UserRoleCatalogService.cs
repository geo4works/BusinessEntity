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

// UserRoleCatalogService владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserRoleCatalogService
{
        private readonly IUserMiniAppRepository<UserRoleDto> _roleRepository;
        private readonly IUserMiniAppRepository<UserRoleAssignmentDto> _roleAssignmentRepository;
        // Получает зависимости выделенного сценария.
        public UserRoleCatalogService(IUserMiniAppRepository<UserRoleDto> roleRepository, IUserMiniAppRepository<UserRoleAssignmentDto> roleAssignmentRepository)
        { _roleRepository = roleRepository; _roleAssignmentRepository = roleAssignmentRepository; }
        // Выполняет сценарий EnsureSystemRolesAsync внутри владельца User.
        public async Task EnsureSystemRolesAsync(CancellationToken cancellationToken = default)
        {
            var roles = (await _roleRepository.GetAllAsync(null, cancellationToken)).ToList();
            await EnsureRoleAsync(roles, SystemAdminRoleName, BuildAllPermissionString(), isSystem: true, cancellationToken);
            await EnsureRoleAsync(roles, GuestRoleName, BuildReadPublishedPermissionString(), isSystem: false, cancellationToken);
            await EnsureRoleAsync(roles, ReadersRoleName, BuildReadPublishedPermissionString(), isSystem: false, cancellationToken);
        }

        // Выполняет сценарий GetRolesAsync внутри владельца User.
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

        // Выполняет сценарий CreateRoleAsync внутри владельца User.
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

        // Выполняет сценарий UpdateRoleAsync внутри владельца User.
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

        // Выполняет сценарий DeleteRoleAsync внутри владельца User.
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

        // Выполняет сценарий EnsureRoleAsync внутри владельца User.
        internal async Task EnsureRoleAsync(
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

        // Выполняет сценарий EnsureRoleExistsAsync внутри владельца User.
        internal async Task EnsureRoleExistsAsync(Guid roleId, CancellationToken cancellationToken)
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
}

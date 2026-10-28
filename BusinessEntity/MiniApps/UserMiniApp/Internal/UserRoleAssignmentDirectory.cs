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

// UserRoleAssignmentDirectory владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserRoleAssignmentDirectory
{
        private readonly IUserMiniAppRepository<UserDto> _userRepository;
        private readonly IUserMiniAppRepository<UserRoleDto> _roleRepository;
        private readonly IDataProviderConnector _data;
        private readonly IUserContextService _userContextService;
        private readonly UserGroupService _groupService;
        // Получает зависимости выделенного сценария.
        public UserRoleAssignmentDirectory(IUserMiniAppRepository<UserDto> userRepository, IUserMiniAppRepository<UserRoleDto> roleRepository, IDataProviderConnector data, IUserContextService userContextService, UserGroupService groupService)
        { _userRepository = userRepository; _roleRepository = roleRepository; _data = data; _userContextService = userContextService; _groupService = groupService; }
        // Выполняет сценарий GetRoleAssignmentSpacesAsync внутри владельца User.
        public async Task<IReadOnlyList<UserSpaceRecord>> GetRoleAssignmentSpacesAsync(CancellationToken cancellationToken = default)
        {
            var currentSpaceId = _userContextService.CurrentSpaceId;
            var spaces = (await _data.GetAllAsync(cancellationToken)).Where(entity => entity.EntityType == global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id).ToArray();
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

        // Выполняет сценарий EnsureAssignmentSubjectExistsAsync внутри владельца User.
        internal async Task EnsureAssignmentSubjectExistsAsync(
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
                await _groupService.EnsureGroupExistsAsync(subjectId, cancellationToken);
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

        // Выполняет сценарий GetRequiredSpaceNameAsync внутри владельца User.
        internal async Task<string> GetRequiredSpaceNameAsync(Guid spaceId, CancellationToken cancellationToken)
        {
            if (spaceId == Guid.Empty)
            {
                throw new ArgumentException("Пространство не выбрано.", nameof(spaceId));
            }

            var space = await _data.GetByIdAsync(spaceId, cancellationToken);
            if (space == null || space.EntityType != global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id)
            {
                throw new KeyNotFoundException("Пространство не найдено.");
            }

            return space.Name;
        }

        // Выполняет сценарий GetRoleAssignmentSpaceNameAsync внутри владельца User.
        internal async Task<string> GetRoleAssignmentSpaceNameAsync(
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
        // Справочник субъектов назначения даёт значения, не раскрывая репозитории.
        internal Task<IReadOnlyList<UserDto>> ReadUsersAsync(CancellationToken ct) => _userRepository.GetAllAsync(null, ct);
        // Справочник ролей используется построением подписей назначений.
        internal Task<IReadOnlyList<UserRoleDto>> ReadRolesAsync(CancellationToken ct) => _roleRepository.GetAllAsync(null, ct);
        // Группы читает их собственный сервис, проверка существования там же.
        internal Task<IReadOnlyList<UserGroupDto>> ReadGroupsAsync(CancellationToken ct) => _groupService.ReadGroupsAsync(ct);

}

using BusinessEntity.Contracts;
using BusinessEntity.Core.BaseClasses.Relations;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.Services;
using System.Text.Json;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Рассчитывает space-level права пользователя и наличие доступного контента в пространстве.
internal sealed class UserSpaceContentAccessHelper
{
    private readonly IUserMiniAppRepository<UserRoleDto> _roleRepository;
    private readonly IUserMiniAppRepository<UserGroupMemberDto> _groupMemberRepository;
    private readonly IUserMiniAppRepository<UserRoleAssignmentDto> _roleAssignmentRepository;
    private readonly IAsyncRepository<BusinessEntityDto> _businessEntityRepository;
    private readonly IAsyncRepository<BusinessEntityRelationDto> _businessEntityRelationRepository;
    private readonly IAsyncRepository<BusinessEntityDataDto> _businessEntityDataRepository;
    private readonly IUserContextService _userContextService;

    // Подключает repositories user mini-app и data-provider для расчета прав и обхода дерева пространства.
    public UserSpaceContentAccessHelper(
        IUserMiniAppRepository<UserRoleDto> roleRepository,
        IUserMiniAppRepository<UserGroupMemberDto> groupMemberRepository,
        IUserMiniAppRepository<UserRoleAssignmentDto> roleAssignmentRepository,
        IAsyncRepository<BusinessEntityDto> businessEntityRepository,
        IAsyncRepository<BusinessEntityRelationDto> businessEntityRelationRepository,
        IAsyncRepository<BusinessEntityDataDto> businessEntityDataRepository,
        IUserContextService userContextService)
    {
        _roleRepository = roleRepository;
        _groupMemberRepository = groupMemberRepository;
        _roleAssignmentRepository = roleAssignmentRepository;
        _businessEntityRepository = businessEntityRepository;
        _businessEntityRelationRepository = businessEntityRelationRepository;
        _businessEntityDataRepository = businessEntityDataRepository;
        _userContextService = userContextService;
    }

    // Возвращает effective permissions пользователя в заданном пространстве.
    public async Task<UserEffectivePermissions> GetEffectivePermissionsForSpaceAsync(
        Guid userId,
        Guid spaceId,
        bool isAnonymous,
        CancellationToken cancellationToken = default)
    {
        var result = UserEffectivePermissions.Deny(userId, spaceId, isAnonymous);
        if (userId == Guid.Empty || spaceId == Guid.Empty)
        {
            return result;
        }

        var groupIds = (await _groupMemberRepository.GetAllAsync(
                membership => membership.UserId == userId,
                cancellationToken))
            .Select(membership => membership.GroupId)
            .ToHashSet();

        var assignments = await _roleAssignmentRepository.GetAllAsync(
            assignment =>
                (assignment.SpaceId == spaceId || assignment.SpaceId == Guid.Empty) &&
                (assignment.Subject == UserRoleAssignmentSubjects.Space ||
                 assignment.Subject == UserRoleAssignmentSubjects.AllSpaces),
            cancellationToken);

        var roleIds = assignments
            .Where(assignment => IsAssignmentApplicableToUser(assignment, userId, groupIds))
            .Select(assignment => assignment.RoleId)
            .Distinct()
            .ToHashSet();
        if (roleIds.Count == 0)
        {
            return result;
        }

        var roles = await _roleRepository.GetAllAsync(role => roleIds.Contains(role.Id), cancellationToken);
        foreach (var role in roles)
        {
            ApplyRole(result, role);
        }

        if (isAnonymous)
        {
            NormalizeAnonymousPermissions(result);
        }

        return result;
    }

    // Проверяет, есть ли в пространстве хотя бы один объект, доступный пользователю по его effective permissions.
    public async Task<bool> HasAccessibleObjectsInSpaceAsync(
        Guid userId,
        Guid spaceId,
        bool isAnonymous,
        CancellationToken cancellationToken = default)
    {
        var permissions = await GetEffectivePermissionsForSpaceAsync(userId, spaceId, isAnonymous, cancellationToken);
        return await SpaceContainsReadableObjectAsync(spaceId, permissions, cancellationToken);
    }

    // Возвращает документы пространства, которые пользователь может открыть по его effective permissions.
    public async Task<IReadOnlyList<UserAccessibleDocumentRecord>> GetAccessibleDocumentsInSpaceAsync(
        Guid userId,
        Guid spaceId,
        bool isAnonymous,
        CancellationToken cancellationToken = default)
    {
        var permissions = await GetEffectivePermissionsForSpaceAsync(userId, spaceId, isAnonymous, cancellationToken);
        var entities = (await _businessEntityRepository.GetAllAsync(null, ct: cancellationToken))
            .ToDictionary(entity => entity.Id);
        if (!entities.TryGetValue(spaceId, out var space) || space.EntityType != BusinessEntityTypeEnum.Space)
        {
            return Array.Empty<UserAccessibleDocumentRecord>();
        }

        var relations = await _businessEntityRelationRepository.GetAllAsync(
            relation =>
                relation.RelationType == BusinessEntityRelationTypeEnum.Contains.ToString() ||
                relation.RelationType == BusinessEntityRelationTypeEnum.RelatesTo.ToString(),
            ct: cancellationToken);
        var childrenByParentId = relations
            .Where(relation => relation.RelationType == BusinessEntityRelationTypeEnum.Contains.ToString())
            .GroupBy(relation => relation.ObjectAId)
            .ToDictionary(
                group => group.Key,
                group => OrderEntityIds(group.Select(relation => relation.ObjectBId), entities));

        var records = new List<UserAccessibleDocumentRecord>();
        var visitedTreeIds = new HashSet<Guid>();
        var addedDocumentIds = new HashSet<Guid>();

        await VisitChildrenAsync(spaceId);

        var relatedObjectIds = relations
            .Where(relation => relation.RelationType == BusinessEntityRelationTypeEnum.RelatesTo.ToString() &&
                               relation.ObjectAId == spaceId)
            .Select(relation => relation.ObjectBId);
        foreach (var relatedObjectId in OrderEntityIds(relatedObjectIds, entities))
        {
            await TryAddDocumentAsync(relatedObjectId);
        }

        return records;

        async Task VisitChildrenAsync(Guid parentId)
        {
            if (!childrenByParentId.TryGetValue(parentId, out var childIds))
            {
                return;
            }

            foreach (var childId in childIds)
            {
                if (!visitedTreeIds.Add(childId))
                {
                    continue;
                }

                await TryAddDocumentAsync(childId);
                await VisitChildrenAsync(childId);
            }
        }

        async Task TryAddDocumentAsync(Guid entityId)
        {
            if (!addedDocumentIds.Add(entityId) ||
                !entities.TryGetValue(entityId, out var entity) ||
                !ContentAccessPolicy.IsCommonFlagContentType(entity.EntityType) ||
                !await IsReadableObjectAsync(entity, permissions, cancellationToken))
            {
                return;
            }

            records.Add(new UserAccessibleDocumentRecord
            {
                Id = entity.Id,
                SpaceId = spaceId,
                Name = entity.Name,
                EntityType = entity.EntityType
            });
        }
    }

    // Возвращает пространства, где у пользователя есть права и доступный контент.
    public async Task<IReadOnlyList<UserSpaceRecord>> GetSpacesWithAccessibleObjectsAsync(
        Guid userId,
        bool isAnonymous,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<UserSpaceRecord>();
        }

        var spaces = await _businessEntityRepository.GetAllAsync(
            entity => entity.EntityType == BusinessEntityTypeEnum.Space,
            ct: cancellationToken);
        var records = new List<UserSpaceRecord>();

        foreach (var space in spaces
                     .OrderBy(space => space.CreatedDate)
                     .ThenBy(space => space.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!await HasAccessibleObjectsInSpaceAsync(userId, space.Id, isAnonymous, cancellationToken))
            {
                continue;
            }

            records.Add(new UserSpaceRecord
            {
                Id = space.Id,
                Name = space.Name,
                IsCurrent = _userContextService.CurrentSpaceId.HasValue &&
                            _userContextService.CurrentSpaceId.Value == space.Id
            });
        }

        return records;
    }

    // Находит пространство, в котором находится указанная сущность дерева.
    public async Task<Guid?> ResolveContainingSpaceIdAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        if (entityId == Guid.Empty)
        {
            return null;
        }

        var entities = (await _businessEntityRepository.GetAllAsync(null, ct: cancellationToken))
            .ToDictionary(entity => entity.Id);
        if (!entities.TryGetValue(entityId, out var entity))
        {
            return null;
        }

        if (entity.EntityType == BusinessEntityTypeEnum.Space)
        {
            return entity.Id;
        }

        var parentByChildId = (await _businessEntityRelationRepository.GetAllAsync(
                relation => relation.RelationType == BusinessEntityRelationTypeEnum.Contains.ToString(),
                ct: cancellationToken))
            .GroupBy(relation => relation.ObjectBId)
            .ToDictionary(group => group.Key, group => group.First().ObjectAId);

        var visited = new HashSet<Guid> { entityId };
        var currentId = entityId;
        while (parentByChildId.TryGetValue(currentId, out var parentId) && visited.Add(parentId))
        {
            if (!entities.TryGetValue(parentId, out var parent))
            {
                return null;
            }

            if (parent.EntityType == BusinessEntityTypeEnum.Space)
            {
                return parent.Id;
            }

            currentId = parentId;
        }

        return null;
    }

    // Проверяет, применимо ли назначение роли к пользователю напрямую или через группу.
    private static bool IsAssignmentApplicableToUser(
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

    // Упорядочивает ids так же стабильно, как дерево: по дате создания, затем по имени.
    private static IReadOnlyList<Guid> OrderEntityIds(
        IEnumerable<Guid> ids,
        IReadOnlyDictionary<Guid, BusinessEntityDto> entities)
    {
        return ids
            .OrderBy(id => entities.TryGetValue(id, out var entity) ? entity.CreatedDate : DateTime.MaxValue)
            .ThenBy(id => entities.TryGetValue(id, out var entity) ? entity.Name : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(id => id)
            .ToList();
    }

    // Добавляет права роли к итоговому набору через OR.
    private static void ApplyRole(UserEffectivePermissions result, UserRoleDto role)
    {
        var permissions = ParsePermissionCodes(role.Permissions);
        result.CanViewPublished |= permissions.Contains(UserRolePermissionCodes.ViewPublished);
        result.CanViewDraft |= permissions.Contains(UserRolePermissionCodes.ViewDraft);
        result.CanEditDraft |= permissions.Contains(UserRolePermissionCodes.EditDraft);
        result.CanPublishDraft |= permissions.Contains(UserRolePermissionCodes.PublishDraft);
        result.CanAdminItems |= permissions.Contains(UserRolePermissionCodes.AdminItems);
        result.CanAdminSpace |= permissions.Contains(UserRolePermissionCodes.AdminSpace);
        result.CanGlobalAdmin |= permissions.Contains(UserRolePermissionCodes.GlobalAdmin);
    }

    // Срезает anonymous-права до чтения published-контента.
    private static void NormalizeAnonymousPermissions(UserEffectivePermissions result)
    {
        result.CanViewDraft = false;
        result.CanEditDraft = false;
        result.CanPublishDraft = false;
        result.CanAdminItems = false;
        result.CanAdminSpace = false;
        result.CanGlobalAdmin = false;
    }

    // Проверяет наличие хотя бы одного readable объекта внутри пространства.
    private async Task<bool> SpaceContainsReadableObjectAsync(
        Guid spaceId,
        UserEffectivePermissions permissions,
        CancellationToken cancellationToken)
    {
        var entities = (await _businessEntityRepository.GetAllAsync(null, ct: cancellationToken))
            .ToDictionary(entity => entity.Id);
        if (!entities.TryGetValue(spaceId, out var space) || space.EntityType != BusinessEntityTypeEnum.Space)
        {
            return false;
        }

        var relations = await _businessEntityRelationRepository.GetAllAsync(
            relation =>
                relation.RelationType == BusinessEntityRelationTypeEnum.Contains.ToString() ||
                relation.RelationType == BusinessEntityRelationTypeEnum.RelatesTo.ToString(),
            ct: cancellationToken);
        var childrenByParentId = relations
            .Where(relation => relation.RelationType == BusinessEntityRelationTypeEnum.Contains.ToString())
            .GroupBy(relation => relation.ObjectAId)
            .ToDictionary(group => group.Key, group => group.Select(relation => relation.ObjectBId).ToList());
        var relatedObjectIds = relations
            .Where(relation => relation.RelationType == BusinessEntityRelationTypeEnum.RelatesTo.ToString() &&
                               relation.ObjectAId == spaceId)
            .Select(relation => relation.ObjectBId);

        foreach (var relatedObjectId in relatedObjectIds)
        {
            if (entities.TryGetValue(relatedObjectId, out var relatedObject) &&
                await IsReadableObjectAsync(relatedObject, permissions, cancellationToken))
            {
                return true;
            }
        }

        var stack = new Stack<Guid>();
        if (childrenByParentId.TryGetValue(spaceId, out var childIds))
        {
            foreach (var childId in childIds)
            {
                stack.Push(childId);
            }
        }

        var visited = new HashSet<Guid>();
        while (stack.Count > 0)
        {
            var entityId = stack.Pop();
            if (!visited.Add(entityId))
            {
                continue;
            }

            if (entities.TryGetValue(entityId, out var entity) &&
                await IsReadableObjectAsync(entity, permissions, cancellationToken))
            {
                return true;
            }

            if (childrenByParentId.TryGetValue(entityId, out var descendants))
            {
                foreach (var descendantId in descendants)
                {
                    stack.Push(descendantId);
                }
            }
        }

        return false;
    }

    // Проверяет, может ли объект быть показан пользователю в рамках прав пространства.
    private async Task<bool> IsReadableObjectAsync(
        BusinessEntityDto entity,
        UserEffectivePermissions permissions,
        CancellationToken cancellationToken)
    {
        if (!ContentAccessPolicy.IsCommonFlagContentType(entity.EntityType))
        {
            return false;
        }

        var publishedVersion = await GetPublishedVersionAsync(entity, cancellationToken);
        return ContentAccessPolicy.CanReadContent(
            entity.EntityType,
            entity.IsPublic,
            entity.CreatedByUserId,
            permissions.UserId,
            isAccessAdmin: false,
            permissions,
            publishedVersion);
    }

    // Возвращает publishedVersion из последнего payload документа.
    private async Task<int> GetPublishedVersionAsync(BusinessEntityDto entity, CancellationToken cancellationToken)
    {
        if (ContentAccessPolicy.IsAlwaysPublishedWhenCommon(entity.EntityType))
        {
            return 0;
        }

        var dataItems = await _businessEntityDataRepository.GetAllAsync(
            data => data.BusinessEntityId == entity.Id,
            ct: cancellationToken);
        var latestData = dataItems
            .OrderByDescending(data => data.Version <= 0 ? 1 : data.Version)
            .ThenByDescending(data => data.LastModifiedDate)
            .FirstOrDefault();
        return TryReadPublishedVersion(latestData?.Data);
    }

    // Извлекает указатель публикации из обязательного storage-envelope до переноса в отдельную таблицу на этапе 2.
    private static int TryReadPublishedVersion(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("payload", out var payload))
                throw new InvalidDataException("Отсутствует payload manifest статьи.");

            return payload.TryGetProperty("publishedVersion", out var versionElement) &&
                   versionElement.TryGetInt32(out var version)
                ? version
                : 0;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Повреждён manifest публикации статьи.", exception);
        }
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
}

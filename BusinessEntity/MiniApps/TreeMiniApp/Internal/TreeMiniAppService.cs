using BusinessEntity.Services;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal
{
    // Инкапсулирует серверные операции дерева поверх BusinessEntityHelper.
    internal sealed class TreeMiniAppService
    {
        private readonly BusinessEntityHelper _businessEntityHelper;
        private readonly IUserConnector _userConnector;
        private readonly TreeVisibilityService _treeVisibilityService;

        public TreeMiniAppService(
            BusinessEntityHelper businessEntityHelper,
            IUserConnector userConnector,
            TreeVisibilityService treeVisibilityService)
        {
            _businessEntityHelper = businessEntityHelper;
            _userConnector = userConnector;
            _treeVisibilityService = treeVisibilityService;
        }

        // Возвращает полный снимок дерева для пространства.
        public async Task<TreeSpaceSnapshot?> GetTreeForSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default)
        {
            var projection = await _businessEntityHelper.GetSubgraphAsync(spaceId, cancellationToken);
            var space = projection.Entities.SingleOrDefault(entity => entity.Id == spaceId);
            if (space == null || space.EntityType != global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id)
            {
                return null;
            }

            var currentUser = await _userConnector.EnsureCurrentUserAsync(cancellationToken);
            var currentBusinessUser = await _userConnector.GetCurrentUserAsync(cancellationToken);
            var permissions = await _userConnector.GetCurrentUserPermissionsForSpaceAsync(space.Id, cancellationToken);
            var isAccessAdmin = IsAccessAdmin(currentBusinessUser);
            var children = _treeVisibilityService.BuildVisibleChildren(
                space.Id, projection,
                currentUser?.Id,
                isAccessAdmin,
                permissions,
                cancellationToken);
            if (children.Count == 0 && !ContentAccessPolicy.CanViewSpaceContainer(permissions, isAccessAdmin))
            {
                return null;
            }

            return new TreeSpaceSnapshot(space, children);
        }

        // Создает дочернюю сущность под выбранным родителем.
        public async Task<BusinessEntity.Core.Classes.BusinessEntity> CreateEntityAsync(Guid parentId, BusinessEntityTypeEnum entityType, CancellationToken cancellationToken = default)
        {
            await EnsureAuthenticatedMutationAsync(cancellationToken);
            await EnsureCanAdminItemsAsync(parentId, cancellationToken);
            var parent = await _businessEntityHelper.GetBusinessEntityById(parentId)
                ?? throw new InvalidOperationException($"Parent entity '{parentId}' was not found.");

            return await _businessEntityHelper.CreateChildAsync(parent, entityType, ct: cancellationToken);
        }

        // Переименовывает сущность дерева.
        public async Task<BusinessEntity.Core.Classes.BusinessEntity?> RenameEntityAsync(Guid entityId, string newName, CancellationToken cancellationToken = default)
        {
            await EnsureAuthenticatedMutationAsync(cancellationToken);
            await EnsureCanAdminItemsAsync(entityId, cancellationToken);
            return await _businessEntityHelper.RenameEntity(entityId, newName, cancellationToken);
        }

        // Удаляет набор сущностей дерева.
        public async Task DeleteEntitiesAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default)
        {
            await EnsureAuthenticatedMutationAsync(cancellationToken);
            var distinctEntityIds = entityIds.Distinct().ToList();
            foreach (var entityId in distinctEntityIds)
            {
                await EnsureCanAdminItemsAsync(entityId, cancellationToken);
            }

            await _businessEntityHelper.RemoveEntitiesAsync(distinctEntityIds, "TreeMiniApp", cancellationToken);
        }

        // Перемещает набор сущностей к новому родителю.
        public async Task MoveEntitiesAsync(IReadOnlyList<Guid> entityIds, Guid targetParentId, CancellationToken cancellationToken = default)
        {
            await EnsureAuthenticatedMutationAsync(cancellationToken);
            await EnsureCanAdminItemsAsync(targetParentId, cancellationToken);
            var targetParent = await _businessEntityHelper.GetBusinessEntityById(targetParentId)
                ?? throw new InvalidOperationException($"Target parent '{targetParentId}' was not found.");

            var distinctEntityIds = entityIds.Distinct().ToList();
            foreach (var entityId in distinctEntityIds)
            {
                await EnsureCanAdminItemsAsync(entityId, cancellationToken);
            }

            await _businessEntityHelper.MoveEntitiesAsync(distinctEntityIds, targetParentId, "TreeMiniApp", cancellationToken);
        }

        // Запрещает anonymous-режиму выполнять изменения дерева.
        private async Task EnsureAuthenticatedMutationAsync(CancellationToken cancellationToken)
        {
            var currentUser = await _userConnector.GetCurrentUserAsync(cancellationToken);
            if (currentUser?.IsAuthenticated == true)
            {
                return;
            }

            throw new UnauthorizedAccessException("Для изменения дерева требуется вход.");
        }

        // Проверяет право администрирования элементов дерева в пространстве сущности.
        private async Task EnsureCanAdminItemsAsync(Guid entityId, CancellationToken cancellationToken)
        {
            var currentUser = await _userConnector.GetCurrentUserAsync(cancellationToken);
            var permissions = await _userConnector.GetCurrentUserPermissionsForEntityAsync(entityId, cancellationToken);
            if (IsAccessAdmin(currentUser) ||
                permissions.CanAdminItems ||
                permissions.CanAdminSpace ||
                permissions.CanGlobalAdmin)
            {
                return;
            }

            throw new UnauthorizedAccessException("Нет прав на администрирование элементов.");
        }

        private static bool IsAccessAdmin(BusinessEntityUser? user)
        {
            return user?.IsAkadmin == true ||
                   user?.IsGeneralAdmin == true ||
                   string.Equals(user?.UserName, "admin", StringComparison.OrdinalIgnoreCase);
        }
    }
}

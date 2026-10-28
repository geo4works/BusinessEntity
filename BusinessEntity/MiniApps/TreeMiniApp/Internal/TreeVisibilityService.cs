using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.Services;
using System.Globalization;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal
{
    // Формирует видимую для пользователя часть дерева пространства.
    internal sealed class TreeVisibilityService
    {
        private const bool ShowFoldersWithoutVisibleContent = false;
        private static readonly StringComparer TreeNameComparer = StringComparer.Create(new CultureInfo("ru-RU"), ignoreCase: true);

        private readonly BusinessEntityHelper _businessEntityHelper;

        public TreeVisibilityService(
            BusinessEntityHelper businessEntityHelper)
        {
            _businessEntityHelper = businessEntityHelper;
        }

        public async Task<IReadOnlyList<TreeNodeSnapshot>> BuildVisibleChildrenAsync(
            Guid parentId,
            Guid? currentUserId,
            bool isAccessAdmin,
            UserEffectivePermissions permissions,
            CancellationToken cancellationToken)
        {
            var children = await _businessEntityHelper.GetContainedEntitiesAsync(parentId, cancellationToken);
            var snapshots = new List<TreeNodeSnapshot>();

            foreach (var child in children)
            {
                var descendants = await BuildVisibleChildrenAsync(
                    child.Id,
                    currentUserId,
                    isAccessAdmin,
                    permissions,
                    cancellationToken);

                if (IsFolderEntity(child.EntityType))
                {
                    if (descendants.Count > 0 ||
                        (ShowFoldersWithoutVisibleContent && ContentAccessPolicy.CanViewSpaceContainer(permissions, isAccessAdmin)))
                    {
                        snapshots.Add(new TreeNodeSnapshot(child, descendants));
                    }

                    continue;
                }

                if (CanDisplayTreeEntity(child, currentUserId, isAccessAdmin, permissions))
                {
                    snapshots.Add(new TreeNodeSnapshot(child, descendants));
                }
            }

            return SortTreeSnapshots(snapshots);
        }

        private static bool CanDisplayTreeEntity(
            BusinessEntity.Core.Classes.BusinessEntity entity,
            Guid? currentUserId,
            bool isAccessAdmin,
            UserEffectivePermissions permissions)
        {
            if (!ContentAccessPolicy.IsCommonFlagContentType(entity.EntityType))
            {
                return ContentAccessPolicy.CanViewSpaceContainer(permissions, isAccessAdmin);
            }

            return ContentAccessPolicy.CanReadContent(
                entity.EntityType,
                entity.IsPublic,
                entity.CreatedByUserId,
                currentUserId,
                isAccessAdmin,
                permissions,
                entity.PublishedVersion);
        }

        private static bool IsFolderEntity(BusinessEntityTypeEnum entityType)
        {
            return entityType == BusinessEntityTypeEnum.Folder;
        }

        private static IReadOnlyList<TreeNodeSnapshot> SortTreeSnapshots(IEnumerable<TreeNodeSnapshot> snapshots)
        {
            return snapshots
                .OrderBy(snapshot => IsFolderEntity(snapshot.Entity.EntityType) ? 0 : 1)
                .ThenBy(snapshot => snapshot.Entity.Name ?? string.Empty, TreeNameComparer)
                .ThenBy(snapshot => snapshot.Entity.Id)
                .ToList();
        }
    }
}

using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.Services;
using BusinessEntity.WebLogger.Services;
using System.Globalization;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal
{
    // Формирует видимую для пользователя часть дерева пространства.
    internal sealed class TreeVisibilityService
    {
        private const bool ShowFoldersWithoutVisibleContent = false;
        private static readonly StringComparer TreeNameComparer = StringComparer.Create(new CultureInfo("ru-RU"), ignoreCase: true);

        private readonly BusinessEntityHelper _businessEntityHelper;
        private readonly IWebLoggerService? _webLogger;

        public TreeVisibilityService(
            BusinessEntityHelper businessEntityHelper,
            IWebLoggerService? webLogger)
        {
            _businessEntityHelper = businessEntityHelper;
            _webLogger = webLogger;
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

                if (await CanDisplayTreeEntityAsync(child, currentUserId, isAccessAdmin, permissions, cancellationToken))
                {
                    snapshots.Add(new TreeNodeSnapshot(child, descendants));
                }
            }

            return SortTreeSnapshots(snapshots);
        }

        private async Task<bool> CanDisplayTreeEntityAsync(
            BusinessEntity.Core.Classes.BusinessEntity entity,
            Guid? currentUserId,
            bool isAccessAdmin,
            UserEffectivePermissions permissions,
            CancellationToken cancellationToken)
        {
            if (!ContentAccessPolicy.IsCommonFlagContentType(entity.EntityType))
            {
                return ContentAccessPolicy.CanViewSpaceContainer(permissions, isAccessAdmin);
            }

            var publishedVersion = await GetPublishedVersionAsync(entity, cancellationToken);
            return ContentAccessPolicy.CanReadContent(
                entity.EntityType,
                entity.IsPublic,
                entity.CreatedByUserId,
                currentUserId,
                isAccessAdmin,
                permissions,
                publishedVersion);
        }

        private async Task<int> GetPublishedVersionAsync(
            BusinessEntity.Core.Classes.BusinessEntity entity,
            CancellationToken cancellationToken)
        {
            try
            {
                if (ContentAccessPolicy.IsAlwaysPublishedWhenCommon(entity.EntityType))
                {
                    return 0;
                }

                return entity.EntityType switch
                {
                    BusinessEntityTypeEnum.RichTextDocument => (await _businessEntityHelper.GetEntityWithDataAsync<RichTextDocument>(entity.Id, cancellationToken))?.Data.PublishedVersion ?? 0,
                    _ => 0
                };
            }
            catch (Exception ex)
            {
                if (_webLogger != null)
                {
                    await _webLogger.Error(ex);
                }

                return 0;
            }
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

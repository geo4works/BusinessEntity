using BusinessEntity.Services;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Navigation;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using System.Globalization;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal
{
    // Формирует видимую для пользователя часть дерева пространства.
    internal sealed class TreeVisibilityService
    {
        private const bool ShowFoldersWithoutVisibleContent = false;
        private static readonly StringComparer TreeNameComparer = StringComparer.Create(new CultureInfo("ru-RU"), ignoreCase: true);

        // Сервис только фильтрует уже готовый structural snapshot текущими правами.
        public TreeVisibilityService() { }

        // Дерево строится из одной ревизии графа без повторных bus/SQL обращений для потомков.
        public IReadOnlyList<TreeNodeSnapshot> BuildVisibleChildren(
            Guid parentId,
            GraphProjection projection,
            Guid? currentUserId,
            bool isAccessAdmin,
            UserEffectivePermissions permissions,
            CancellationToken cancellationToken)
        {
            var entities = projection.Entities.ToDictionary(entity => entity.Id);
            var children = projection.Relations.Where(relation => relation.RelationType == "Contains")
                .GroupBy(relation => relation.ObjectAId).ToDictionary(group => group.Key, group => group.Select(relation => relation.ObjectBId).ToArray());
            return Build(parentId);

            // Права применяются поверх одной структурной ревизии без storage-вызова для каждого узла.
            IReadOnlyList<TreeNodeSnapshot> Build(Guid parent)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = new List<TreeNodeSnapshot>();
                foreach (var id in children.GetValueOrDefault(parent) ?? [])
                {
                    var child = entities[id]; var descendants = Build(id);
                    if (IsFolderEntity(child.EntityType))
                    {
                        if (descendants.Count > 0 || (ShowFoldersWithoutVisibleContent && ContentAccessPolicy.CanViewSpaceContainer(permissions, isAccessAdmin)))
                            result.Add(new(child, descendants));
                    }
                    else if (CanDisplayTreeEntity(child, currentUserId, isAccessAdmin, permissions))
                    {
                        if (ContentAccessPolicy.RequiresPublishedVersion(child.EntityType) &&
                            !ContentAccessPolicy.CanViewDraft(child.EntityType, child.IsPublic, child.CreatedByUserId, currentUserId, isAccessAdmin, permissions))
                            child.Name = projection.PublishedNames.TryGetValue(child.Id, out var title) ? title
                                : throw new InvalidDataException("Отсутствует название опубликованной версии.");
                        result.Add(new(child, descendants));
                    }
                }
                return SortTreeSnapshots(result);
            }
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
            return entityType == global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id;
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

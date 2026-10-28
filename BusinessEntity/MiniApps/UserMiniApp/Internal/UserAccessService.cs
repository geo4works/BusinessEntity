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

// UserAccessService владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserAccessService
{
        private readonly IDataProviderConnector _data;
        private readonly UserSpaceContentAccessHelper _spaceContentAccessHelper;
        private readonly UserIdentityService _identityService;
        // Получает зависимости выделенного сценария.
        public UserAccessService(IDataProviderConnector data, UserSpaceContentAccessHelper spaceContentAccessHelper, UserIdentityService identityService)
        { _data = data; _spaceContentAccessHelper = spaceContentAccessHelper; _identityService = identityService; }
        // Выполняет сценарий GetCurrentUserPermissionsForSpaceAsync внутри владельца User.
        public async Task<UserEffectivePermissions> GetCurrentUserPermissionsForSpaceAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default)
        {
            if (spaceId == Guid.Empty)
            {
                return UserEffectivePermissions.Deny(Guid.Empty, spaceId, isAnonymous: false);
            }

            var currentUser = await _identityService.GetCurrentUserAsync(cancellationToken);
            if (currentUser?.IsAuthenticated == true)
            {
                var localUser = await _identityService.EnsureCurrentUserAsync(cancellationToken);
                if (localUser != null)
                {
                    return await _spaceContentAccessHelper.GetEffectivePermissionsForSpaceAsync(
                        localUser.Id,
                        spaceId,
                        isAnonymous: false,
                        cancellationToken);
                }
            }

            return await GetAnonymousPermissionsForSpaceAsync(spaceId, cancellationToken);
        }

        // Выполняет сценарий GetCurrentUserPermissionsForEntityAsync внутри владельца User.
        public async Task<UserEffectivePermissions> GetCurrentUserPermissionsForEntityAsync(
            Guid entityId,
            CancellationToken cancellationToken = default)
        {
            var spaceId = await _spaceContentAccessHelper.ResolveContainingSpaceIdAsync(entityId, cancellationToken);
            if (!spaceId.HasValue)
            {
                return UserEffectivePermissions.Deny(Guid.Empty, Guid.Empty, isAnonymous: false);
            }

            return await GetCurrentUserPermissionsForSpaceAsync(spaceId.Value, cancellationToken);
        }

        // Выполняет сценарий GetContentAccessForEntityIdAsync внутри владельца User.
        public async Task<UserContentAccessDecision> GetContentAccessForEntityIdAsync(Guid entityId, CancellationToken ct = default)
        {
            var entity = await _data.GetByIdAsync(entityId, ct) ?? throw new KeyNotFoundException("Объект не найден.");
            return await GetCurrentUserContentAccessForEntityAsync(new UserContentAccessRequest
            { EntityId = entity.Id, EntityType = entity.EntityType, IsCommon = entity.IsPublic,
                CreatedByUserId = entity.CreatedByUserId, PublishedVersion = entity.PublishedVersion }, ct);
        }

        // Выполняет сценарий GetCurrentUserContentAccessForEntityAsync внутри владельца User.
        public async Task<UserContentAccessDecision> GetCurrentUserContentAccessForEntityAsync(
            UserContentAccessRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var currentBusinessUser = await _identityService.GetCurrentUserAsync(cancellationToken);
            var localUser = currentBusinessUser?.IsAuthenticated == true
                ? await _identityService.EnsureCurrentUserAsync(cancellationToken)
                : null;
            var permissions = await GetCurrentUserPermissionsForEntityAsync(request.EntityId, cancellationToken);
            var currentUserId = permissions.IsAnonymous ? null : localUser?.Id;
            var isAccessAdmin = IsAccessAdmin(currentBusinessUser);
            var canViewDraft = ContentAccessPolicy.CanViewDraft(
                request.EntityType,
                request.IsCommon,
                request.CreatedByUserId,
                currentUserId,
                isAccessAdmin,
                permissions);
            var canViewPublished = ContentAccessPolicy.CanViewPublished(
                request.EntityType,
                request.IsCommon,
                request.CreatedByUserId,
                currentUserId,
                isAccessAdmin,
                permissions,
                request.PublishedVersion);

            return new UserContentAccessDecision
            {
                IsOwner = ContentAccessPolicy.IsOwner(request.CreatedByUserId, currentUserId),
                IsAccessAdmin = isAccessAdmin,
                CanViewDraft = canViewDraft,
                CanViewPublished = canViewPublished,
                CanRead = canViewDraft || canViewPublished,
                CanEditDraft = ContentAccessPolicy.CanEditDraft(
                    request.EntityType,
                    request.IsCommon,
                    request.CreatedByUserId,
                    currentUserId,
                    isAccessAdmin,
                    permissions),
                CanPublishDraft = ContentAccessPolicy.CanPublishDraft(
                    request.EntityType,
                    request.IsCommon,
                    request.CreatedByUserId,
                    currentUserId,
                    isAccessAdmin,
                    permissions),
                CanChangeCommonFlag = ContentAccessPolicy.CanChangeCommonFlag(
                    request.CreatedByUserId,
                    currentUserId,
                    isAccessAdmin,
                    permissions),
                CanViewSpaceContainer = ContentAccessPolicy.CanViewSpaceContainer(permissions, isAccessAdmin)
            };
        }

        // Выполняет сценарий GetAnonymousPermissionsForSpaceAsync внутри владельца User.
        public async Task<UserEffectivePermissions> GetAnonymousPermissionsForSpaceAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default)
        {
            var anonymousUser = await _identityService.EnsureAnonymousUserAsync(cancellationToken);
            return await _spaceContentAccessHelper.GetEffectivePermissionsForSpaceAsync(
                anonymousUser.Id,
                spaceId,
                isAnonymous: true,
                cancellationToken);
        }

        // Выполняет сценарий GetAnonymousAccessibleSpacesAsync внутри владельца User.
        public async Task<IReadOnlyList<UserSpaceRecord>> GetAnonymousAccessibleSpacesAsync(CancellationToken cancellationToken = default)
        {
            var anonymousUser = await _identityService.EnsureAnonymousUserAsync(cancellationToken);
            return await _spaceContentAccessHelper.GetSpacesWithAccessibleObjectsAsync(
                anonymousUser.Id,
                isAnonymous: true,
                cancellationToken);
        }

        // Выполняет сценарий GetAnonymousAccessibleDocumentsAsync внутри владельца User.
        public async Task<IReadOnlyList<UserAccessibleDocumentRecord>> GetAnonymousAccessibleDocumentsAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default)
        {
            var anonymousUser = await _identityService.EnsureAnonymousUserAsync(cancellationToken);
            return await _spaceContentAccessHelper.GetAccessibleDocumentsInSpaceAsync(
                anonymousUser.Id,
                spaceId,
                isAnonymous: true,
                cancellationToken);
        }
}

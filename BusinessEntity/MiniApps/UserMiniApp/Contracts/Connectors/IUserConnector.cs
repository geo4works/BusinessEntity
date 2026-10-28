using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;

namespace BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors
{
    // Определяет короткий connector для адресного доступа к user mini-app из других модулей.
    public interface IUserConnector
    {
        // Рассчитывает права по свежей серверной карточке, без metadata из UI.
        Task<UserContentAccessDecision> GetEntityContentAccessAsync(Guid entityId, CancellationToken cancellationToken = default);
        // Возвращает текущего пользователя приложения через публичный контракт mini-app.
        Task<BusinessEntityUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default);
        // Гарантирует локальную учетную запись текущего пользователя и возвращает ее DTO.
        Task<UserDto?> EnsureCurrentUserAsync(CancellationToken cancellationToken = default);
        // Удаляет локальную учетную запись текущего пользователя вместе с ее техническими свойствами.
        Task<bool> DeleteCurrentUserAsync(CancellationToken cancellationToken = default);
        // Возвращает локальных пользователей для страницы администрирования.
        Task<IReadOnlyList<UserAdministrationRecord>> GetAdministrationUsersAsync(CancellationToken cancellationToken = default);
        // Читает пользователей приложения из Authentik и сохраняет их в локальную таблицу Users.
        Task<IReadOnlyList<UserAdministrationRecord>> ReadAdministrationUsersFromAuthentikAsync(
            CancellationToken cancellationToken = default);
        // Создает пользователя приложения в Authentik через user mini-app.
        Task<UserAdministrationRecord> CreateAdministrationUserAsync(CancellationToken cancellationToken = default);
        // Обновляет локального пользователя через user mini-app.
        Task<UserAdministrationRecord> UpdateAdministrationUserAsync(
            Guid userId,
            UserAdministrationSaveRequest request,
            CancellationToken cancellationToken = default);
        // Удаляет локального пользователя через user mini-app.
        Task<bool> DeleteAdministrationUserAsync(Guid userId, CancellationToken cancellationToken = default);
        // Возвращает роли для страницы администрирования.
        Task<IReadOnlyList<UserRoleRecord>> GetRolesAsync(CancellationToken cancellationToken = default);
        // Создает роль через user mini-app.
        Task<UserRoleRecord> CreateRoleAsync(CancellationToken cancellationToken = default);
        // Обновляет роль через user mini-app.
        Task<UserRoleRecord> UpdateRoleAsync(
            Guid roleId,
            UserRoleSaveRequest request,
            CancellationToken cancellationToken = default);
        // Удаляет роль через user mini-app.
        Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
        // Возвращает группы пользователей для страницы администрирования.
        Task<IReadOnlyList<UserGroupRecord>> GetUserGroupsAsync(CancellationToken cancellationToken = default);
        // Создает группу пользователей через user mini-app.
        Task<UserGroupRecord> CreateUserGroupAsync(CancellationToken cancellationToken = default);
        // Обновляет группу пользователей через user mini-app.
        Task<UserGroupRecord> UpdateUserGroupAsync(
            Guid groupId,
            UserGroupSaveRequest request,
            CancellationToken cancellationToken = default);
        // Удаляет группу пользователей через user mini-app.
        Task<bool> DeleteUserGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
        // Возвращает назначение пользователей в группу.
        Task<IReadOnlyList<UserGroupMembershipRecord>> GetUserGroupMembershipsAsync(
            Guid groupId,
            CancellationToken cancellationToken = default);
        // Сохраняет полный список пользователей группы.
        Task<IReadOnlyList<UserGroupMembershipRecord>> UpdateUserGroupMembershipsAsync(
            Guid groupId,
            UserGroupMembershipSaveRequest request,
            CancellationToken cancellationToken = default);
        // Возвращает пространства для вкладки назначения ролей.
        Task<IReadOnlyList<UserSpaceRecord>> GetRoleAssignmentSpacesAsync(CancellationToken cancellationToken = default);
        // Возвращает назначения ролей для выбранного пространства.
        Task<IReadOnlyList<UserRoleAssignmentRecord>> GetRoleAssignmentsAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default);
        // Создает назначение роли для группы или пользователя.
        Task<UserRoleAssignmentRecord> CreateRoleAssignmentAsync(
            Guid spaceId,
            UserRoleAssignmentSaveRequest request,
            CancellationToken cancellationToken = default);
        // Удаляет назначение роли.
        Task<bool> DeleteRoleAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);
        // Возвращает профиль текущего пользователя.
        Task<UserProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default);
        // Обновляет профиль текущего пользователя.
        Task<UserProfileDto> UpdateProfileAsync(
            UserProfileUpdateRequest request,
            CancellationToken cancellationToken = default);
        // Возвращает все группы текущего пользователя.
        Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken cancellationToken = default);
        // Проверяет membership текущего пользователя в конкретной группе.
        Task<bool> IsInGroupAsync(string groupName, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<RichTextDocumentBookmark>> GetRichDocBookmarksAsync(
            Guid documentId,
            CancellationToken cancellationToken = default);

        Task<RichTextDocumentBookmark?> AddRichDocBookmarkAsync(
            Guid documentId,
            RichTextDocumentTextSelection? selection,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteRichDocBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken = default);

        Task<int> GetRichDocDisplayedLevelAsync(
            Guid documentId,
            CancellationToken cancellationToken = default);

        Task SaveRichDocDisplayedLevelAsync(
            Guid documentId,
            int displayLevelCount,
            CancellationToken cancellationToken = default);

        // Возвращает технические свойства текущего пользователя по типу.
        Task<IReadOnlyList<UserPropertyDto>> GetCurrentUserPropertiesAsync(
            int propertyType,
            CancellationToken cancellationToken = default);

        // Создает или обновляет техническое свойство текущего пользователя.
        Task<UserPropertyDto?> UpsertCurrentUserPropertyAsync(
            Guid? propertyId,
            int propertyType,
            string data,
            string metadata,
            CancellationToken cancellationToken = default);

        // Удаляет техническое свойство текущего пользователя.
        Task<bool> DeleteCurrentUserPropertyAsync(
            Guid propertyId,
            CancellationToken cancellationToken = default);

        // Возвращает пользовательскую коллекцию пресетов печати документов.
        Task<DocPrintSettingsPresetCollection> GetDocPrintPresetsAsync(CancellationToken cancellationToken = default);

        // Сохраняет или перезаписывает пользовательский пресет печати документов.
        Task<DocPrintSettingsPreset> SaveDocPrintPresetAsync(
            DocPrintSettingsPreset preset,
            CancellationToken cancellationToken = default);

        // Удаляет пользовательский пресет печати документов по имени.
        Task<bool> DeleteDocPrintPresetAsync(
            string presetName,
            CancellationToken cancellationToken = default);

        // Возвращает effective permissions текущего или anonymous пользователя в пространстве.
        Task<UserEffectivePermissions> GetCurrentUserPermissionsForSpaceAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default);

        // Возвращает effective permissions текущего или anonymous пользователя для пространства сущности.
        Task<UserEffectivePermissions> GetCurrentUserPermissionsForEntityAsync(
            Guid entityId,
            CancellationToken cancellationToken = default);

        // Возвращает готовое решение по доступу текущего или anonymous пользователя к контентной сущности.
        Task<UserContentAccessDecision> GetCurrentUserContentAccessForEntityAsync(
            UserContentAccessRequest request,
            CancellationToken cancellationToken = default);

        // Возвращает effective permissions anonymous-пользователя в пространстве.
        Task<UserEffectivePermissions> GetAnonymousPermissionsForSpaceAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default);

        // Возвращает пространства, где anonymous имеет права и есть доступные объекты.
        Task<IReadOnlyList<UserSpaceRecord>> GetAnonymousAccessibleSpacesAsync(CancellationToken cancellationToken = default);

        // Возвращает документы в пространстве, которые anonymous может открыть для просмотра.
        Task<IReadOnlyList<UserAccessibleDocumentRecord>> GetAnonymousAccessibleDocumentsAsync(
            Guid spaceId,
            CancellationToken cancellationToken = default);
    }
}

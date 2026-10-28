using BusinessEntity.Services;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;

namespace BusinessEntity.MiniApps.UserMiniApp.Contracts;

// Централизует правила доступа к контентным BusinessEntity.
public static class ContentAccessPolicy
{
    // Проверяет, относится ли тип к контенту с признаком "Общее".
    public static bool IsCommonFlagContentType(BusinessEntityTypeEnum entityType)
    {
        return entityType == global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id ||
               entityType == global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id;
    }

    // Проверяет, нужен ли типу explicit published version для ViewPublished.
    public static bool RequiresPublishedVersion(BusinessEntityTypeEnum entityType)
    {
        return entityType == global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id;
    }

    // Проверяет, считается ли тип опубликованным без explicit published version.
    public static bool IsAlwaysPublishedWhenCommon(BusinessEntityTypeEnum entityType)
    {
        return entityType == global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id;
    }

    // Проверяет административный bypass для контента.
    public static bool HasAdministrativeContentAccess(UserEffectivePermissions permissions, bool isAccessAdmin)
    {
        return isAccessAdmin ||
               permissions.CanAdminSpace ||
               permissions.CanGlobalAdmin;
    }

    // Проверяет, является ли текущий пользователь создателем сущности.
    public static bool IsOwner(Guid? createdByUserId, Guid? currentUserId)
    {
        return currentUserId.HasValue &&
               createdByUserId.HasValue &&
               createdByUserId.Value == currentUserId.Value;
    }

    // Проверяет owner/admin bypass для всех операций над контентом.
    public static bool HasOwnerOrAdminContentAccess(
        Guid? createdByUserId,
        Guid? currentUserId,
        bool isAccessAdmin,
        UserEffectivePermissions permissions)
    {
        return IsOwner(createdByUserId, currentUserId) ||
               HasAdministrativeContentAccess(permissions, isAccessAdmin);
    }

    // Проверяет, можно ли пользователю видеть draft/current состояние контента.
    public static bool CanViewDraft(
        BusinessEntityTypeEnum entityType,
        bool isCommon,
        Guid? createdByUserId,
        Guid? currentUserId,
        bool isAccessAdmin,
        UserEffectivePermissions permissions)
    {
        if (!IsCommonFlagContentType(entityType))
        {
            return HasAdministrativeContentAccess(permissions, isAccessAdmin) ||
                   permissions.CanViewDraft;
        }

        if (HasOwnerOrAdminContentAccess(createdByUserId, currentUserId, isAccessAdmin, permissions))
        {
            return true;
        }

        return isCommon && permissions.CanViewDraft;
    }

    // Проверяет, можно ли пользователю видеть published-состояние контента.
    public static bool CanViewPublished(
        BusinessEntityTypeEnum entityType,
        bool isCommon,
        Guid? createdByUserId,
        Guid? currentUserId,
        bool isAccessAdmin,
        UserEffectivePermissions permissions,
        int publishedVersion)
    {
        if (!IsCommonFlagContentType(entityType))
        {
            return HasAdministrativeContentAccess(permissions, isAccessAdmin) ||
                   permissions.CanViewPublished;
        }

        if (HasOwnerOrAdminContentAccess(createdByUserId, currentUserId, isAccessAdmin, permissions))
        {
            return true;
        }

        if (!isCommon || !permissions.CanViewPublished)
        {
            return false;
        }

        if (IsAlwaysPublishedWhenCommon(entityType))
        {
            return true;
        }

        return RequiresPublishedVersion(entityType) && publishedVersion > 0;
    }

    // Проверяет, можно ли пользователю читать контент любым допустимым режимом.
    public static bool CanReadContent(
        BusinessEntityTypeEnum entityType,
        bool isCommon,
        Guid? createdByUserId,
        Guid? currentUserId,
        bool isAccessAdmin,
        UserEffectivePermissions permissions,
        int publishedVersion)
    {
        return CanViewDraft(entityType, isCommon, createdByUserId, currentUserId, isAccessAdmin, permissions) ||
               CanViewPublished(entityType, isCommon, createdByUserId, currentUserId, isAccessAdmin, permissions, publishedVersion);
    }

    // Проверяет, можно ли пользователю изменять draft/current состояние контента.
    public static bool CanEditDraft(
        BusinessEntityTypeEnum entityType,
        bool isCommon,
        Guid? createdByUserId,
        Guid? currentUserId,
        bool isAccessAdmin,
        UserEffectivePermissions permissions)
    {
        if (HasOwnerOrAdminContentAccess(createdByUserId, currentUserId, isAccessAdmin, permissions))
        {
            return true;
        }

        if (!IsCommonFlagContentType(entityType))
        {
            return permissions.CanEditDraft && permissions.CanViewDraft;
        }

        return isCommon && permissions.CanEditDraft && permissions.CanViewDraft;
    }

    // Проверяет, можно ли пользователю публиковать draft/current состояние контента.
    public static bool CanPublishDraft(
        BusinessEntityTypeEnum entityType,
        bool isCommon,
        Guid? createdByUserId,
        Guid? currentUserId,
        bool isAccessAdmin,
        UserEffectivePermissions permissions)
    {
        if (HasOwnerOrAdminContentAccess(createdByUserId, currentUserId, isAccessAdmin, permissions))
        {
            return true;
        }

        if (!IsCommonFlagContentType(entityType))
        {
            return permissions.CanPublishDraft && permissions.CanViewDraft;
        }

        return isCommon && permissions.CanPublishDraft && permissions.CanViewDraft;
    }

    // Проверяет, можно ли пользователю менять признак "Общее".
    public static bool CanChangeCommonFlag(
        Guid? createdByUserId,
        Guid? currentUserId,
        bool isAccessAdmin,
        UserEffectivePermissions permissions)
    {
        return HasOwnerOrAdminContentAccess(createdByUserId, currentUserId, isAccessAdmin, permissions);
    }

    // Проверяет, есть ли у пользователя право видеть контейнерный слой пространства.
    public static bool CanViewSpaceContainer(UserEffectivePermissions permissions, bool isAccessAdmin)
    {
        return HasAdministrativeContentAccess(permissions, isAccessAdmin) ||
               permissions.CanViewDraft ||
               permissions.CanViewPublished;
    }
}

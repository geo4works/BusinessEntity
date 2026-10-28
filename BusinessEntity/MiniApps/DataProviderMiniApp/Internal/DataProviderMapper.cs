using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal;

internal static class DataProviderMapper
{
    // Собирает runtime BusinessEntityData из DTO-записи хранилища.
    public static BusinessEntity.Core.Classes.BusinessEntity ToBusinessEntity(BusinessEntityDto dto)
    {
        return new BusinessEntity.Core.Classes.BusinessEntity
        {
            Id = dto.Id,
            CreatedDate = dto.CreatedDate,
            LastModifiedDate = dto.LastModifiedDate,
            CreatedByUserId = dto.CreatedByUserId,
            LastModifiedByUserId = dto.LastModifiedByUserId,
            IsPublic = dto.IsPublic,
            PublishedVersion = dto.PublishedVersion,
            Name = dto.Name,

            EntityType = dto.EntityType
        };
    }

    // Преобразует runtime BusinessEntityData в DTO для хранения.
    public static BusinessEntityDto ToDto(BusinessEntity.Core.Classes.BusinessEntity entityData)
    {
        return new BusinessEntityDto
        {
            Id = entityData.Id,
            CreatedDate = entityData.CreatedDate,
            LastModifiedDate = entityData.LastModifiedDate,
            CreatedByUserId = entityData.CreatedByUserId,
            LastModifiedByUserId = entityData.LastModifiedByUserId,
            IsPublic = entityData.IsPublic,
            PublishedVersion = entityData.PublishedVersion,
            Name = entityData.Name,

            EntityType = entityData.EntityType
        };
    }

    // Собирает runtime BusinessEntityRelation из DTO связи.
    public static BusinessEntityRelation ToBusinessEntityRelation(BusinessEntityRelationDto dto)
    {
        return new BusinessEntityRelation
        {
            Id = dto.Id,
            CreatedDate = dto.CreatedDate,
            LastModifiedDate = dto.LastModifiedDate,
            ObjectAId = dto.ObjectAId,
            ObjectBId = dto.ObjectBId,
            RelationType = dto.RelationType,
            RelationParams = dto.RelationParams
        };
    }

    // Преобразует runtime BusinessEntityRelation в DTO для хранения.
    public static BusinessEntityRelationDto ToDto(BusinessEntityRelation relation)
    {
        return new BusinessEntityRelationDto
        {
            Id = relation.Id,
            CreatedDate = relation.CreatedDate,
            LastModifiedDate = relation.LastModifiedDate,
            ObjectAId = relation.ObjectAId,
            ObjectBId = relation.ObjectBId,
            RelationType = relation.RelationType,
            RelationParams = relation.RelationParams
        };
    }

}

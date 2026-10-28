using System;
using BusinessEntity.Core.Classes;

// Контракт легковесной бизнес-сущности дерева
namespace BusinessEntity.Core.Contracts
{
    // Описывает поля базовой entity без тяжелого payload
    public interface IBusinessEntity
    {
        // Идентификатор сущности
        Guid Id { get; set; }
        // Дата создания сущности
        DateTime CreatedDate { get; set; }
        // Дата последнего изменения сущности
        DateTime LastModifiedDate { get; set; }
        // Локальный пользователь, создавший сущность
        Guid? CreatedByUserId { get; set; }
        // Локальный пользователь, последним изменивший сущность
        Guid? LastModifiedByUserId { get; set; }
        // Признак общей видимости документа для пользователей, не являющихся создателями.
        bool IsPublic { get; set; }
        // Отображаемое имя сущности
        string Name { get; set; }

        // Основной тип сущности
        BusinessEntityTypeEnum EntityType { get; set; }
    }
}

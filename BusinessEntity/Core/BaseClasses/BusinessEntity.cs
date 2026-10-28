using BusinessEntity.Core.Contracts;

// Базовая узловая сущность дерева
namespace BusinessEntity.Core.Classes;

// Хранит легковесные поля бизнес-сущности без тяжелого payload
public class BusinessEntity: IBusinessEntity
{
    // Идентификатор сущности
    public Guid Id { get; set; } = Guid.NewGuid();
    // Дата создания сущности
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    // Дата последнего изменения сущности
    public DateTime LastModifiedDate { get; set; } = DateTime.UtcNow;
    // Локальный пользователь, создавший сущность
    public Guid? CreatedByUserId { get; set; }
    // Локальный пользователь, последним изменивший сущность
    public Guid? LastModifiedByUserId { get; set; }
    // Указатель публикации хранится вне строк истории содержимого.
    public int PublishedVersion { get; set; }
    // Признак общей видимости документа.
    public bool IsPublic { get; set; }
    // Отображаемое имя сущности
    public string Name { get; set; } = string.Empty;

    // Основной тип сущности
    public BusinessEntityTypeEnum EntityType { get; set; } = BusinessEntityTypeEnum.Undefined;
}

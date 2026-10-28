namespace BusinessEntity.Core.Contracts;

// Неисторическая проекция указателя публикации; содержимое версий её не хранит.
public interface IBusinessEntityPublication
{
    int PublishedVersion { get; set; }
}

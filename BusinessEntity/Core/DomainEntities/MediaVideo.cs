using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;

namespace BusinessEntity.Core.DomainEntities;

// Typed payload видео из общего мультимедиа-хранилища.
public class MediaVideo : BusinessEntityData, IBusinessEntityData
{
    // Видео всегда имеет тип MediaVideo.
    public override BusinessEntityTypeEnum EntityType { get; set; } = BusinessEntityTypeEnum.MediaVideo;

    // Имя файла при загрузке.
    public string FileName { get; set; } = string.Empty;

    // Имя, которое пользователь видит в интерфейсе хранилища и Embed.
    public string DisplayName { get; set; } = string.Empty;

    // MIME-тип исходного файла.
    public string ContentType { get; set; } = "application/octet-stream";

    // Размер исходного файла в байтах.
    public long OriginalSizeBytes { get; set; }

    // Длительность в секундах. Пока может быть null, если метаданные не извлечены.
    public double? DurationSeconds { get; set; }

    // Идентификатор пользователя, загрузившего файл, если он известен.
    public Guid? UploadedByUserId { get; set; }

    // Дата загрузки файла.
    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    // Пользовательский комментарий к видео в медиатеке.
    public string Comment { get; set; } = string.Empty;
}

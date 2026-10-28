namespace BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;

// Прикладной сервис общего мультимедиа-хранилища.
public interface IMediaServerService
{
    Task<IReadOnlyList<MediaVideoInfo>> GetVideosAsync(
        Guid? spaceId = null,
        string? filter = null,
        CancellationToken cancellationToken = default);

    Task<MediaVideoInfo?> GetVideoAsync(Guid videoId, CancellationToken cancellationToken = default);

    Task<MediaVideoInfo> UploadVideoAsync(
        Stream content,
        string fileName,
        string? contentType,
        long? length,
        CancellationToken cancellationToken = default,
        IProgress<long>? progress = null,
        Guid? spaceId = null,
        string? clientUploadToken = null);

    // Полное состояние metadata доступно собственному рабочему экземпляру библиотеки.
    Task<Entities.MediaVideo> GetEditableVideoDataAsync(Guid videoId, CancellationToken ct = default);

    Task DeleteVideoAsync(Guid videoId, CancellationToken cancellationToken = default);

    Task<MediaVideoFileContent?> GetVideoFileAsync(Guid videoId, CancellationToken cancellationToken = default);
}

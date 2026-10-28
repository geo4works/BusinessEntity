namespace BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;

public interface IMediaServerUploadManager
{
    Task<string> SaveIncomingVideoToTemporaryFileAsync(
        Guid jobId,
        Stream content,
        CancellationToken cancellationToken = default);

    void EnqueueVideoProcessing(
        Guid jobId,
        string temporaryFilePath,
        string fileName,
        string? contentType,
        long? length,
        Guid? spaceId,
        string? clientUploadToken,
        Guid userId);
}

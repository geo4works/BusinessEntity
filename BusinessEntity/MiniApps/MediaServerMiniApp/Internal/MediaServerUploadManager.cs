using BusinessEntity.Infrastructure.Bus;
using System.Buffers;
using System.Threading.Channels;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

// Long-running upload/process manager owned by MediaServerMiniApp.
public sealed class MediaServerUploadManager : BackgroundService, IMediaServerUploadManager
{
    private readonly Channel<VideoUploadWorkItem> _queue = Channel.CreateUnbounded<VideoUploadWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaServerUploadJobRegistry _uploadJobs;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<MediaServerUploadManager> _logger;
    private readonly string _temporaryRoot;

    public MediaServerUploadManager(
        IServiceScopeFactory scopeFactory,
        MediaServerUploadJobRegistry uploadJobs,
        IMessageBus messageBus,
        IWebHostEnvironment environment,
        ILogger<MediaServerUploadManager> logger)
    {
        _scopeFactory = scopeFactory;
        _uploadJobs = uploadJobs;
        _messageBus = messageBus;
        _logger = logger;

        var contentRoot = environment.ContentRootPath ?? AppContext.BaseDirectory;
        _temporaryRoot = Path.Combine(contentRoot, "App_Data", "MediaServerUploadJobs");
        Directory.CreateDirectory(_temporaryRoot);
    }

    public async Task<string> SaveIncomingVideoToTemporaryFileAsync(
        Guid jobId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        Directory.CreateDirectory(_temporaryRoot);
        var temporaryFilePath = Path.Combine(_temporaryRoot, $"{jobId:D}.upload");

        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 128);
        var totalBytes = 0L;
        try
        {
            await using var output = new FileStream(
                temporaryFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                useAsync: true);

            while (true)
            {
                var bytesRead = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead <= 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytes += bytesRead;
                _uploadJobs.ReportVideoUploadProgress(jobId, totalBytes);
            }

            return temporaryFilePath;
        }
        catch
        {
            TryDeleteFile(temporaryFilePath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void EnqueueVideoProcessing(
        Guid jobId,
        string temporaryFilePath,
        string fileName,
        string? contentType,
        long? length,
        Guid? spaceId,
        string? clientUploadToken,
        Guid userId)
    {
        if (string.IsNullOrWhiteSpace(temporaryFilePath))
        {
            throw new ArgumentException("Temporary file path is required.", nameof(temporaryFilePath));
        }

        _queue.Writer.TryWrite(new VideoUploadWorkItem(
            jobId,
            temporaryFilePath,
            fileName,
            contentType,
            length,
            spaceId,
            clientUploadToken, userId));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessVideoUploadAsync(item, stoppingToken);
        }
    }

    private async Task ProcessVideoUploadAsync(VideoUploadWorkItem item, CancellationToken stoppingToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            _uploadJobs.GetCancellationToken(item.JobId));

        try
        {
            if (!File.Exists(item.TemporaryFilePath))
            {
                throw new FileNotFoundException("Temporary upload file was not found.", item.TemporaryFilePath);
            }

            linkedCts.Token.ThrowIfCancellationRequested();
            _uploadJobs.MarkVideoUploadJobProcessing(item.JobId);

            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<BusRequestScope>().SetUser(item.UserId);
            var mediaServerService = scope.ServiceProvider.GetRequiredService<IMediaServerService>();
            await using var input = new FileStream(
                item.TemporaryFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                useAsync: true);

            var result = await mediaServerService.UploadVideoAsync(
                input,
                item.FileName,
                item.ContentType,
                item.Length,
                linkedCts.Token,
                progress: null,
                spaceId: item.SpaceId,
                clientUploadToken: item.ClientUploadToken);

            _uploadJobs.CompleteVideoUploadJob(item.JobId, result);
        }
        catch (OperationCanceledException)
        {
            _uploadJobs.MarkVideoUploadJobCancelled(item.JobId);
            SendFailedMessage(item, "Загрузка отменена.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media video upload job {JobId} failed.", item.JobId);
            _uploadJobs.FailVideoUploadJob(item.JobId, ex.Message);
            SendFailedMessage(item, ex.Message);
        }
        finally
        {
            TryDeleteFile(item.TemporaryFilePath);
        }
    }

    private void SendFailedMessage(VideoUploadWorkItem item, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(item.ClientUploadToken))
        {
            return;
        }

        _messageBus.SendMessage(new MediaVideoUploadFailedMessage(
            item.ClientUploadToken,
            string.IsNullOrWhiteSpace(errorMessage) ? "Не удалось загрузить видео." : errorMessage,
            item.SpaceId));
    }

    // Ошибка уборки временного файла диагностируется и не подменяет исход операции.
    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Cannot remove temporary upload file {Path}", path);
        }
    }

    private sealed record VideoUploadWorkItem(
        Guid JobId,
        string TemporaryFilePath,
        string FileName,
        string? ContentType,
        long? Length,
        Guid? SpaceId,
        string? ClientUploadToken,
        Guid UserId);
}

using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Internal;
using Microsoft.AspNetCore.Mvc;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Controllers;

// API физической отдачи и upload-а файлов MediaServerMiniApp.
[ApiController]
[Route("media-server-files")]
public sealed class MediaServerFilesController : ControllerBase
{
    private readonly IMediaServerService _mediaServerService;
    private readonly MediaServerUploadJobRegistry _uploadJobs;
    private readonly IMediaServerUploadManager _uploadManager;
    private readonly IUserContextService _userContextService;
    private readonly IUserConnector _userConnector;

    public MediaServerFilesController(
        IMediaServerService mediaServerService,
        MediaServerUploadJobRegistry uploadJobs,
        IMediaServerUploadManager uploadManager,
        IUserContextService userContextService, IUserConnector userConnector)
    {
        _mediaServerService = mediaServerService;
        _uploadJobs = uploadJobs;
        _uploadManager = uploadManager;
        _userContextService = userContextService;
        _userConnector = userConnector;
    }

    [HttpGet("videos/{videoId:guid}/original")]
    public async Task<IActionResult> GetVideo(Guid videoId, CancellationToken cancellationToken)
    {
        var file = await _mediaServerService.GetVideoFileAsync(videoId, cancellationToken);
        if (file == null)
        {
            return NotFound();
        }

        return PhysicalFile(
            file.PhysicalPath,
            file.ContentType,
            file.FileName,
            enableRangeProcessing: true);
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("videos")]
    [RequestSizeLimit(2L * 1024L * 1024L * 1024L)]
    public async Task<IActionResult> UploadVideo(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length <= 0)
        {
            return BadRequest("Файл видео не передан.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _mediaServerService.UploadVideoAsync(
                stream,
                file.FileName,
                file.ContentType,
                file.Length,
                cancellationToken,
                spaceId: RequireCurrentSpaceId());

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("video-upload-jobs")]
    public IActionResult GetUploadJobs()
    {
        return Ok(_uploadJobs.GetVideoUploadJobs());
    }

    [HttpGet("video-upload-jobs/{jobId:guid}")]
    public IActionResult GetUploadJob(Guid jobId)
    {
        var job = _uploadJobs.GetVideoUploadJob(jobId);
        return job == null ? NotFound() : Ok(job);
    }

    [HttpPost("video-upload-jobs/{jobId:guid}/cancel")]
    public IActionResult CancelUploadJob(Guid jobId)
    {
        return _uploadJobs.CancelVideoUploadJob(jobId) ? Ok() : NotFound();
    }

    [HttpPut("video-upload-jobs/{jobId:guid}")]
    [RequestSizeLimit(2L * 1024L * 1024L * 1024L)]
    public async Task<IActionResult> UploadVideoJob(Guid jobId)
    {
        var fileName = DecodeHeaderValue(Request.Headers["X-File-Name"].FirstOrDefault());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest("Имя видеофайла не передано.");
        }

        var contentType = Request.Headers["X-Content-Type"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = Request.ContentType;
        }

        var length = TryReadLength(Request.Headers["X-File-Length"].FirstOrDefault()) ?? Request.ContentLength;
        var clientUploadToken = DecodeHeaderValue(Request.Headers["X-Client-Upload-Token"].FirstOrDefault());
        var spaceId = RequireCurrentSpaceId();
        _uploadJobs.RegisterVideoUploadJob(
            jobId,
            fileName,
            contentType ?? "application/octet-stream",
            length,
            spaceId,
            clientUploadToken);
        _uploadJobs.MarkVideoUploadJobUploading(jobId);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            HttpContext.RequestAborted,
            _uploadJobs.GetCancellationToken(jobId));

        try
        {
            var temporaryFilePath = await _uploadManager.SaveIncomingVideoToTemporaryFileAsync(
                jobId,
                Request.Body,
                linkedCts.Token);

            _uploadJobs.MarkVideoUploadJobProcessing(jobId);
            var actor = await _userConnector.EnsureCurrentUserAsync(linkedCts.Token)
                ?? throw new UnauthorizedAccessException("Для загрузки видео нужно войти.");
            _uploadManager.EnqueueVideoProcessing(
                jobId,
                temporaryFilePath,
                fileName,
                contentType,
                length,
                spaceId,
                clientUploadToken, actor.Id);

            return Accepted(_uploadJobs.GetVideoUploadJob(jobId));
        }
        catch (OperationCanceledException)
        {
            _uploadJobs.MarkVideoUploadJobCancelled(jobId);
            return StatusCode(499, "Загрузка отменена.");
        }
        catch (InvalidOperationException ex)
        {
            _uploadJobs.FailVideoUploadJob(jobId, ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _uploadJobs.FailVideoUploadJob(jobId, ex.Message);
            throw;
        }
    }

    private static string DecodeHeaderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Uri.UnescapeDataString(value);
    }

    private static long? TryReadLength(string? value)
    {
        return long.TryParse(value, out var length) && length >= 0 ? length : null;
    }

    private Guid RequireCurrentSpaceId()
    {
        if (_userContextService.CurrentSpaceId.HasValue)
        {
            return _userContextService.CurrentSpaceId.Value;
        }

        throw new InvalidOperationException("Не выбрано текущее пространство для загрузки видео.");
    }
}

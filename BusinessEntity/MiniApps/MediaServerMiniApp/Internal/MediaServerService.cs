using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using System.Buffers;
using System.Text.Json;
using BusinessEntity.Core.BaseClasses.Relations;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using ReactiveUI;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

// Реальная логика общего мультимедиа-хранилища.
public sealed class MediaServerService : IMediaServerService
{
    private const long MaxVideoBytes = 2L * 1024L * 1024L * 1024L;

    private readonly IDataProviderConnector _dataProviderConnector;
    private readonly IBusinessEntityFactory _businessEntityFactory;
    private readonly IMessageBus _messageBus;
    private readonly IUserConnector _userConnector;
    private readonly BusRequestScope _scope;
    private readonly ILogger<MediaServerService> _logger;

    // Владеет предметной моделью видео; постоянные файлы и метаданные записывает DataProvider.
    public MediaServerService(IDataProviderConnector dataProviderConnector, IBusinessEntityFactory businessEntityFactory,
        IMessageBus messageBus, BusRequestScope scope, IUserConnector userConnector, ILogger<MediaServerService> logger)
    {
        _dataProviderConnector = dataProviderConnector; _businessEntityFactory = businessEntityFactory;
        _messageBus = messageBus; _scope = scope; _userConnector = userConnector; _logger = logger;
    }

    public async Task<IReadOnlyList<MediaVideoInfo>> GetVideosAsync(
        Guid? spaceId = null,
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        var entities = await _dataProviderConnector.GetAllAsync(cancellationToken);
        var result = new List<MediaVideoInfo>();
        HashSet<Guid>? allowedVideoIds = null;

        if (spaceId.HasValue)
        {
            var relationType = BusinessEntityRelationTypeEnum.RelatesTo.ToString();
            allowedVideoIds = (await _dataProviderConnector.GetAllRelationsAsync(cancellationToken))
                .Where(x => x.RelationType == relationType &&
                            (x.ObjectAId == spaceId.Value || x.ObjectBId == spaceId.Value))
                .Select(x => x.ObjectAId == spaceId.Value ? x.ObjectBId : x.ObjectAId)
                .ToHashSet();
        }

        var normalizedFilter = (filter ?? string.Empty).Trim();

        foreach (var entity in entities
                     .Where(x => x.EntityType == BusinessEntityTypeEnum.MediaVideo)
                     .Where(x => allowedVideoIds == null || allowedVideoIds.Contains(x.Id))
                     .OrderByDescending(x => x.CreatedDate))
        {
            var data = await _dataProviderConnector.GetDataAsync<MediaVideo>(entity.Id, cancellationToken);
            if (data == null)
            {
                continue;
            }

            var info = ToInfo(entity.Id, data);
            if (!MatchesFilter(info, normalizedFilter))
            {
                continue;
            }

            result.Add(info);
        }

        return result;
    }

    public async Task<MediaVideoInfo?> GetVideoAsync(Guid videoId, CancellationToken cancellationToken = default)
    {
        var data = await _dataProviderConnector.GetDataAsync<MediaVideo>(videoId, cancellationToken);
        return data == null ? null : ToInfo(videoId, data);
    }

    // Использует лишь временную подготовку потока; карточка, payload, файл и связь коммитятся вместе.
    public async Task<MediaVideoInfo> UploadVideoAsync(Stream content, string fileName, string? contentType,
        long? length, CancellationToken cancellationToken = default, IProgress<long>? progress = null,
        Guid? spaceId = null, string? clientUploadToken = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var safeName = SanitizeFileName(Path.GetFileName(fileName));
        if (string.IsNullOrWhiteSpace(safeName)) throw new ArgumentException("Имя видео обязательно.", nameof(fileName));
        var mime = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        if (!IsSupportedVideo(safeName, mime)) throw new InvalidOperationException("Файл не является поддерживаемым видео.");
        if (length > MaxVideoBytes) throw new InvalidOperationException("Видео слишком большое для загрузки.");
        var actor = await ResolveCurrentUserIdAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("Для загрузки нужно войти.");
        var reusable = content is FileStream stream && stream.Position == 0;
        string temporary;
        if (reusable) temporary = ((FileStream)content).Name;
        else
        {
            var directory = Path.Combine(Path.GetTempPath(), "be-media-preparation");
            Directory.CreateDirectory(directory); temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".upload");
        }
        try
        {
            if (!reusable)
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                    await CopyWithProgressAsync(content, output, progress, cancellationToken);
            var actualLength = new FileInfo(temporary).Length;
            if (actualLength > MaxVideoBytes) throw new InvalidOperationException("Видео слишком большое для загрузки.");
            var name = Path.GetFileNameWithoutExtension(safeName);
            var entity = _businessEntityFactory.Create<MediaVideo>(BusinessEntityTypeEnum.MediaVideo, name);
            var data = new MediaVideo { Id = entity.Id, Name = name, Tag = BusinessEntityTypeEnum.MediaVideo.ToString(),
                FileName = safeName, DisplayName = name, ContentType = mime, OriginalSizeBytes = actualLength,
                UploadedByUserId = actor, UploadedDate = DateTime.UtcNow };
            var relations = spaceId.HasValue && spaceId.Value != Guid.Empty
                ? new[] { new BusinessEntityRelation { ObjectAId = spaceId.Value, ObjectBId = entity.Id,
                    RelationType = BusinessEntityRelationTypeEnum.RelatesTo.ToString(), RelationParams = "media-video-space" } } : [];
            await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "MediaServerMiniApp", null)
            {
                Entities = [entity], Payloads = [new(entity.Id, 0, data)], Relations = relations,
                Files = [new(entity.Id, "video", "original", safeName, mime, [], temporary)]
            }, cancellationToken);
            var result = ToInfo(entity.Id, data);
            _messageBus.SendMessage(new MediaVideoUploadedMessage(clientUploadToken, result, spaceId));
            return result;
        }
        finally
        {
            if (!reusable)
                try { File.Delete(temporary); }
                catch (Exception exception) { _logger.LogWarning(exception, "Temporary media preparation cleanup failed"); }
        }
    }

    public async Task<MediaVideoInfo> RenameVideoAsync(Guid videoId, string displayName, CancellationToken cancellationToken = default)
    {
        var trimmed = (displayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Отображаемое имя видео не должно быть пустым.");
        }

        var entity = await _dataProviderConnector.GetByIdAsync(videoId, cancellationToken)
            ?? throw new KeyNotFoundException($"Видео '{videoId}' не найдено.");
        var data = await _dataProviderConnector.GetDataAsync<MediaVideo>(videoId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payload видео '{videoId}' не найден.");

        entity.Name = trimmed;
        entity.LastModifiedDate = DateTime.UtcNow;
        data.Name = trimmed;
        data.DisplayName = trimmed;

        await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "MediaServerMiniApp", null)
            { Entities = [entity], Payloads = [new(videoId, data.Version, data)] }, cancellationToken);
        return ToInfo(videoId, data);
    }

    public async Task<MediaVideoInfo> UpdateVideoCommentAsync(Guid videoId, string comment, CancellationToken cancellationToken = default)
    {
        var data = await _dataProviderConnector.GetDataAsync<MediaVideo>(videoId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payload видео '{videoId}' не найден.");

        data.Comment = comment ?? string.Empty;
        await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "MediaServerMiniApp", null)
            { Payloads = [new(videoId, data.Version, data)] }, cancellationToken);
        return ToInfo(videoId, data);
    }

    // Удаляет связанные SQL-записи пакетом; уборка постоянного файла происходит после commit у DataProvider.
    public async Task DeleteVideoAsync(Guid videoId, CancellationToken cancellationToken = default)
        => await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "MediaServerMiniApp", null)
            { DeleteEntities = [videoId] }, cancellationToken);

    // Получает проверенный серверный FileResult из владельца хранения без обхода его метаданных.
    public async Task<MediaVideoFileContent?> GetVideoFileAsync(Guid videoId, CancellationToken cancellationToken = default)
    {
        var data = await _dataProviderConnector.GetDataAsync<MediaVideo>(videoId, cancellationToken);
        if (data == null) return null;
        var file = await _dataProviderConnector.GetStoredFileAsync(videoId, "video", "original", cancellationToken)
            ?? throw new InvalidDataException("Отсутствует обязательный оригинал видео.");
        return new MediaVideoFileContent { PhysicalPath = file.PhysicalPath, FileName = file.FileName, ContentType = file.ContentType };
    }

    private async Task<Guid?> ResolveCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        if (_scope.UserId.HasValue) return _scope.UserId;
        var user = await _userConnector.EnsureCurrentUserAsync(cancellationToken);
        return user?.Id;
    }

    private static async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 128);
        var totalBytes = 0L;
        try
        {
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead <= 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytes += bytesRead;
                if (totalBytes > MaxVideoBytes) throw new InvalidOperationException("Видео слишком большое для загрузки.");
                progress?.Report(totalBytes);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static MediaVideoInfo ToInfo(Guid id, MediaVideo data)
    {
        return new MediaVideoInfo
        {
            Id = id,
            FileName = data.FileName ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(data.DisplayName)
                ? data.FileName ?? string.Empty
                : data.DisplayName,
            ContentType = string.IsNullOrWhiteSpace(data.ContentType) ? "application/octet-stream" : data.ContentType,
            OriginalSizeBytes = data.OriginalSizeBytes,
            DurationSeconds = data.DurationSeconds,
            UploadedByUserId = data.UploadedByUserId,
            UploadedDate = data.UploadedDate,
            EmbedUrl = BuildEmbedUrl(id),
            Comment = data.Comment ?? string.Empty
        };
    }

    private static bool MatchesFilter(MediaVideoInfo video, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(video.DisplayName, filter) ||
               Contains(video.FileName, filter) ||
               Contains(video.Comment, filter);
    }

    private static bool Contains(string? value, string filter)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildEmbedUrl(Guid videoId)
    {
        return $"/media-server-files/videos/{videoId:D}/original";
    }

    private static bool IsSupportedVideo(string fileName, string contentType)
    {
        if ((contentType ?? string.Empty).StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp4" or ".webm" or ".ogv" or ".ogg" or ".mov" or ".m4v" => true,
            _ => false
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
            .ToHashSet();

        var sanitized = new string((value ?? string.Empty)
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        return sanitized.Trim();
    }

}

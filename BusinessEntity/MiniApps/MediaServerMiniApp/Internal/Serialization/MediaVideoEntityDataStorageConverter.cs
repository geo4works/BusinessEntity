using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using System.Text.Json.Serialization;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.Entities;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal.Serialization;

// Конвертер payload-а видео из общего мультимедиа-хранилища.
internal sealed class MediaVideoEntityDataStorageConverter : EntityDataStorageConverterBase<MediaVideo>
{
    public override BusinessEntityTypeEnum SupportedType => global::BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.MediaVideoType.Id;
    public override string StorageKind => global::BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.MediaVideoType.Key;

    public override string SerializePayload(MediaVideo data)
    {
        if (string.IsNullOrWhiteSpace(data.FileName) || string.IsNullOrWhiteSpace(data.ContentType) || data.OriginalSizeBytes <= 0 || data.UploadedDate == default)
            throw new InvalidDataException("Не заданы обязательные метаданные видео.");
        return SerializeBody(new MediaVideoPayloadBody
        {
            Tag = data.Tag ?? string.Empty,
            FileName = data.FileName ?? string.Empty,
            DisplayName = data.DisplayName ?? string.Empty,
            ContentType = data.ContentType ?? "application/octet-stream",
            OriginalSizeBytes = data.OriginalSizeBytes,
            DurationSeconds = data.DurationSeconds,
            UploadedByUserId = data.UploadedByUserId,
            UploadedDate = data.UploadedDate,
            Comment = data.Comment ?? string.Empty
        });
    }

    public override MediaVideo DeserializePayload(string payloadJson)
    {
        var body = DeserializeBody<MediaVideoPayloadBody>(payloadJson);
        if (string.IsNullOrWhiteSpace(body.FileName) || string.IsNullOrWhiteSpace(body.ContentType) || body.OriginalSizeBytes <= 0 || body.UploadedDate == default)
            throw new InvalidDataException("Повреждены обязательные метаданные видео.");
        return new MediaVideo
        {
            Tag = body.Tag,
            FileName = body.FileName,
            DisplayName = body.DisplayName,
            ContentType = body.ContentType,
            OriginalSizeBytes = body.OriginalSizeBytes,
            DurationSeconds = body.DurationSeconds,
            UploadedByUserId = body.UploadedByUserId,
            UploadedDate = body.UploadedDate,
            Comment = body.Comment
        };
    }

    private sealed class MediaVideoPayloadBody
    {
        [JsonPropertyName("tag")]
        [JsonRequired]
        public string Tag { get; set; } = string.Empty;

        [JsonPropertyName("fileName")]
        [JsonRequired]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        [JsonRequired]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("contentType")]
        [JsonRequired]
        public string ContentType { get; set; } = string.Empty;

        [JsonPropertyName("originalSizeBytes")]
        [JsonRequired]
        public long OriginalSizeBytes { get; set; }

        [JsonPropertyName("durationSeconds")]
        [JsonRequired]
        public double? DurationSeconds { get; set; }

        [JsonPropertyName("uploadedByUserId")]
        [JsonRequired]
        public Guid? UploadedByUserId { get; set; }

        [JsonPropertyName("uploadedDate")]
        [JsonRequired]
        public DateTime UploadedDate { get; set; }



        [JsonPropertyName("comment")]
        [JsonRequired]
        public string Comment { get; set; } = string.Empty;
    }
}

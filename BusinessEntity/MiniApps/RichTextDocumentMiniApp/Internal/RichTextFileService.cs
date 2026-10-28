using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.BaseClasses.Relations;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;

using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport;
using BusinessEntity.WebLogger.Services;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// RichTextFileService: выделенный сценарий владельца статьи без общего смешанного helper.
public sealed class RichTextFileService
{
        private const long MaxEmbeddedImageUploadBytes = 20L * 1024L * 1024L;
        private readonly IDataProviderConnector _dataProviderConnector;
        private readonly RichTextDocumentReader _reader;
        // Подключает только зависимости своего предметного сценария.
        public RichTextFileService(IDataProviderConnector dataProviderConnector, RichTextDocumentReader reader)
        { _dataProviderConnector = dataProviderConnector; _reader = reader; }
        // Обрабатывает файлы статьи через DataProvider.
        public Task<RichTextEmbeddedFileContent?> GetRichTextEmbeddedFileAsync(Guid entityId, string imageId,
            string variant, CancellationToken ct = default)
            => _dataProviderConnector.GetRichTextEmbeddedFileAsync(entityId, imageId, variant, ct);

        // Обрабатывает файлы статьи через DataProvider.
        public async Task<RichTextEmbeddedImageUploadResult> SaveRichTextEmbeddedImageAsync(
            Guid entityId,
            Stream content,
            string? fileName,
            string? contentType,
            CancellationToken ct = default)
        {
            if (entityId == Guid.Empty)
            {
                throw new ArgumentException("Entity id is required.", nameof(entityId));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var shell = await _reader.GetRichTextDocumentShellAsync(entityId, ct);
            if (shell == null)
            {
                throw new InvalidOperationException("Rich-text документ не найден.");
            }

            var normalizedContentType = NormalizeImageContentType(contentType);
            var normalizedFileName = NormalizeEmbeddedFileName(fileName, normalizedContentType);
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            if (buffer.Length <= 0)
            {
                throw new InvalidOperationException("Файл изображения пустой.");
            }

            if (buffer.Length > MaxEmbeddedImageUploadBytes)
            {
                throw new InvalidOperationException(
                    $"Размер изображения превышает лимит {MaxEmbeddedImageUploadBytes / 1024 / 1024} MB.");
            }

            var imageId = Guid.NewGuid().ToString("D");
            var variant = "original";
            var embeddedFile = new RichTextEmbeddedFile
            {
                ImageId = imageId,
                Variant = variant,
                FileName = normalizedFileName,
                ContentType = normalizedContentType,
                Content = buffer.ToArray()
            };

            await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
                { Files = [RichTextFileChanges.FileChange(entityId, embeddedFile)] }, ct);

            return new RichTextEmbeddedImageUploadResult
            {
                ImageId = imageId,
                Variant = variant,
                Url = $"/rich-document-files/{entityId:D}/images/{Uri.EscapeDataString(imageId)}/{variant}",
                FileName = normalizedFileName,
                ContentType = normalizedContentType
            };
        }

        // Обрабатывает файлы статьи через DataProvider.
        private static string NormalizeImageContentType(string? contentType)
        {
            var normalized = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType.Trim();

            if (!normalized.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Можно загружать только изображения.");
            }

            return normalized;
        }

        // Обрабатывает файлы статьи через DataProvider.
        private static string NormalizeEmbeddedFileName(string? fileName, string contentType)
        {
            var normalized = Path.GetFileName(fileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = $"clipboard-image.{GetExtensionFromContentType(contentType)}";
            }

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                normalized = normalized.Replace(invalidChar, '_');
            }

            return normalized.Length <= 120
                ? normalized
                : normalized[^120..];
        }

        // Обрабатывает файлы статьи через DataProvider.
        private static string GetExtensionFromContentType(string contentType)
        {
            return contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => "jpg",
                "image/png" => "png",
                "image/gif" => "gif",
                "image/webp" => "webp",
                "image/svg+xml" => "svg",
                _ => "bin"
            };
        }
}

using BusinessEntity.Services;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using System.Text.Json;

using static BusinessEntity.MiniApps.UserMiniApp.Internal.UserValueRules;
namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// UserPreferencesService владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserPreferencesService
{
        private readonly IUserMiniAppRepository<UserPropertyDto> _userPropertyRepository;
        private readonly UserIdentityService _identityService;
        // Получает зависимости выделенного сценария.
        public UserPreferencesService(IUserMiniAppRepository<UserPropertyDto> userPropertyRepository, UserIdentityService identityService)
        { _userPropertyRepository = userPropertyRepository; _identityService = identityService; }
        // Выполняет сценарий GetRichDocBookmarksAsync внутри владельца User.
        public async Task<IReadOnlyList<RichTextDocumentBookmark>> GetRichDocBookmarksAsync(
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Array.Empty<RichTextDocumentBookmark>();
            }

            var payload = await ReadBookmarksPayloadAsync(user.Id, cancellationToken);
            return payload.Bookmarks
                .Where(x => x.DocumentId == documentId)
                .OrderBy(x => x.ChunkSortOrder)
                .ThenBy(x => x.BlockIndex)
                .ThenBy(x => x.CreatedDate)
                .ToList();
        }

        // Выполняет сценарий AddRichDocBookmarkAsync внутри владельца User.
        public async Task<RichTextDocumentBookmark?> AddRichDocBookmarkAsync(
            Guid documentId,
            RichTextDocumentTextSelection? selection,
            CancellationToken cancellationToken = default)
        {
            if (documentId == Guid.Empty || selection?.Position == null)
            {
                return null;
            }

            var selectedText = NormalizeBookmarkText(selection.Text);
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                return null;
            }

            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return null;
            }

            var payload = await ReadBookmarksPayloadAsync(user.Id, cancellationToken);
            var bookmark = new RichTextDocumentBookmark
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkSortOrder = selection.Position.ChunkSortOrder,
                BlockIndex = selection.Position.BlockIndex,
                SelectedText = selectedText,
                Label = BuildBookmarkLabel(selectedText),
                CreatedDate = DateTime.UtcNow
            };

            payload.Bookmarks.Add(bookmark);
            await UpsertBookmarksPayloadAsync(user.Id, payload, cancellationToken);
            return bookmark;
        }

        // Выполняет сценарий DeleteRichDocBookmarkAsync внутри владельца User.
        public async Task<bool> DeleteRichDocBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken = default)
        {
            if (bookmarkId == Guid.Empty)
            {
                return false;
            }

            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return false;
            }

            var payload = await ReadBookmarksPayloadAsync(user.Id, cancellationToken);
            var removedCount = payload.Bookmarks.RemoveAll(x => x.Id == bookmarkId);
            if (removedCount == 0)
            {
                return false;
            }

            await UpsertBookmarksPayloadAsync(user.Id, payload, cancellationToken);
            return true;
        }

        // Выполняет сценарий GetRichDocDisplayedLevelAsync внутри владельца User.
        public async Task<int> GetRichDocDisplayedLevelAsync(
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            if (documentId == Guid.Empty)
            {
                return 1;
            }

            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return 1;
            }

            var property = await ReadDisplayedLevelPropertyAsync(user.Id, documentId, cancellationToken);
            return NormalizeDisplayedLevel(property?.DisplayLevelCount ?? 1);
        }

        // Выполняет сценарий SaveRichDocDisplayedLevelAsync внутри владельца User.
        public async Task SaveRichDocDisplayedLevelAsync(
            Guid documentId,
            int displayLevelCount,
            CancellationToken cancellationToken = default)
        {
            if (documentId == Guid.Empty)
            {
                return;
            }

            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return;
            }

            await UpsertDisplayedLevelPropertyAsync(
                user.Id,
                new RichDocDisplayedLevelProperty
                {
                    DocumentId = documentId,
                    DisplayLevelCount = NormalizeDisplayedLevel(displayLevelCount)
                },
                cancellationToken);
        }

        // Выполняет сценарий GetCurrentUserPropertiesAsync внутри владельца User.
        public async Task<IReadOnlyList<UserPropertyDto>> GetCurrentUserPropertiesAsync(
            int propertyType,
            CancellationToken cancellationToken = default)
        {
            if (propertyType <= (int)UserPropertyTypeEnum.Undefined)
            {
                return Array.Empty<UserPropertyDto>();
            }

            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return Array.Empty<UserPropertyDto>();
            }

            return (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == user.Id && x.PropertyType == propertyType,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .Select(CloneUserProperty)
                .ToList();
        }

        // Выполняет сценарий UpsertCurrentUserPropertyAsync внутри владельца User.
        public async Task<UserPropertyDto?> UpsertCurrentUserPropertyAsync(
            Guid? propertyId,
            int propertyType,
            string data,
            string metadata,
            CancellationToken cancellationToken = default)
        {
            if (propertyType <= (int)UserPropertyTypeEnum.Undefined)
            {
                throw new ArgumentOutOfRangeException(nameof(propertyType), "Тип пользовательского свойства должен быть положительным.");
            }

            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            UserPropertyDto? property = null;
            if (propertyId.HasValue && propertyId.Value != Guid.Empty)
            {
                property = (await _userPropertyRepository.GetAllAsync(
                        x => x.Id == propertyId.Value && x.ParentEntityId == user.Id,
                        cancellationToken))
                    .FirstOrDefault();

                if (property != null && property.PropertyType != propertyType)
                {
                    throw new InvalidOperationException("Нельзя обновить пользовательское свойство другим типом.");
                }
            }

            if (property == null)
            {
                property = new UserPropertyDto
                {
                    Id = Guid.NewGuid(),
                    DateCreated = now,
                    DateLastModified = now,
                    ParentEntityId = user.Id,
                    PropertyType = propertyType,
                    Data = data ?? string.Empty,
                    Metadata = metadata ?? string.Empty
                };

                await _userPropertyRepository.AddAsync(property, cancellationToken);
                return CloneUserProperty(property);
            }

            property.DateLastModified = now;
            property.Data = data ?? string.Empty;
            property.Metadata = metadata ?? string.Empty;
            await _userPropertyRepository.UpdateAsync(property, cancellationToken);
            return CloneUserProperty(property);
        }

        // Выполняет сценарий DeleteCurrentUserPropertyAsync внутри владельца User.
        public async Task<bool> DeleteCurrentUserPropertyAsync(
            Guid propertyId,
            CancellationToken cancellationToken = default)
        {
            if (propertyId == Guid.Empty)
            {
                return false;
            }

            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return false;
            }

            var property = (await _userPropertyRepository.GetAllAsync(
                    x => x.Id == propertyId && x.ParentEntityId == user.Id,
                    cancellationToken))
                .FirstOrDefault();

            if (property == null)
            {
                return false;
            }

            await _userPropertyRepository.DeleteAsync(property.Id, cancellationToken);
            return true;
        }

        // Выполняет сценарий GetDocPrintPresetsAsync внутри владельца User.
        public async Task<DocPrintSettingsPresetCollection> GetDocPrintPresetsAsync(CancellationToken cancellationToken = default)
        {
            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return new DocPrintSettingsPresetCollection();
            }

            return await ReadDocPrintPresetsPayloadAsync(user.Id, cancellationToken);
        }

        // Выполняет сценарий SaveDocPrintPresetAsync внутри владельца User.
        public async Task<DocPrintSettingsPreset> SaveDocPrintPresetAsync(
            DocPrintSettingsPreset preset,
            CancellationToken cancellationToken = default)
        {
            var normalizedPreset = NormalizeDocPrintPreset(preset);
            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                throw new InvalidOperationException("Текущий пользователь не найден.");
            }

            var payload = await ReadDocPrintPresetsPayloadAsync(user.Id, cancellationToken);
            payload.Presets.RemoveAll(x => string.Equals(x.Name, normalizedPreset.Name, StringComparison.OrdinalIgnoreCase));
            payload.Presets.Add(normalizedPreset);
            payload.SelectedPresetName = normalizedPreset.Name;
            payload = NormalizeDocPrintPresetsPayload(payload);

            await UpsertDocPrintPresetsPayloadAsync(user.Id, payload, cancellationToken);
            return normalizedPreset;
        }

        // Выполняет сценарий DeleteDocPrintPresetAsync внутри владельца User.
        public async Task<bool> DeleteDocPrintPresetAsync(string presetName, CancellationToken cancellationToken = default)
        {
            var normalizedName = NormalizeOptionalText(presetName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return false;
            }

            normalizedName = NormalizePrintPresetName(normalizedName);

            var user = await _identityService.EnsureCurrentUserAsync(cancellationToken);
            if (user == null)
            {
                return false;
            }

            var payload = await ReadDocPrintPresetsPayloadAsync(user.Id, cancellationToken);
            var removedCount = payload.Presets.RemoveAll(x => string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (removedCount == 0)
            {
                return false;
            }

            if (string.Equals(payload.SelectedPresetName, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                payload.SelectedPresetName = payload.Presets.FirstOrDefault()?.Name ?? string.Empty;
            }

            payload = NormalizeDocPrintPresetsPayload(payload);
            await UpsertDocPrintPresetsPayloadAsync(user.Id, payload, cancellationToken);
            return true;
        }

        // Выполняет сценарий ReadBookmarksPayloadAsync внутри владельца User.
        internal async Task<RichTextDocumentBookmarksPayload> ReadBookmarksPayloadAsync(Guid userId, CancellationToken cancellationToken)
        {
            var property = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.RichDocBookmarks,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .FirstOrDefault();

            if (property == null)
            {
                return new RichTextDocumentBookmarksPayload();
            }

            var payload = JsonSerializer.Deserialize<RichTextDocumentBookmarksPayload>(property.Data, UserMiniAppJsonOptions.Default);
            if (payload?.SchemaVersion != 1 || payload.Kind != "RichDocBookmarks" || payload.Bookmarks == null)
                throw new InvalidDataException("Неизвестный формат закладок пользователя.");
            return payload;
        }

        // Выполняет сценарий ReadDocPrintPresetsPayloadAsync внутри владельца User.
        internal async Task<DocPrintSettingsPresetCollection> ReadDocPrintPresetsPayloadAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            var property = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.DocPrintPresets,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .FirstOrDefault();

            if (property == null)
            {
                return new DocPrintSettingsPresetCollection();
            }

            var payload = JsonSerializer.Deserialize<DocPrintSettingsPresetCollection>(property.Data, UserMiniAppJsonOptions.Default);
            if (payload?.SchemaVersion != 1 || payload.Kind != nameof(DocPrintSettingsPresetCollection))
                throw new InvalidDataException("Неизвестный формат пресетов печати.");
            return NormalizeDocPrintPresetsPayload(payload);
        }

        // Выполняет сценарий ReadDisplayedLevelPropertyAsync внутри владельца User.
        internal async Task<RichDocDisplayedLevelProperty?> ReadDisplayedLevelPropertyAsync(
            Guid userId,
            Guid documentId,
            CancellationToken cancellationToken)
        {
            var properties = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.RichDocDisplayedLevelProperty,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .ToList();

            foreach (var property in properties)
            {
                var payload = TryReadDisplayedLevelProperty(property.Data);
                if (payload?.DocumentId == documentId)
                {
                    return payload;
                }
            }

            return null;
        }

        // Выполняет сценарий UpsertBookmarksPayloadAsync внутри владельца User.
        internal async Task UpsertBookmarksPayloadAsync(
            Guid userId,
            RichTextDocumentBookmarksPayload payload,
            CancellationToken cancellationToken)
        {
            var properties = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.RichDocBookmarks,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .ToList();

            var now = DateTime.UtcNow;
            var data = JsonSerializer.Serialize(payload, UserMiniAppJsonOptions.Default);
            var metadata = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    kind = "RichDocBookmarksMetadata",
                    bookmarkCount = payload.Bookmarks.Count
                },
                UserMiniAppJsonOptions.Default);

            var property = properties.FirstOrDefault();
            if (property == null)
            {
                await _userPropertyRepository.AddAsync(
                    new UserPropertyDto
                    {
                        Id = Guid.NewGuid(),
                        DateCreated = now,
                        DateLastModified = now,
                        ParentEntityId = userId,
                        PropertyType = (int)UserPropertyTypeEnum.RichDocBookmarks,
                        Data = data,
                        Metadata = metadata
                    },
                    cancellationToken);
                return;
            }

            property.DateLastModified = now;
            property.Data = data;
            property.Metadata = metadata;
            await _userPropertyRepository.UpdateAsync(property, cancellationToken);

            foreach (var duplicate in properties.Skip(1))
            {
                await _userPropertyRepository.DeleteAsync(duplicate.Id, cancellationToken);
            }
        }

        // Выполняет сценарий UpsertDocPrintPresetsPayloadAsync внутри владельца User.
        internal async Task UpsertDocPrintPresetsPayloadAsync(
            Guid userId,
            DocPrintSettingsPresetCollection payload,
            CancellationToken cancellationToken)
        {
            var properties = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.DocPrintPresets,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .ToList();

            payload = NormalizeDocPrintPresetsPayload(payload);

            var now = DateTime.UtcNow;
            var data = JsonSerializer.Serialize(payload, UserMiniAppJsonOptions.Default);
            var metadata = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    kind = "DocPrintPresetsMetadata",
                    presetCount = payload.Presets.Count,
                    selectedPresetName = payload.SelectedPresetName
                },
                UserMiniAppJsonOptions.Default);

            var property = properties.FirstOrDefault();
            if (property == null)
            {
                await _userPropertyRepository.AddAsync(
                    new UserPropertyDto
                    {
                        Id = Guid.NewGuid(),
                        DateCreated = now,
                        DateLastModified = now,
                        ParentEntityId = userId,
                        PropertyType = (int)UserPropertyTypeEnum.DocPrintPresets,
                        Data = data,
                        Metadata = metadata
                    },
                    cancellationToken);
                return;
            }

            property.DateLastModified = now;
            property.Data = data;
            property.Metadata = metadata;
            await _userPropertyRepository.UpdateAsync(property, cancellationToken);

            foreach (var duplicate in properties.Skip(1))
            {
                await _userPropertyRepository.DeleteAsync(duplicate.Id, cancellationToken);
            }
        }

        // Выполняет сценарий UpsertDisplayedLevelPropertyAsync внутри владельца User.
        internal async Task UpsertDisplayedLevelPropertyAsync(
            Guid userId,
            RichDocDisplayedLevelProperty payload,
            CancellationToken cancellationToken)
        {
            var properties = (await _userPropertyRepository.GetAllAsync(
                    x => x.ParentEntityId == userId &&
                         x.PropertyType == (int)UserPropertyTypeEnum.RichDocDisplayedLevelProperty,
                    cancellationToken))
                .OrderByDescending(x => x.DateLastModified)
                .ToList();

            payload.DisplayLevelCount = NormalizeDisplayedLevel(payload.DisplayLevelCount);

            var matchingProperties = properties
                .Where(property => TryReadDisplayedLevelProperty(property.Data)?.DocumentId == payload.DocumentId)
                .ToList();

            var now = DateTime.UtcNow;
            var data = JsonSerializer.Serialize(payload, UserMiniAppJsonOptions.Default);
            var metadata = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    kind = "RichDocDisplayedLevelPropertyMetadata",
                    documentId = payload.DocumentId,
                    displayLevelCount = payload.DisplayLevelCount
                },
                UserMiniAppJsonOptions.Default);

            var property = matchingProperties.FirstOrDefault();
            if (property == null)
            {
                await _userPropertyRepository.AddAsync(
                    new UserPropertyDto
                    {
                        Id = Guid.NewGuid(),
                        DateCreated = now,
                        DateLastModified = now,
                        ParentEntityId = userId,
                        PropertyType = (int)UserPropertyTypeEnum.RichDocDisplayedLevelProperty,
                        Data = data,
                        Metadata = metadata
                    },
                    cancellationToken);
                return;
            }

            property.DateLastModified = now;
            property.Data = data;
            property.Metadata = metadata;
            await _userPropertyRepository.UpdateAsync(property, cancellationToken);

            foreach (var duplicate in matchingProperties.Skip(1))
            {
                await _userPropertyRepository.DeleteAsync(duplicate.Id, cancellationToken);
            }
        }
}

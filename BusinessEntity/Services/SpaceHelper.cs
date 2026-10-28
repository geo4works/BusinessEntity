using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Properties;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Text.RegularExpressions;

namespace BusinessEntity.Services
{
    // Инкапсулирует бизнес-операции управления пространствами.
    public class SpaceHelper
    {
        // Требует минимум 5 непробельных символов в начале, далее разрешает только буквы, цифры, "_" и пробелы.
        private static readonly Regex SpaceNameRegex = new(
            @"^[\p{L}\p{Nd}_]{5}[\p{L}\p{Nd}_ ]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly JsonSerializerOptions PropertyJsonOptions = new()
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        // Основной helper бизнес-сущностей.
        private readonly BusinessEntityHelper _businessEntityHelper;
        private readonly IDataProviderConnector _data;

        // Подключает helper бизнес-сущностей для CRUD операций с пространствами.
        public SpaceHelper(
            BusinessEntityHelper businessEntityHelper,
            IDataProviderConnector dataProvider)
        {
            _businessEntityHelper = businessEntityHelper;
            _data = dataProvider;
        }

        /// <summary>
        /// Получает одно пространство по идентификатору.
        /// </summary>
        public async Task<BusinessEntity.Core.Classes.BusinessEntity?> GetSpaceByIdAsync(Guid spaceId)
        {
            var entity = await _businessEntityHelper.GetBusinessEntityById(spaceId);
            return entity?.EntityType == BusinessEntityTypeEnum.Space ? entity : null;
        }

        /// <summary>
        /// Возвращает все пространства.
        /// </summary>
        public async Task<IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity>> GetSpacesAsync(CancellationToken ct = default)
        {
            var spaces = await _businessEntityHelper.GetSpacesAsync();
            return spaces
                .OrderBy(x => x.CreatedDate)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Возвращает общие настройки пространства.
        /// </summary>
        public async Task<GenericSpaceProperties> GetGenericSpacePropertiesAsync(Guid spaceId, CancellationToken ct = default)
        {
            var property = await GetGenericSpacePropertyAsync(spaceId, ct);
            return DeserializeGenericSpaceProperties(property);
        }

        /// <summary>
        /// Возвращает общие настройки для набора пространств.
        /// </summary>
        public async Task<IReadOnlyDictionary<Guid, GenericSpaceProperties>> GetGenericSpacePropertiesAsync(
            IEnumerable<Guid> spaceIds,
            CancellationToken ct = default)
        {
            var ids = spaceIds.ToHashSet();
            if (ids.Count == 0)
            {
                return new Dictionary<Guid, GenericSpaceProperties>();
            }

            var idList = ids.ToList();
            var propertyType = (int)BusinessEntityPropertyTypeEnum.GenericSpaceProperties;
            var properties = (await _data.GetStoredPropertiesAsync(EntityPropertyOwner.Entity, cancellationToken: ct))
                .Where(x => idList.Contains(x.ParentEntityId) && x.PropertyType == propertyType).ToArray();

            return ids.ToDictionary(
                x => x,
                x =>
                {
                    var property = properties
                        .Where(p => p.ParentEntityId == x)
                        .OrderByDescending(p => p.LastModifiedDate)
                        .ThenByDescending(p => p.CreatedDate)
                        .FirstOrDefault();
                    return DeserializeGenericSpaceProperties(property);
                });
        }

        /// <summary>
        /// Сохраняет признак резервного копирования пространства.
        /// </summary>
        public async Task<GenericSpaceProperties> SetDoBackupAsync(Guid spaceId, bool doBackup, CancellationToken ct = default)
        {
            var property = await GetGenericSpacePropertyAsync(spaceId, ct);
            var settings = DeserializeGenericSpaceProperties(property);
            settings.DoBackup = doBackup;
            return await SaveGenericSpacePropertiesAsync(spaceId, settings, ct);
        }

        /// <summary>
        /// Сохраняет общие настройки пространства.
        /// </summary>
        public async Task<GenericSpaceProperties> SaveGenericSpacePropertiesAsync(
            Guid spaceId,
            GenericSpaceProperties settings,
            CancellationToken ct = default)
        {
            await GetRequiredSpaceAsync(spaceId, ct);

            var property = await GetGenericSpacePropertyAsync(spaceId, ct);
            var normalizedSettings = NormalizeGenericSpaceProperties(settings);
            var now = DateTime.UtcNow;
            var serialized = JsonSerializer.Serialize(normalizedSettings, PropertyJsonOptions);
            await _data.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "SpacesMiniApp", null)
            {
                Properties = [new(property?.Id ?? Guid.NewGuid(), spaceId, EntityPropertyOwner.Entity,
                    (int)BusinessEntityPropertyTypeEnum.GenericSpaceProperties, serialized, nameof(GenericSpaceProperties))]
            }, ct);

            return normalizedSettings;
        }

        /// <summary>
        /// Создает новое пространство.
        /// </summary>
        public async Task<BusinessEntity.Core.Classes.BusinessEntity> CreateSpaceAsync(string name, CancellationToken ct = default)
        {
            var normalizedName = NormalizeName(name);
            return await _businessEntityHelper.CreateBusinessEntity(BusinessEntityTypeEnum.Space, normalizedName);
        }

        /// <summary>
        /// Переименовывает пространство.
        /// </summary>
        public async Task<BusinessEntity.Core.Classes.BusinessEntity?> RenameSpaceAsync(Guid spaceId, string newName, CancellationToken ct = default)
        {
            var space = await GetRequiredSpaceAsync(spaceId, ct);
            var normalizedName = NormalizeName(newName);
            return await _businessEntityHelper.RenameEntity(space.Id, normalizedName, ct);
        }

        /// <summary>
        /// Удаляет пространство вместе с его поддеревом.
        /// </summary>
        public async Task DeleteSpaceAsync(Guid spaceId, CancellationToken ct = default)
        {
            var space = await GetRequiredSpaceAsync(spaceId, ct);
            var deleteResult = await _businessEntityHelper.RemoveBusinessEntityPermanently(space.Id, ct);
            if (!deleteResult.success)
            {
                var message = deleteResult.messages.Count > 0
                    ? string.Join(Environment.NewLine, deleteResult.messages)
                    : $"Не удалось удалить пространство '{space.Name}'.";
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Нормализует и проверяет имя пространства.
        /// </summary>
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Имя пространства не может быть пустым.", nameof(name));
            }

            var normalizedName = name.Trim();
            if (!SpaceNameRegex.IsMatch(normalizedName))
            {
                throw new ArgumentException(
                    "Имя пространства должно начинаться минимум с 5 символов. Допустимы только буквы, цифры, символ '_' и пробелы.",
                    nameof(name));
            }

            return normalizedName;
        }

        /// <summary>
        /// Загружает пространство и проверяет его тип.
        /// </summary>
        private async Task<BusinessEntity.Core.Classes.BusinessEntity> GetRequiredSpaceAsync(Guid spaceId, CancellationToken ct)
        {
            var space = await GetSpaceByIdAsync(spaceId);
            if (space == null)
            {
                throw new InvalidOperationException($"Пространство с id '{spaceId}' не найдено.");
            }

            return space;
        }

        private async Task<StoredEntityProperty?> GetGenericSpacePropertyAsync(Guid spaceId, CancellationToken ct)
        {
            var propertyType = (int)BusinessEntityPropertyTypeEnum.GenericSpaceProperties;
            var properties = (await _data.GetStoredPropertiesAsync(EntityPropertyOwner.Entity, spaceId, ct))
                .Where(x => x.PropertyType == propertyType).ToArray();

            return properties
                .OrderByDescending(x => x.LastModifiedDate)
                .ThenByDescending(x => x.CreatedDate)
                .FirstOrDefault();
        }

        // Новое пространство без свойства получает настройки по умолчанию; повреждённое свойство даёт отказ.
        private static GenericSpaceProperties DeserializeGenericSpaceProperties(StoredEntityProperty? property)
        {
            if (property == null) return new GenericSpaceProperties();
            var settings = JsonSerializer.Deserialize<GenericSpaceProperties>(property.Data, PropertyJsonOptions);
            if (settings?.SchemaVersion != 1 || settings.Kind != nameof(GenericSpaceProperties))
                throw new InvalidDataException("Неизвестный формат настроек пространства.");
            return NormalizeGenericSpaceProperties(settings);
        }

        private static GenericSpaceProperties NormalizeGenericSpaceProperties(GenericSpaceProperties? settings)
        {
            return new GenericSpaceProperties
            {
                SchemaVersion = settings?.SchemaVersion > 0 ? settings.SchemaVersion : 1,
                Kind = string.IsNullOrWhiteSpace(settings?.Kind)
                    ? nameof(GenericSpaceProperties)
                    : settings.Kind,
                DoBackup = settings?.DoBackup ?? true,
                BackupFolder = settings?.BackupFolder?.Trim() ?? string.Empty,
                BackupIntervalMinutes = settings?.BackupIntervalMinutes > 0
                    ? settings.BackupIntervalMinutes
                    : 5
            };
        }

    }
}

using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Navigation;
using BusinessEntity.Core.Saving;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Types;
using BusinessEntity.Core.Chunks;
using System.Linq;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.WebLogger.Services;
using BusinessEntity.Core.BaseClasses.Relations;

// Основной сервис работы с сущностями, data и связями
namespace BusinessEntity.Core.Services
{
    // Содержит прикладные операции поверх DataProvider
    public class BusinessEntityHelper
    {
        // Коннектор хранения entity, data и relations
        private readonly IDataProviderConnector _dataProviderConnector;
        private readonly EntitySaveBatchFactory _batches;
        private readonly EntityTypeRegistry _types;
        // Логгер бизнес-операций
        private readonly IWebLoggerService? _webLogger;
        // Фабрика для создания entity и typed entity
        private readonly IBusinessEntityFactory _businessEntityFactory;

        // Подключает зависимости для работы с бизнес-сущностями
        public BusinessEntityHelper(
            IDataProviderConnector dataProviderConnector,
            IWebLoggerService? webLogger,
            IBusinessEntityFactory businessEntityFactory, EntitySaveBatchFactory batches, EntityTypeRegistry types)
        {
            _batches = batches; _types = types;
            _dataProviderConnector = dataProviderConnector ?? throw new ArgumentNullException(nameof(dataProviderConnector));
            _webLogger = webLogger; // Логгер может быть не настроен, поэтому допускаем null
            _businessEntityFactory = businessEntityFactory ?? throw new ArgumentNullException(nameof(businessEntityFactory));
        }

        // Создает простую сущность и сохраняет ее в хранилище
        public async Task<Classes.BusinessEntity> CreateBusinessEntity(BusinessEntityTypeEnum type, string name)
        {
            var entity = _businessEntityFactory.Create(type, name);
            var initial = _types.Get(type).CreateInitial(entity.Id, name, null);
            var payloads = initial.Data == null ? Array.Empty<EntityPayloadChange>()
                : new[] { new EntityPayloadChange(entity.Id, 0, initial.Data, initial.Chunks, initial.ChunkOrder) };
            await _dataProviderConnector.SaveAsync(_batches.Create(type) with { Entities = [entity], Payloads = payloads });
            return entity;
        }

        // Удаляет сущность вместе со всеми ее связями
        public async Task RemoveBusinessEntity(Guid id)
        {
            var entity = await _dataProviderConnector.GetByIdAsync(id);
            if (entity == null) return;
            await _dataProviderConnector.SaveAsync(_batches.Create(entity.EntityType) with { DeleteEntities = [id] });
        }

        // Создает relation между двумя сущностями
        public async Task<BusinessEntityRelation> CreateRelation(IBusinessEntity entityA, IBusinessEntity entityB,
            MacroRelationType macroRelationType, string parameters = "")
        {
            ArgumentNullException.ThrowIfNull(entityA); ArgumentNullException.ThrowIfNull(entityB);
            ArgumentNullException.ThrowIfNull(macroRelationType);
            var relation = new BusinessEntityRelation { ObjectAId = entityA.Id, ObjectBId = entityB.Id,
                RelationType = macroRelationType.RelationType.ToString(), RelationParams = parameters };
            await _dataProviderConnector.SaveAsync(_batches.Create(entityB.EntityType) with { Relations = [relation] });
            return relation;
        }

        // Удаляет все связи выбранного типа между двумя сущностями
        public async Task<int> RemoveRelation(IBusinessEntity entityA, IBusinessEntity entityB, MacroRelationType macroRelationType)
        {
            var ids = (await _dataProviderConnector.GetRelationsAsync(entityA.Id, entityB.Id))
                .Where(r => r.RelationType == macroRelationType.RelationType.ToString()).Select(r => r.Id).ToArray();
            await _dataProviderConnector.SaveAsync(_batches.Create(entityB.EntityType) with { DeleteRelations = ids });
            return ids.Length;
        }

        // Возвращает entity по идентификатору
        public async Task<Classes.BusinessEntity?> GetBusinessEntityById(Guid id)
        {
            _webLogger?.Debug($"GetBusinessEntityById: id={id}");
            return await _dataProviderConnector.GetByIdAsync(id);
        }

        // Возвращает relation по идентификатору
        public async Task<BusinessEntityRelation?> GetRelationById(Guid id)
        {
            return await _dataProviderConnector.GetRelationByIdAsync(id);
        }

        // Возвращает все entity с необязательным ограничением по количеству
        public async Task<IReadOnlyList<Classes.BusinessEntity>> GetAllBusinessEntities(int? take = null)
        {
            var entities = await _dataProviderConnector.GetAllAsync();
            return take.HasValue ? entities.Take(take.Value).ToList() : entities;
        }

        // Возвращает карточки объявленного типа без знания его предметного значения.
        public async Task<IReadOnlyList<Classes.BusinessEntity>> GetEntitiesOfTypeAsync(BusinessEntityTypeEnum type, int? take = null)
        {
            _types.Get(type);
            var entities = (await _dataProviderConnector.GetAllAsync()).Where(entity => entity.EntityType == type);
            return (take.HasValue ? entities.Take(take.Value) : entities).ToArray();
        }

        // Возвращает все relation с необязательным ограничением по количеству
        public async Task<IReadOnlyList<BusinessEntityRelation>> GetAllRelations(int? take = null)
        {
            var relations = await _dataProviderConnector.GetAllRelationsAsync();
            return take.HasValue ? relations.Take(take.Value).ToList() : relations;
        }

        // Возвращает все relation, где участвует указанная entity
        public async Task<IReadOnlyList<BusinessEntityRelation>> GetRelationsByEntityIdAsync(Guid entityId, CancellationToken ct = default)
        {
            return (await _dataProviderConnector.GetAllRelationsAsync(ct))
                .Where(r => r.ObjectAId == entityId || r.ObjectBId == entityId)
                .ToList();
        }

        // Возвращает дочерние entity по связи Contains
        public async Task<IEnumerable<Classes.BusinessEntity>> GetContainedEntitiesAsync(Guid parentId, CancellationToken ct = default)
        {
            var projection = await _dataProviderConnector.QueryGraphAsync(GraphQueryKind.Children, parentId, cancellationToken: ct);
            return projection.Entities.OrderBy(entity => entity.CreatedDate);
        }

        // Возвращает корневые узлы согласованного снимка.
        public async Task<IEnumerable<Classes.BusinessEntity>> GetRootEntitiesAsync()
        {
            var projection = await _dataProviderConnector.QueryGraphAsync(GraphQueryKind.Roots);
            return projection.Entities.OrderBy(entity => entity.CreatedDate);
        }

        // Читает поддерево единым запросом, без SQL на каждого потомка.
        public Task<GraphProjection> GetSubgraphAsync(Guid rootId, CancellationToken ct = default)
            => _dataProviderConnector.QueryGraphAsync(GraphQueryKind.Subgraph, rootId, cancellationToken: ct);

        // Возвращает путь от корня до узла на одной ревизии графа.
        public async Task<IReadOnlyList<Classes.BusinessEntity>> GetPathToRootAsync(Guid entityId, CancellationToken ct = default)
            => (await _dataProviderConnector.QueryGraphAsync(GraphQueryKind.PathToRoot, entityId, cancellationToken: ct)).Entities;

        // Ищет имена в выбранном поддереве или всём графе без загрузки payload.
        public async Task<IReadOnlyList<Classes.BusinessEntity>> SearchByNameAsync(string query, Guid? rootId = null,
            int take = 100, CancellationToken ct = default)
            => (await _dataProviderConnector.QueryGraphAsync(GraphQueryKind.SearchName, rootId, query, take, ct)).Entities;

        // Генерирует уникальное имя нового дочернего элемента
        private async Task<string> GetNewItemNameAsync(
            Classes.BusinessEntity parent,
            BusinessEntityTypeEnum newType,
            CancellationToken ct = default)
        {
            // Формируем базовое имя по типу
            var baseName = _types.Get(newType).DefaultNamePrefix;
            var children = await GetContainedEntitiesAsync(parent.Id, ct);
            var sameTypeChildren = children.Where(c => c.EntityType == newType)
                                          .Select(c => c.Name)
                                          .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Ищем свободный суффикс
            for (var i = 1; ; i++)
            {
                var candidateName = $"{baseName}{i}";
                if (!sameTypeChildren.Contains(candidateName))
                    return candidateName;
            }
        }

        // Создаёт связанный дочерний объект по правилам и initial payload владельца типа.
        public async Task<Classes.BusinessEntity> CreateChildAsync(Classes.BusinessEntity parent, BusinessEntityTypeEnum type,
            string? initialContent = null, CancellationToken ct = default)
        {
            var descriptor = _types.Get(type);
            if (!descriptor.AllowedParentTypes.Contains(parent.EntityType)) throw new ArgumentException("Тип нельзя вложить в выбранного родителя.");
            var name = await GetNewItemNameAsync(parent, type, ct); var entity = _businessEntityFactory.Create(type, name);
            var initial = descriptor.CreateInitial(entity.Id, name, initialContent);
            var payloads = initial.Data == null ? Array.Empty<EntityPayloadChange>()
                : new[] { new EntityPayloadChange(entity.Id, 0, initial.Data, initial.Chunks, initial.ChunkOrder) };
            var relation = new BusinessEntityRelation { ObjectAId = parent.Id, ObjectBId = entity.Id, RelationType = "Contains" };
            await _dataProviderConnector.SaveAsync(_batches.Create(type) with { Entities = [entity], Payloads = payloads, Relations = [relation] }, ct);
            return entity;
        }

        // Меняет родителя элемента в дереве по связи Contains
        public async Task ChangeVisualFolderParentForItem(Classes.BusinessEntity child, Classes.BusinessEntity newVisualParent)
        {
            ArgumentNullException.ThrowIfNull(child); ArgumentNullException.ThrowIfNull(newVisualParent);
            if (await WouldCreateCyclicDependency(child.Id, newVisualParent.Id))
                throw new InvalidOperationException("Перемещение создаёт цикл.");
            var old = (await _dataProviderConnector.GetAllRelationsAsync()).Where(r => r.ObjectBId == child.Id
                && r.RelationType == BusinessEntityRelationTypeEnum.Contains.ToString()).Select(r => r.Id).ToArray();
            var relation = new BusinessEntityRelation { ObjectAId = newVisualParent.Id, ObjectBId = child.Id,
                RelationType = BusinessEntityRelationTypeEnum.Contains.ToString() };
            await _dataProviderConnector.SaveAsync(_batches.Create(child.EntityType) with { DeleteRelations = old, Relations = [relation] });
        }

        // Перемещение всего набора готовит связи заранее и отправляет одну транзакцию от владельца сценария.
        public async Task MoveEntitiesAsync(IReadOnlyList<Guid> ids, Guid parentId, string miniAppId, CancellationToken ct)
        {
            var all = await _dataProviderConnector.QueryGraphAsync(GraphQueryKind.All, cancellationToken: ct);
            var parent = all.Entities.Single(x => x.Id == parentId);
            var children = ids.Distinct().Select(id => all.Entities.Single(x => x.Id == id)).ToArray();
            foreach (var child in children)
            {
                if (!_types.Get(child.EntityType).AllowedParentTypes.Contains(parent.EntityType)) throw new ArgumentException("Недопустимое вложение.");
                if (await WouldCreateCyclicDependency(child.Id, parentId)) throw new InvalidOperationException("Перемещение создаёт цикл.");
            }
            var selected = children.Select(x => x.Id).ToHashSet();
            var removed = all.Relations.Where(x => selected.Contains(x.ObjectBId) && x.RelationType == "Contains").Select(x => x.Id).ToArray();
            var added = children.Select(child => new BusinessEntityRelation { ObjectAId = parentId, ObjectBId = child.Id, RelationType = "Contains" }).ToArray();
            await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), miniAppId, null) { DeleteRelations = removed, Relations = added }, ct);
        }
        // Удаляет весь выбранный набор одной транзакцией, включая зависимые поддеревья на стороне DataProvider.
        public Task RemoveEntitiesAsync(IReadOnlyList<Guid> ids, string miniAppId, CancellationToken ct) =>
            _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), miniAppId, null) { DeleteEntities = ids.Distinct().ToArray() }, ct);

        // Полностью удаляет сущность и ее дочернее поддерево
        public async Task<(bool success, List<string> messages)> RemoveBusinessEntityPermanently(Guid entityId, CancellationToken ct = default)
        {
            var entity = await _dataProviderConnector.GetByIdAsync(entityId, ct);
            if (entity == null) return (true, []);
            await _dataProviderConnector.SaveAsync(_batches.Create(entity.EntityType) with { DeleteEntities = [entityId] }, ct);
            return (true, []);
        }
        
        // Рекурсивно удаляет всех потомков сущности по связи Contains

        // Переименовывает сущность и сохраняет изменение
        public async Task<Classes.BusinessEntity?> RenameEntity(Guid entityId, string newName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("Название обязательно.", nameof(newName));
            var entity = await _dataProviderConnector.GetByIdAsync(entityId, ct);
            if (entity == null) return null;
            var data = await _dataProviderConnector.GetDataAsync<BusinessEntityData>(entityId, ct);
            entity.Name = newName.Trim();
            var payloads = Array.Empty<EntityPayloadChange>();
            if (data != null) { data.Name = entity.Name; payloads = [new(entityId, data.Version, data)]; }
            await _dataProviderConnector.SaveAsync(_batches.Create(entity.EntityType) with { Entities = [entity], Payloads = payloads }, ct);
            return entity;
        }

        // Проверяет, создаст ли перенос циклическую зависимость
        public async Task<bool> WouldCreateCyclicDependency(Guid childId, Guid newParentId)
        {
            // Если пытаемся переместить элемент в самого себя
            if (childId == newParentId)
                return true;

            // Проверяем, является ли newParent потомком child
            return await IsDescendantInDatabase(newParentId, childId);
        }

        // Проверяет принадлежность предку по согласованному пути, без рекурсивных storage-вызовов.
        private async Task<bool> IsDescendantInDatabase(Guid descendantId, Guid ancestorId)
            => (await GetPathToRootAsync(descendantId)).Any(entity => entity.Id == ancestorId);

        // Загружает payload-объекты для сущности
        public async Task<IReadOnlyList<BusinessEntityData>> GetData(Classes.BusinessEntity entityData)
        {
            if (entityData == null) throw new ArgumentNullException(nameof(entityData));
            _webLogger?.Debug($"GetData: entityId={entityData.Id}, type={entityData.EntityType}");

            var descriptor = _types.Get(entityData.EntityType);
            var data = await _dataProviderConnector.GetDataAsync<BusinessEntityData>(entityData.Id);
            if (data == null)
            {
                if (descriptor.PayloadRequired) throw new InvalidDataException("У обязательного payload отсутствует сохранённая версия.");
                return [];
            }
            if (!descriptor.DataType.IsInstanceOfType(data)) throw new InvalidDataException("Payload не соответствует описателю типа.");
            ApplyEntityMetadata(entityData, data); return [data];
        }

        // Загружает typed-сущность вместе с ее payload-объектом
        public async Task<Classes.BusinessEntity<TData>?> GetEntityWithDataAsync<TData>(Guid entityId, CancellationToken ct = default)
            where TData : class, IBusinessEntityData, new()
        {
            var entity = await _dataProviderConnector.GetByIdAsync(entityId, cancellationToken: ct);
            if (entity == null)
            {
                return null;
            }

            var data = await _dataProviderConnector.GetDataAsync<TData>(entity.Id, ct);
            if (data == null)
            {
                var descriptor = _types.Get(entity.EntityType);
                if (descriptor.PayloadRequired) throw new InvalidDataException("Обязательный payload отсутствует.");
                data = new TData(); // Отсутствие payload разрешено контрактом контейнерного типа.
            }
            var typedEntity = _businessEntityFactory.Create(entity.EntityType, data, entity.Name);
            typedEntity = CopyEntityState(entity, typedEntity);

            if (string.IsNullOrWhiteSpace(typedEntity.Data.Name))
            {
                typedEntity.Data.Name = typedEntity.Name;
            }

            if (typedEntity.Data.EntityType == BusinessEntityTypeEnum.Undefined)
            {
                typedEntity.Data.EntityType = typedEntity.EntityType;
            }

            if (string.IsNullOrWhiteSpace(typedEntity.Data.Tag))
            {
                typedEntity.Data.Tag = _types.Get(typedEntity.EntityType).TypeKey;
            }

            return typedEntity;
        }

        // Читает обязательный singleton; создавать начальное состояние вправе startup владельца.
        public async Task<Classes.BusinessEntity<TData>> GetRequiredSingletonEntityAsync<TData>(BusinessEntityTypeEnum type, string name, CancellationToken ct = default)
            where TData : class, IBusinessEntityData, new()
        {
            var entity = await FindSingletonEntityAsync(type, name, ct) ?? throw new InvalidDataException("Обязательный singleton отсутствует.");
            return await GetEntityWithDataAsync<TData>(entity.Id, ct) ?? throw new InvalidDataException("Обязательный singleton удалён во время чтения.");
        }
        // Startup создаёт отсутствующий singleton из описателя владельца; дубликаты считаются ошибкой.
        public async Task<Classes.BusinessEntity<TData>> GetOrCreateSingletonEntityAsync<TData>(BusinessEntityTypeEnum type, string name, CancellationToken ct = default)
            where TData : class, IBusinessEntityData, new()
        {
            var existing = await FindSingletonEntityAsync(type, name, ct);
            if (existing != null) return await GetEntityWithDataAsync<TData>(existing.Id, ct)
                ?? throw new InvalidDataException("Singleton удалён во время инициализации.");
            var created = _businessEntityFactory.Create<TData>(type, name);
            created.Data.Name = name; created.Data.Tag = _types.Get(type).TypeKey;
            return await SaveEntityAsync(created, ct);
        }
        // Поиск обобщённый: singleton и тип данных объявляет владелец, Core не перечисляет домены.
        private async Task<Classes.BusinessEntity?> FindSingletonEntityAsync(BusinessEntityTypeEnum type, string name, CancellationToken ct)
        {
            if (!_types.Get(type).IsSingleton) throw new ArgumentException("Тип не объявлен singleton.");
            var graph = await _dataProviderConnector.QueryGraphAsync(GraphQueryKind.All, cancellationToken: ct);
            return graph.Entities.Where(x => x.EntityType == type && string.Equals(x.Name, name, StringComparison.Ordinal)).SingleOrDefault();
        }

        // Сохраняет typed business-объект как entity + payload без relation
        public async Task<Classes.BusinessEntity<TData>> SaveEntityAsync<TData>(
            Classes.BusinessEntity<TData> entity,
            CancellationToken ct = default)
            where TData : class, IBusinessEntityData
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (entity.Data == null) throw new ArgumentNullException(nameof(entity.Data));

            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }

            if (entity.CreatedDate == default)
            {
                entity.CreatedDate = DateTime.UtcNow;
            }

            entity.LastModifiedDate = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(entity.Data.Name))
            {
                entity.Data.Name = entity.Name;
            }

            if (entity.Data.EntityType == BusinessEntityTypeEnum.Undefined)
            {
                entity.Data.EntityType = entity.EntityType;
            }

            if (string.IsNullOrWhiteSpace(entity.Data.Tag))
            {
                entity.Data.Tag = _types.Get(entity.EntityType).TypeKey;
            }

            entity.SynchronizeDataWithEntity();

            var persistenceEntity = CopyEntityState(entity, _businessEntityFactory.Create(entity.EntityType, entity.Name));
            var sent = _types.Get(entity.EntityType).Clone(entity.Data as BusinessEntityData ?? throw new ArgumentException("Payload не реализует общую модель данных."));
            var exists = await _dataProviderConnector.GetDataAsync<IBusinessEntityData>(entity.Id, ct);
            var saved = await _dataProviderConnector.SaveAsync(_batches.Create(entity.EntityType) with
                { Entities = [persistenceEntity], Payloads = [new(entity.Id, exists == null ? 0 : sent.Version, sent)] }, ct);
            entity.Data.Version = saved.Versions.Single().Version;
            return entity;
        }

        // Сохраняет entity и ее payload в хранилище
        public async Task SaveEntity(Classes.BusinessEntity entityData, BusinessEntityData data)
        {
            ArgumentNullException.ThrowIfNull(entityData); ArgumentNullException.ThrowIfNull(data);
            var sent = _types.Get(entityData.EntityType).Clone(data);
            sent.Id = entityData.Id; sent.Name = entityData.Name; sent.EntityType = entityData.EntityType;
            var entityToSave = CreatePersistenceEntity(entityData, data);
            var exists = await _dataProviderConnector.GetDataAsync<IBusinessEntityData>(entityData.Id);
            var result = await _dataProviderConnector.SaveAsync(_batches.Create(entityData.EntityType) with
                { Entities = [entityToSave], Payloads = [new(entityData.Id, exists == null ? 0 : sent.Version, sent)] });
            data.Version = result.Versions.Single().Version;
        }

        // Карточка копируется общим алгоритмом; предметный payload фиксируется через описатель владельца.
        private Classes.BusinessEntity CreatePersistenceEntity(Classes.BusinessEntity entityData, BusinessEntityData data)
        {
            ResolveEntityType(entityData, data);
            return CopyEntityState(entityData, _businessEntityFactory.Create(entityData.EntityType, entityData.Name));
        }

        // Копирует метаданные существующей entity в новую runtime-entity
        private static Classes.BusinessEntity CopyEntityState(Classes.BusinessEntity source, Classes.BusinessEntity target)
        {
            target.Id = source.Id;
            target.CreatedDate = source.CreatedDate;
            target.LastModifiedDate = source.LastModifiedDate;
            target.CreatedByUserId = source.CreatedByUserId;
            target.LastModifiedByUserId = source.LastModifiedByUserId;
            target.IsPublic = source.IsPublic;
            target.Name = source.Name;

            target.EntityType = source.EntityType;

            return target;
        }

        // Копирует метаданные существующей entity в typed runtime-entity
        private static Classes.BusinessEntity<TData> CopyEntityState<TData>(Classes.BusinessEntity source, Classes.BusinessEntity<TData> target)
            where TData : class, IBusinessEntityData
        {
            target.Id = source.Id;
            target.CreatedDate = source.CreatedDate;
            target.LastModifiedDate = source.LastModifiedDate;
            target.CreatedByUserId = source.CreatedByUserId;
            target.LastModifiedByUserId = source.LastModifiedByUserId;
            target.IsPublic = source.IsPublic;
            target.Name = source.Name;

            target.EntityType = source.EntityType;
            target.SynchronizeDataWithEntity();

            return target;
        }

        // Выбирает итоговый тип entity для сохранения
        private static BusinessEntityTypeEnum ResolveEntityType(Classes.BusinessEntity entityData, BusinessEntityData data)
        {
            if (entityData.EntityType == BusinessEntityTypeEnum.Undefined || data.EntityType != entityData.EntityType)
                throw new ArgumentException("Тип карточки и payload должен быть задан явно и совпадать.");
            return entityData.EntityType;
        }

        // Накладывает общие entity-метаданные поверх typed payload, полученного из data-provider.
        private void ApplyEntityMetadata(Classes.BusinessEntity entityData, BusinessEntityData data)
        {
            data.Id = entityData.Id;
            data.Name = entityData.Name;
            data.CreatedDate = entityData.CreatedDate;
            data.LastModifiedDate = entityData.LastModifiedDate;
            data.EntityType = entityData.EntityType;

            if (string.IsNullOrWhiteSpace(data.Tag))
            {
                data.Tag = _types.Get(entityData.EntityType).TypeKey;
            }
        }
    }
}

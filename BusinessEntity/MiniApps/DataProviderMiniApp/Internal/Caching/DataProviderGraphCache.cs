using System.Collections.Frozen;
using System.Data;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Navigation;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Caching;

// Общий structural snapshot публикуется целиком после commit; runtime-объекты выдаются только копиями.
internal sealed class DataProviderGraphCache
{
    private readonly DbContextOptions<KmsBusinessEntityDbContext> _options;
    private readonly StorageCommandMetrics _storage;
    private readonly BoundedPayloadCache _payloads;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private GraphSnapshot? _current;
    private long _hits;
    // Кэш не содержит пользовательских прав и тяжёлых тел чанков.
    public DataProviderGraphCache(DbContextOptions<KmsBusinessEntityDbContext> options, StorageCommandMetrics storage, BoundedPayloadCache payloads)
    { _options = options; _storage = storage; _payloads = payloads; }

    // Startup и первый запрос используют одну инициализацию, которую писатель дожидается до своих изменений.
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _current) != null) return;
        await _initialization.WaitAsync(ct);
        try
        {
            if (_current != null) return;
            await using var db = new KmsBusinessEntityDbContext(_options);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var snapshot = await LoadAsync(db, null, new HashSet<Guid>(), ct);
            await tx.CommitAsync(ct);
            Volatile.Write(ref _current, snapshot);
        }
        finally { _initialization.Release(); }
    }
    // Предварительно строит полный кандидат из своей ещё не опубликованной SQL-транзакции.
    public Task<GraphSnapshot> PrepareAsync(KmsBusinessEntityDbContext db, IReadOnlySet<Guid> changed, CancellationToken ct)
        => LoadAsync(db, Current, changed, ct);

    // Ни одного SQL/IO после commit для публикации снимка; события следуют за этой границей.
    public void Publish(GraphSnapshot snapshot, IReadOnlySet<Guid> changed)
    {
        Volatile.Write(ref _current, snapshot);
        _payloads.Invalidate(changed);
    }
    // Снимок immutable и доступен только внутри DataProvider.
    private GraphSnapshot Current => Volatile.Read(ref _current) ?? throw new InvalidOperationException("Кэш графа ещё не инициализирован.");
    // Один read-запрос удерживает одну ревизию даже при параллельном commit.
    public GraphSnapshot Read()
    { Interlocked.Increment(ref _hits); return Current; }

    // Структурные проекции работают только с памятью; права применяются владельцем вызывающего сценария.
    public GraphProjection Query(GraphQueryKind kind, Guid? rootId, string? searchText, int take)
    {
        if (take < 1 || take > 1000) throw new ArgumentOutOfRangeException(nameof(take));
        var snapshot = Read();
        IEnumerable<Guid> ids;
        switch (kind)
        {
            case GraphQueryKind.All:
                ids = snapshot.Entities.Keys;
                break;
            case GraphQueryKind.Roots:
                ids = snapshot.Entities.Keys.Where(id => !snapshot.Parents.ContainsKey(id));
                break;
            case GraphQueryKind.Children:
                ids = snapshot.Children.GetValueOrDefault(rootId ?? throw new ArgumentException("Не задан родитель.")) ?? [];
                break;
            case GraphQueryKind.Subgraph:
                ids = Descendants(snapshot, rootId ?? throw new ArgumentException("Не задан корень."));
                break;
            case GraphQueryKind.PathToRoot:
                var path = new List<Guid>(); var current = rootId ?? throw new ArgumentException("Не задан узел.");
                if (snapshot.Entities.ContainsKey(current))
                {
                    path.Add(current); var seen = new HashSet<Guid> { current };
                    while (snapshot.Parents.TryGetValue(current, out current))
                    {
                        if (!seen.Add(current)) throw new InvalidDataException("Цикл в структурном графе.");
                        path.Add(current);
                    }
                    path.Reverse();
                }
                ids = path;
                break;
            case GraphQueryKind.SearchName:
                if (string.IsNullOrWhiteSpace(searchText)) throw new ArgumentException("Поисковая строка обязательна.");
                var scope = rootId.HasValue ? Descendants(snapshot, rootId.Value).ToHashSet() : null;
                ids = snapshot.Entities.Values.Where(x => (scope == null || scope.Contains(x.Id))
                    && x.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).Take(take).Select(x => x.Id);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        var selected = ids.ToArray(); var set = selected.ToHashSet();
        return new(snapshot.Revision, selected.Select(id => snapshot.Entities[id].Copy()).ToArray(),
            snapshot.Relations.Values.Where(x => set.Contains(x.ObjectAId) && set.Contains(x.ObjectBId)).Select(x => x.Copy()).ToArray(),
            selected.Where(snapshot.PublishedNames.ContainsKey).ToDictionary(id => id, id => snapshot.PublishedNames[id]));
    }
    // Названия опубликованных снимков являются лёгкой metadata-проекцией, не загрузкой payload.
    public IReadOnlyDictionary<Guid, string> PublishedNames(IReadOnlyList<Guid> ids)
    {
        var snapshot = Read();
        return ids.Distinct().Where(snapshot.PublishedNames.ContainsKey).ToDictionary(id => id, id => snapshot.PublishedNames[id]);
    }
    // Счётчики реальных SQL отдельно от cache hits и размера удерживаемого payload.
    public DataProviderCacheStatistics Statistics()
    {
        var snapshot = Current; var payloads = _payloads.Statistics();
        return new(snapshot.Revision, snapshot.Entities.Count, snapshot.Relations.Count, snapshot.EstimatedBytes, Interlocked.Read(ref _hits),
            payloads.Hits, payloads.Misses, payloads.Evictions, payloads.Entries, payloads.Bytes,
            _payloads.LimitBytes, _payloads.LimitEntries, _storage.Reads, _storage.Writes);
    }
    // Итеративный обход исключает рекурсию на глубоком дереве; Contains валидируется до коммита.
    private static IReadOnlyList<Guid> Descendants(GraphSnapshot snapshot, Guid root)
    {
        if (!snapshot.Entities.ContainsKey(root)) return [];
        var selected = new HashSet<Guid> { root }; var queue = new Queue<Guid>([root]); var result = new List<Guid>();
        while (queue.TryDequeue(out var current))
        {
            result.Add(current);
            foreach (var child in snapshot.Children.GetValueOrDefault(current) ?? []) if (selected.Add(child)) queue.Enqueue(child);
        }
        return result;
    }
    // SQL читает карточки, связи и лёгкие номера/названия; тела payload и чанков сюда не попадают.
    private static async Task<GraphSnapshot> LoadAsync(KmsBusinessEntityDbContext db, GraphSnapshot? previous,
        IReadOnlySet<Guid> changed, CancellationToken ct)
    {
        var entities = (await db.BusinessEntities.AsNoTracking().ToListAsync(ct)).Select(GraphEntity.From).ToFrozenDictionary(x => x.Id);
        var relations = (await db.BusinessEntityRelations.AsNoTracking().ToListAsync(ct)).Select(GraphRelation.From).ToFrozenDictionary(x => x.Id);
        var latest = await db.BusinessEntityDataItems.AsNoTracking().GroupBy(x => x.BusinessEntityId)
            .Select(group => new { Id = group.Key, Version = group.Max(x => x.Version) }).ToDictionaryAsync(x => x.Id, x => x.Version, ct);
        var published = await (from entity in db.BusinessEntities.AsNoTracking()
            join data in db.BusinessEntityDataItems.AsNoTracking()
                on new { Id = entity.Id, Version = entity.PublishedVersion } equals new { Id = data.BusinessEntityId, data.Version }
            where entity.PublishedVersion > 0
            select new { entity.Id, data.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        if (entities.Values.Any(x => x.PublishedVersion > 0 && !published.ContainsKey(x.Id)))
            throw new InvalidDataException("Указатель публикации ссылается на отсутствующую версию.");
        var contains = relations.Values.Where(x => x.RelationType == "Contains").ToArray();
        var children = contains.GroupBy(x => x.ObjectAId).ToFrozenDictionary(group => group.Key,
            group => group.Select(x => x.ObjectBId).OrderBy(id => entities[id].CreatedDate).ThenBy(id => id).ToArray());
        var parents = contains.ToFrozenDictionary(x => x.ObjectBId, x => x.ObjectAId);
        var generations = entities.Keys.ToFrozenDictionary(id => id,
            id => previous == null ? 1 : changed.Contains(id) || !previous.Generations.ContainsKey(id)
                ? checked(previous.Revision + 1) : previous.Generations[id]);
        var bytes = entities.Values.Sum(x => 224L + x.Name.Length * 2L)
            + relations.Values.Sum(x => 160L + (x.RelationType.Length + x.RelationParams.Length) * 2L)
            + published.Values.Sum(x => 64L + x.Length * 2L) + latest.Count * 48L;
        return new((previous?.Revision ?? 0) + 1, entities, relations, latest.ToFrozenDictionary(), published.ToFrozenDictionary(), children, parents, generations, bytes);
    }
}
// Immutable технические карточки не выдаются за пределы storage-владельца.
internal sealed record GraphSnapshot(long Revision, FrozenDictionary<Guid, GraphEntity> Entities,
    FrozenDictionary<Guid, GraphRelation> Relations, FrozenDictionary<Guid, int> LatestVersions,
    FrozenDictionary<Guid, string> PublishedNames, FrozenDictionary<Guid, Guid[]> Children,
    FrozenDictionary<Guid, Guid> Parents, FrozenDictionary<Guid, long> Generations, long EstimatedBytes);
// Собственная карточка кэша исключает изменение общих объектов клиентом.
internal sealed record GraphEntity(Guid Id, string Name, BusinessEntityTypeEnum EntityType, DateTime CreatedDate,
    DateTime LastModifiedDate, Guid? CreatedByUserId, Guid? LastModifiedByUserId, bool IsPublic, int PublishedVersion)
{
    // Копирует primitive metadata из EF без удержания DbContext или navigation properties.
    public static GraphEntity From(BusinessEntityDto row) => new(row.Id, row.Name, row.EntityType, row.CreatedDate,
        row.LastModifiedDate, row.CreatedByUserId, row.LastModifiedByUserId, row.IsPublic, row.PublishedVersion);
    // Возвращает вызывающему коду его отдельную runtime-карточку.
    public Core.Classes.BusinessEntity Copy() => new() { Id = Id, Name = Name, EntityType = EntityType,
        CreatedDate = CreatedDate, LastModifiedDate = LastModifiedDate, CreatedByUserId = CreatedByUserId,
        LastModifiedByUserId = LastModifiedByUserId, IsPublic = IsPublic, PublishedVersion = PublishedVersion };
}
// Рёбра кэша также не удерживают изменяемые модели внешних потребителей.
internal sealed record GraphRelation(Guid Id, Guid ObjectAId, Guid ObjectBId, string RelationType, string RelationParams,
    DateTime CreatedDate, DateTime LastModifiedDate)
{
    // Создаёт immutable ребро из собственной EF-модели.
    public static GraphRelation From(BusinessEntityRelationDto row) => new(row.Id, row.ObjectAId, row.ObjectBId,
        row.RelationType, row.RelationParams, row.CreatedDate, row.LastModifiedDate);
    // Возвращает независимую копию ребра для read-контракта.
    public BusinessEntityRelation Copy() => new() { Id = Id, ObjectAId = ObjectAId, ObjectBId = ObjectBId,
        RelationType = RelationType, RelationParams = RelationParams, CreatedDate = CreatedDate, LastModifiedDate = LastModifiedDate };
}

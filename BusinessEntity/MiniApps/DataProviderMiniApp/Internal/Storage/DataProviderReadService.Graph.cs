using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Navigation;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Caching;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

// Структурное чтение обслуживается одной ревизией RAM-графа, тяжёлый payload удерживается ограниченно.
internal sealed partial class DataProviderReadService
{
    // Возвращает независимые карточки без SQL после startup.
    public async Task<IReadOnlyList<Core.Classes.BusinessEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await _graph.InitializeAsync(ct);
        return _graph.Read().Entities.Values.Select(x => x.Copy()).ToArray();
    }
    // Возвращает отдельную копию карточки по ID.
    public async Task<Core.Classes.BusinessEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await _graph.InitializeAsync(ct);
        return _graph.Read().Entities.TryGetValue(id, out var card) ? card.Copy() : null;
    }
    // Поиск, путь и подграф используют только структурную память и одну ревизию.
    public async Task<GraphProjection> QueryGraphAsync(GraphQueryKind kind, Guid? rootId, string? query, int take, CancellationToken ct = default)
    {
        await _graph.InitializeAsync(ct);
        return _graph.Query(kind, rootId, query, take);
    }
    // Возвращает лёгкие имена опубликованных версий без чтения содержимого.
    public async Task<IReadOnlyDictionary<Guid, string>> GetPublishedNamesAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        await _graph.InitializeAsync(ct);
        return _graph.PublishedNames(ids);
    }
    // Выдаёт статистику кэша и настоящих SQL-команд DataProvider.
    public async Task<DataProviderCacheStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        await _graph.InitializeAsync(ct);
        return _graph.Statistics();
    }
    // Материализует собственный runtime-объект из ограниченного raw cache либо конкретной SQL-строки.
    public async Task<IBusinessEntityData?> GetPayloadAsync(Guid id, int? version, CancellationToken ct = default)
    {
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        await _graph.InitializeAsync(ct);
        var snapshot = _graph.Read();
        if (!snapshot.Entities.TryGetValue(id, out var entity)) return null;
        var requested = version ?? snapshot.LatestVersions.GetValueOrDefault(id);
        if (requested == 0) return null;
        var key = new PayloadCacheKey(id, requested, snapshot.Generations[id]);
        var stored = _payloads.Get(key);
        if (stored == null)
        {
            await using var db = new KmsBusinessEntityDbContext(_options);
            var row = await db.BusinessEntityDataItems.AsNoTracking().SingleOrDefaultAsync(x => x.BusinessEntityId == id && x.Version == requested, ct);
            if (row == null) return null;
            if (row.Version < 1 || string.IsNullOrWhiteSpace(row.Data)) throw new InvalidDataException("Повреждён payload.");
            stored = new(row.Data, row.Name, row.Version, row.CreatedDate, row.LastModifiedDate);
            _payloads.Put(key, stored);
        }
        var data = _codec.DeserializeEnvelope<IBusinessEntityData>(stored.Json);
        if (data.EntityType != entity.EntityType) throw new InvalidDataException("Payload не соответствует типу владельца.");
        data.Version = stored.Version; data.Name = stored.Name; data.Id = id;
        data.CreatedDate = stored.CreatedDate; data.LastModifiedDate = stored.LastModifiedDate;
        if (data is IBusinessEntityPublication publication) publication.PublishedVersion = entity.PublishedVersion;
        return data;
    }
    // История metadata читается отдельно от навигационного графа и тяжёлых тел.
    public async Task<IReadOnlyList<BusinessEntityDataVersionInfo>> GetVersionsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        return await db.BusinessEntityDataItems.AsNoTracking().Where(x => x.BusinessEntityId == id).OrderByDescending(x => x.Version)
            .Select(x => new BusinessEntityDataVersionInfo { Id = x.Id, BusinessEntityId = x.BusinessEntityId, Version = x.Version,
                Name = x.Name, SavedByUserId = x.SavedByUserId, VersionDescription = x.VersionDescription,
                CreatedDate = x.CreatedDate, LastModifiedDate = x.LastModifiedDate }).ToListAsync(ct);
    }
    // Возвращает копии рёбер выбранного снимка без SQL.
    public async Task<IReadOnlyList<BusinessEntityRelation>> GetRelationsAsync(Guid? a = null, Guid? b = null, CancellationToken ct = default)
    {
        await _graph.InitializeAsync(ct);
        return _graph.Read().Relations.Values.Where(x => (!a.HasValue || x.ObjectAId == a) && (!b.HasValue || x.ObjectBId == b))
            .Select(x => x.Copy()).ToArray();
    }
    // Отдельное ребро никогда не выдаётся как изменяемый объект общего кэша.
    public async Task<BusinessEntityRelation?> GetRelationByIdAsync(Guid id, CancellationToken ct = default)
    {
        await _graph.InitializeAsync(ct);
        return _graph.Read().Relations.TryGetValue(id, out var row) ? row.Copy() : null;
    }
}

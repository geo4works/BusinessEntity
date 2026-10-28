using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

// SQL-проекции структурного чтения; следующая стадия переводит граф в согласованный кэш.
internal sealed partial class DataProviderReadService
{
    // Возвращает лёгкие карточки без тяжёлого содержимого.
    public async Task<IReadOnlyList<Core.Classes.BusinessEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        return (await db.BusinessEntities.AsNoTracking().ToListAsync(ct)).Select(DataProviderMapper.ToBusinessEntity).ToArray();
    }
    // Возвращает карточку по точной идентичности или законное отсутствие.
    public async Task<Core.Classes.BusinessEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        var row = await db.BusinessEntities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? null : DataProviderMapper.ToBusinessEntity(row);
    }
    // Возвращает выбранную полную версию с её названием и проекцией публикации.
    public async Task<IBusinessEntityData?> GetPayloadAsync(Guid id, int? version, CancellationToken ct = default)
    {
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        await using var db = new KmsBusinessEntityDbContext(_options);
        var rows = db.BusinessEntityDataItems.AsNoTracking().Where(x => x.BusinessEntityId == id);
        var row = version.HasValue ? await rows.SingleOrDefaultAsync(x => x.Version == version, ct)
            : await rows.OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (row == null) return null;
        if (row.Version < 1 || string.IsNullOrWhiteSpace(row.Data)) throw new InvalidDataException("Повреждён payload.");
        var entity = await db.BusinessEntities.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var data = _codec.DeserializeEnvelope<IBusinessEntityData>(row.Data);
        if (data.EntityType != entity.EntityType) throw new InvalidDataException("Payload не соответствует типу владельца.");
        data.Version = row.Version; data.Name = row.Name; data.Id = id;
        data.CreatedDate = row.CreatedDate; data.LastModifiedDate = row.LastModifiedDate;
        if (data is IBusinessEntityPublication publication) publication.PublishedVersion = entity.PublishedVersion;
        return data;
    }
    // Возвращает историю metadata без материализации содержимого версий.
    public async Task<IReadOnlyList<BusinessEntityDataVersionInfo>> GetVersionsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        return await db.BusinessEntityDataItems.AsNoTracking().Where(x => x.BusinessEntityId == id).OrderByDescending(x => x.Version)
            .Select(x => new BusinessEntityDataVersionInfo { Id = x.Id, BusinessEntityId = x.BusinessEntityId, Version = x.Version,
                Name = x.Name, SavedByUserId = x.SavedByUserId, VersionDescription = x.VersionDescription,
                CreatedDate = x.CreatedDate, LastModifiedDate = x.LastModifiedDate }).ToListAsync(ct);
    }
    // Возвращает связи всего графа или заданных концов.
    public async Task<IReadOnlyList<BusinessEntityRelation>> GetRelationsAsync(Guid? a = null, Guid? b = null, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        return (await db.BusinessEntityRelations.AsNoTracking().Where(x => (!a.HasValue || x.ObjectAId == a) && (!b.HasValue || x.ObjectBId == b))
            .ToListAsync(ct)).Select(DataProviderMapper.ToBusinessEntityRelation).ToArray();
    }
    // Возвращает одно ребро по точной идентичности.
    public async Task<BusinessEntityRelation?> GetRelationByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        var row = await db.BusinessEntityRelations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? null : DataProviderMapper.ToBusinessEntityRelation(row);
    }
}

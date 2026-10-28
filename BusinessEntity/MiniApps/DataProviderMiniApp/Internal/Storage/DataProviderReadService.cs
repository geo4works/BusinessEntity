using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Caching;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

// Проецирует SQL storage в явные контракты чтения без выдачи чужим владельцам репозиториев.
internal sealed partial class DataProviderReadService
{
    private readonly DbContextOptions<KmsBusinessEntityDbContext> _options;
    private readonly ImmutableEntityFileStore _files;
    private readonly EntityDataStorageCodec _codec;
    private readonly DataProviderGraphCache _graph;
    private readonly BoundedPayloadCache _payloads;
    // Получает только собственные зависимости хранения.
    public DataProviderReadService(DbContextOptions<KmsBusinessEntityDbContext> options, ImmutableEntityFileStore files, EntityDataStorageCodec codec, DataProviderGraphCache graph, BoundedPayloadCache payloads)
    { _options = options; _files = files; _codec = codec; _graph = graph; _payloads = payloads; }

    // Читает отдельную activity-таблицу по владельцу.
    public async Task<IReadOnlyList<StoredEntityComment>> GetCommentsAsync(Guid entityId, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        return await db.BusinessEntityComments.AsNoTracking().Where(x => x.BusinessEntityId == entityId)
            .OrderBy(x => x.CreatedDate).Select(x => new StoredEntityComment(x.Id, x.BusinessEntityId, x.ParentId,
                x.Data, x.CreatedDate, x.LastModifiedDate)).ToListAsync(ct);
    }

    // Выбирает свой уровень хранения и возвращает только read-проекцию.
    public async Task<IReadOnlyList<StoredEntityProperty>> GetPropertiesAsync(EntityPropertyOwner owner, Guid? parentId, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        IQueryable<StoredEntityProperty> query = owner switch
        {
            EntityPropertyOwner.Entity => db.BusinessEntityProperties.AsNoTracking().Where(x => !parentId.HasValue || x.ParentEntityId == parentId.Value).OrderBy(x => x.CreatedDate).Select(x => new StoredEntityProperty(x.Id,
                x.ParentEntityId, owner, x.PropertyType, x.Data, x.Metadata, x.CreatedDate, x.LastModifiedDate)),
            EntityPropertyOwner.Data => db.BusinessEntityDataProperties.AsNoTracking().Where(x => !parentId.HasValue || x.ParentEntityId == parentId.Value).OrderBy(x => x.CreatedDate).Select(x => new StoredEntityProperty(x.Id,
                x.ParentEntityId, owner, x.PropertyType, x.Data, x.Metadata, x.CreatedDate, x.LastModifiedDate)),
            EntityPropertyOwner.Chunk => db.BusinessEntityDataChunkProperties.AsNoTracking().Where(x => !parentId.HasValue || x.ParentEntityId == parentId.Value).OrderBy(x => x.CreatedDate).Select(x => new StoredEntityProperty(x.Id,
                x.ParentEntityId, owner, x.PropertyType, x.Data, x.Metadata, x.CreatedDate, x.LastModifiedDate)),
            _ => throw new ArgumentOutOfRangeException(nameof(owner))
        };
        return await query.ToListAsync(ct);
    }

    // Соединяет references с точными строками чанков; порядок берётся из снимка, а не из старой строки.
    public async Task<RichTextDocumentChunkWindow> GetChunkWindowAsync(Guid entityId, int version, long start, int count,
        IReadOnlyList<Guid>? chunkIds = null, string? searchText = null, CancellationToken ct = default)
    {
        if (version < 0 || start < 0 || count < 0) throw new ArgumentOutOfRangeException(nameof(version));
        await using var db = new KmsBusinessEntityDbContext(_options);
        if (version == 0) version = await db.BusinessEntityDataItems.Where(x => x.BusinessEntityId == entityId).MaxAsync(x => (int?)x.Version, ct) ?? 0;
        if (version < 1 || !await db.BusinessEntityDataItems.AnyAsync(x => x.BusinessEntityId == entityId && x.Version == version, ct))
            throw new KeyNotFoundException("Версия документа не найдена.");
        var references = db.ChunkReferences.AsNoTracking().Where(x => x.BusinessEntityId == entityId && x.Version == version);
        var total = await references.CountAsync(ct);
        var query = from reference in references
                    join chunk in db.BusinessEntityDataChunks.AsNoTracking()
                    on new { Id = reference.ChunkId, Version = reference.ChunkRowVersion } equals new { chunk.Id, chunk.Version }
                    select new { Reference = reference, Chunk = chunk };
        if (chunkIds != null) query = query.Where(x => chunkIds.Contains(x.Chunk.Id));
        if (!string.IsNullOrWhiteSpace(searchText)) query = query.Where(x => x.Chunk.PlainText != null && x.Chunk.PlainText.ToLower().Contains(searchText.ToLower()));
        var rows = await query.Where(x => x.Reference.Position >= start).OrderBy(x => x.Reference.Position).Take(count).ToListAsync(ct);
        return new RichTextDocumentChunkWindow
        {
            BusinessEntityId = entityId, DocumentVersion = version, StartSortOrder = start, TotalChunkCount = total,
            Chunks = rows.Select(x => new BusinessEntityDataChunk
            {
                Id = x.Chunk.Id, BusinessEntityId = entityId, Version = x.Chunk.Version, SortOrder = x.Reference.Position,
                Blocks = RichTextChunkStorageSerializer.DeserializeChunkData(x.Chunk.Data),
                PlainText = x.Chunk.PlainText ?? string.Empty, HtmlCache = x.Chunk.HtmlCache ?? string.Empty,
                BlockCount = x.Chunk.BlockCount, CharCount = x.Chunk.CharCount, DataSizeBytes = x.Chunk.DataSizeBytes,
                Checksum = x.Chunk.Checksum ?? throw new InvalidDataException("Отсутствует digest чанка.")
            }).ToArray()
        };
    }

    // Загружает только структурные ссылки, сохраняя виртуализацию тела документа.
    public async Task<IReadOnlyList<VersionChunkMembership>> GetMembershipAsync(Guid entityId, int version, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        if (version < 1 || !await db.BusinessEntityDataItems.AnyAsync(x => x.Id == entityId && x.Version == version, ct))
            throw new KeyNotFoundException("Версия документа не найдена.");
        return await db.ChunkReferences.AsNoTracking().Where(x => x.BusinessEntityId == entityId && x.Version == version)
            .OrderBy(x => x.Position).Select(x => new VersionChunkMembership(x.ChunkId, x.ChunkRowVersion, x.Position)).ToListAsync(ct);
    }

    // Читает bytes небольшого embedded-файла внутри владельца физического хранения.
    public async Task<RichTextEmbeddedFileContent?> GetEmbeddedFileAsync(Guid entityId, string fileId, string variant, CancellationToken ct = default)
    {
        var file = await GetFileAsync(entityId, fileId, variant, ct);
        return file == null ? null : new RichTextEmbeddedFileContent { FileName = file.FileName,
            ContentType = file.ContentType, Content = await File.ReadAllBytesAsync(file.PhysicalPath, ct) };
    }

    // Вычисляет серверный путь только по проверенным собственным метаданным.
    public async Task<StoredEntityFile?> GetFileAsync(Guid entityId, string fileId, string variant, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        var row = await db.EntityFiles.AsNoTracking().SingleOrDefaultAsync(x => x.EntityId == entityId && x.FileId == fileId && x.Variant == variant, ct);
        return row == null ? null : new(row.EntityId, row.FileId, row.Variant, row.FileName, row.ContentType, row.Length, _files.ResolvePath(row));
    }
}

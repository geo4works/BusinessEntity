using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Единственный последовательный писатель связанных пакетов в одном DbContext и одной транзакции.
internal sealed partial class DataProviderTransactionService
{
    private readonly DbContextOptions<KmsBusinessEntityDbContext> _options;
    private readonly EntityDataStorageCodec _codec;
    private readonly IReadOnlyDictionary<BusinessEntityTypeEnum, IEntityVersionPolicy> _policies;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly IStorageFaults _faults;
    private readonly TimeSpan _outcomeRetention;
    private readonly ImmutableEntityFileStore _files;
    private readonly DataProviderCommitPublisher _publisher;
    private readonly IReadOnlyDictionary<BusinessEntityTypeEnum, IEntityArchivePolicy> _archivePolicies;

    // Получает только общие механизмы хранения и предметные политики, объявленные владельцами.
    public DataProviderTransactionService(DbContextOptions<KmsBusinessEntityDbContext> options,
        EntityDataStorageCodec codec, IEnumerable<IEntityVersionPolicy> policies,
        IStorageFaults faults, IConfiguration configuration, ImmutableEntityFileStore files, DataProviderCommitPublisher publisher, IEnumerable<IEntityArchivePolicy> archivePolicies)
    {
        _options = options; _codec = codec; _policies = policies.ToDictionary(p => p.EntityType);
        _faults = faults; _files = files; _publisher = publisher; _archivePolicies = archivePolicies.ToDictionary(x => x.EntityType);
        var days = configuration.GetValue("DataProvider:OperationRetentionDays", 7);
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(configuration), "Срок исходов должен быть положительным.");
        _outcomeRetention = TimeSpan.FromDays(days);
    }

    // Сначала фиксирует собственный снимок входа, затем последовательно применяет его целиком.
    public async Task<SaveBatchResult> SaveAsync(EntitySaveBatch batch, BusRequestContext actor,
        CancellationToken cancellationToken = default)
    {
        if (actor.UserId == null && !actor.IsSystem) throw new UnauthorizedAccessException();
        var prepared = Prepare(batch, actor);
        var changes = prepared.Supplement;
        await _writer.WaitAsync(cancellationToken);
        var committed = false;
        try
        {
            await using var db = new KmsBusinessEntityDbContext(_options);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var previous = await db.OperationOutcomes.FindAsync([batch.OperationId], cancellationToken);
            if (previous != null)
            {
                if (previous.PackageHash != prepared.Hash)
                    throw new BusRequestFailedException(new("operation.id_conflict", "OperationId уже использован для другого пакета."));
                return ReadResult(previous);
            }
            var now = DateTime.UtcNow;
            // PostgreSQL хранит timestamp с точностью микросекунд; JSON исхода получает то же время.
            now = new DateTime(now.Ticks / 10 * 10, DateTimeKind.Utc);
            var versions = new List<SavedEntityVersion>();
            Guid[] deleted;
            Guid[] deletedRelations;
            if (changes.ImportSnapshot != null)
            {
                versions.AddRange(await ImportArchiveAsync(db, changes.ImportSnapshot, prepared.Files, now, cancellationToken));
                deleted = []; deletedRelations = [];
            }
            else
            {
                foreach (var entity in prepared.Entities)
                {
                    var old = await db.BusinessEntities.FindAsync([entity.Id], cancellationToken);
                    if (((old == null && _archivePolicies.TryGetValue(entity.EntityType, out var owner) && owner.RequiresPayload)
                        || (old != null && _policies.ContainsKey(entity.EntityType) && old.Name != entity.Name))
                        && !prepared.Payloads.Any(p => p.EntityId == entity.Id) && !changes.Restores.Any(p => p.EntityId == entity.Id))
                        throw new ArgumentException("Создание или переименование статьи требует полного payload в том же пакете.");
                    await UpsertEntityAsync(db, entity, actor, now, cancellationToken);
                }
                // FK гарантирует целостность и для пакетной вставки новых сущностей.
                await db.SaveChangesAsync(cancellationToken);
                foreach (var payload in prepared.Payloads)
                    versions.Add(await SavePayloadAsync(db, payload, actor, now, cancellationToken));
                foreach (var restore in changes.Restores)
                    versions.Add(await RestoreAsync(db, restore, actor, now, cancellationToken));
                await ApplyRelationsAsync(db, prepared.Relations, changes.DeleteRelations, cancellationToken);
                await _files.PrepareAsync(db, batch.OperationId, prepared.Files, cancellationToken);
                await ApplySupplementaryAsync(db, changes, now, cancellationToken);
                await _files.AssertVersionReferencesAsync(db, cancellationToken);
                (deleted, deletedRelations) = await DeleteEntitiesAsync(db, changes.DeleteEntities,
                    prepared.Payloads.Select(x => x.EntityId).Concat(changes.Restores.Select(x => x.EntityId)).ToArray(), cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            _faults.Hit(StorageFaultPoint.AfterBusinessChanges);
            var changed = (changes.ImportSnapshot?.Entities.Select(e => e.Card.Id) ?? prepared.Entities.Select(e => e.Id)).Concat(prepared.Payloads.Select(p => p.EntityId))
                .Concat(changes.Publications.Select(p => p.EntityId)).Concat(changes.Restores.Select(p => p.EntityId))
                .Concat(changes.Comments.Select(p => p.EntityId)).Concat(changes.Files.Select(p => p.EntityId))
                .Concat(changes.VersionDescriptions.Select(p => p.EntityId))
                .Concat(changes.Properties.Where(p => p.Owner != EntityPropertyOwner.Chunk).Select(p => p.ParentEntityId))
                .Distinct().Except(deleted).ToArray();
            var result = new SaveBatchResult(batch.OperationId, now, versions, changed, deleted,
                (changes.ImportSnapshot?.Relations.Select(r => r.Id) ?? prepared.Relations.Select(r => r.Id)).ToArray(), changes.DeleteRelations.Concat(deletedRelations).Distinct().ToArray());
            db.OperationOutcomes.Add(new OperationOutcomeRow
            {
                OperationId = batch.OperationId, PackageHash = prepared.Hash, AuthorUserId = actor.UserId,
                CommittedAt = now, ResultJson = JsonSerializer.Serialize(result, StorageJsonOptions.Default)
            });
            await db.OperationOutcomes.Where(o => o.CommittedAt < now - _outcomeRetention)
                .ExecuteDeleteAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            _faults.Hit(StorageFaultPoint.BeforeCommit);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            // После этой границы ошибка/таймаут ответа не означает откат: исход уже находится в БД.
            _publisher.Publish(result);
            _faults.Hit(StorageFaultPoint.AfterCommit);
            await _files.TryRecoverAsync(_options);
            return result;
        }
        catch (Exception exception)
        {
            if (!committed) await _files.TryRecoverAsync(_options);
            for (Exception? cause = exception; cause != null; cause = cause.InnerException)
                if (cause is Npgsql.PostgresException { SqlState: "23503" })
                    throw new BusRequestFailedException(new("storage.reference_conflict", "Объект используется сохранёнными версиями или связями."));
            throw;
        }
        finally { _writer.Release(); }
    }

    // Читает только зафиксированный исход своего автора, без предположений по времени ожидания.
    public async Task<StoredOperationOutcome?> GetOutcomeAsync(Guid operationId, BusRequestContext actor,
        CancellationToken cancellationToken = default)
    {
        await using var db = new KmsBusinessEntityDbContext(_options);
        var row = await db.OperationOutcomes.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == operationId, cancellationToken);
        if (row == null) return null;
        if (!actor.IsSystem && row.AuthorUserId != actor.UserId) throw new UnauthorizedAccessException();
        return new(row.OperationId, row.AuthorUserId, row.CommittedAt, ReadResult(row));
    }

    // Изолирует пакет от дальнейших правок вызывающего кода и хэширует именно сохраняемое состояние.
    private PreparedBatch Prepare(EntitySaveBatch batch, BusRequestContext actor)
    {
        if (batch.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(batch.MiniAppId))
            throw new ArgumentException("OperationId и MiniAppId обязательны.");
        var entities = batch.Entities.Select(DataProviderMapper.ToDto).ToArray();
        var relations = batch.Relations.Select(DataProviderMapper.ToDto).ToArray();
        if (entities.Select(e => e.Id).Distinct().Count() != entities.Length ||
            batch.Payloads.Select(p => p.EntityId).Distinct().Count() != batch.Payloads.Count)
            throw new ArgumentException("Пакет содержит повторные сущности или payload.");
        var payloads = batch.Payloads.Select(p => new PreparedPayload(p.EntityId, p.BaseVersion,
            p.Data.Name, p.Data.EntityType, _codec.SerializePayload(p.Data),
            _policies.TryGetValue(p.Data.EntityType, out var policy) ? policy.GetContent(p.Data) : string.Empty,
            p.ChangedChunks?.Select(PrepareChunk).ToArray(), p.ChunkOrder?.ToArray(), p.VersionDescription)).ToArray();
        var frozen = JsonSerializer.Deserialize<FrozenSupplement>(JsonSerializer.Serialize(new FrozenSupplement(
            batch.DeleteEntities, batch.DeleteRelations, batch.Properties, batch.DeleteProperties, batch.Comments,
            batch.Files, batch.Publications, batch.VersionDescriptions, batch.Restores, batch.ImportSnapshot), StorageJsonOptions.Default), StorageJsonOptions.Default)
            ?? throw new InvalidDataException("Не удалось зафиксировать пакет.");
        if (frozen.ImportSnapshot != null && (entities.Length + payloads.Length + relations.Length + frozen.DeleteEntities.Count +
            frozen.DeleteRelations.Count + frozen.Comments.Count + frozen.Properties.Count + frozen.DeleteProperties.Count +
            frozen.Restores.Count + frozen.Publications.Count + frozen.VersionDescriptions.Count > 0))
            throw new ArgumentException("Восстановление графа не смешивается с обычными правками.");
        var files = frozen.Files.Select(_files.Describe).ToArray();
        // RequestId не входит в идентичность операции: повторное ожидание имеет свою корреляцию.
        var hashInput = JsonSerializer.Serialize(new { actor.UserId, actor.IsSystem, batch.MiniAppId,
            batch.EditSessionId, Entities = entities, Payloads = payloads, Relations = relations,
            frozen.DeleteEntities, frozen.DeleteRelations, frozen.Properties, frozen.DeleteProperties, frozen.Comments,
            frozen.Publications, frozen.VersionDescriptions, frozen.Restores, frozen.ImportSnapshot,
            Files = files.Select(f => new { f.Change.EntityId, f.Change.FileId, f.Change.Variant,
                f.Change.FileName, f.Change.ContentType, f.Hash, f.Length }) }, StorageJsonOptions.Default);
        if (payloads.Select(p => p.EntityId).Intersect(frozen.Restores.Select(p => p.EntityId)).Any()
            || frozen.Restores.Select(p => p.EntityId).Distinct().Count() != frozen.Restores.Count)
            throw new ArgumentException("Восстановление и редактирование одной сущности нельзя смешивать в пакете.");
        return new PreparedBatch(Hash(hashInput), entities, payloads, relations, frozen, files);
    }

    // Содержимое чанка фиксируется до первого await; его внешняя Version не определяет новый номер.
    private static BusinessEntityDataChunkDto PrepareChunk(Core.RichText.RichTextDocumentChunk chunk)
    {
        if (chunk.Id == Guid.Empty) throw new ArgumentException("ChunkId обязателен.");
        var json = RichTextChunkStorageSerializer.SerializeChunkData(chunk.Blocks);
        return new BusinessEntityDataChunkDto
        {
            CreatedDate = DateTime.UnixEpoch, LastModifiedDate = DateTime.UnixEpoch,
            Id = chunk.Id, BusinessEntityId = chunk.BusinessEntityId, SortOrder = chunk.SortOrder, Data = json,
            PlainText = RichTextChunkStorageSerializer.BuildPlainText(chunk.Blocks),
            HtmlCache = RichTextChunkStorageSerializer.BuildHtmlCache(chunk.BusinessEntityId, chunk.Id, chunk.Blocks),
            BlockCount = chunk.Blocks.Count, CharCount = RichTextChunkStorageSerializer.BuildCharCount(chunk.Blocks),
            DataSizeBytes = Encoding.UTF8.GetByteCount(json), Checksum = Hash(json)
        };
    }

    // Обновляет карточку с доверенным автором; старый автор создания не подменяется вводом клиента.
    private async Task UpsertEntityAsync(KmsBusinessEntityDbContext db, BusinessEntityDto incoming,
        BusRequestContext actor, DateTime now, CancellationToken ct)
    {
        if (incoming.Id == Guid.Empty || string.IsNullOrWhiteSpace(incoming.Name) || !_archivePolicies.ContainsKey(incoming.EntityType))
            throw new ArgumentException("Некорректная карточка сущности.");
        var existing = await db.BusinessEntities.FindAsync([incoming.Id], ct);
        if (existing == null)
        {
            incoming.PublishedVersion = 0;
            incoming.CreatedByUserId = actor.UserId; incoming.LastModifiedByUserId = actor.UserId;
            incoming.CreatedDate = now; incoming.LastModifiedDate = now;
            db.BusinessEntities.Add(incoming);
        }
        else
        {
            if (existing.EntityType != incoming.EntityType) throw new ArgumentException("Изменение типа сущности запрещено.");
            existing.Name = incoming.Name; existing.IsPublic = incoming.IsPublic;
            existing.LastModifiedByUserId = actor.UserId; existing.LastModifiedDate = now;
        }
    }

    // Применяет рёбра в том же контексте; внешний FK не допускает незавершённый граф.
    private static async Task ApplyRelationsAsync(KmsBusinessEntityDbContext db,
        IReadOnlyList<BusinessEntityRelationDto> relations, IReadOnlyList<Guid> deleted, CancellationToken ct)
    {
        foreach (var id in deleted)
        {
            var old = await db.BusinessEntityRelations.FindAsync([id], ct);
            if (old != null) db.BusinessEntityRelations.Remove(old);
        }
        foreach (var relation in relations)
        {
            var old = await db.BusinessEntityRelations.FindAsync([relation.Id], ct);
            if (old == null) db.BusinessEntityRelations.Add(relation);
            else db.Entry(old).CurrentValues.SetValues(relation);
        }
        await ValidateContainmentAsync(db, ct);
    }

    // Проверяет эффективный граф после всех правок текущего пакета, включая новые и удалённые рёбра.
    private static async Task<IReadOnlyList<BusinessEntityRelationDto>> EffectiveRelationsAsync(KmsBusinessEntityDbContext db, CancellationToken ct)
    {
        var edges = (await db.BusinessEntityRelations.AsNoTracking().ToListAsync(ct)).ToDictionary(x => x.Id);
        foreach (var entry in db.ChangeTracker.Entries<BusinessEntityRelationDto>())
            if (entry.State == EntityState.Deleted) edges.Remove(entry.Entity.Id);
            else edges[entry.Entity.Id] = entry.Entity;
        return edges.Values.ToArray();
    }
    // Contains является иерархией; цикл и несколько родителей одного узла отклоняются всем пакетом.
    private static async Task ValidateContainmentAsync(KmsBusinessEntityDbContext db, CancellationToken ct)
    {
        var edges = (await EffectiveRelationsAsync(db, ct)).Where(x => x.RelationType == "Contains").ToArray();
        if (edges.GroupBy(x => x.ObjectBId).Any(x => x.Count() > 1)) throw new ArgumentException("У узла несколько родителей Contains.");
        var parents = edges.ToDictionary(x => x.ObjectBId, x => x.ObjectAId);
        foreach (var id in parents.Keys)
        {
            var seen = new HashSet<Guid> { id }; var current = id;
            while (parents.TryGetValue(current, out current))
                if (!seen.Add(current)) throw new ArgumentException("Цикл Contains запрещён.");
        }
    }

    // Проверяет сохранённый результат вместо подмены повреждённого исхода пустым ответом.
    private static SaveBatchResult ReadResult(OperationOutcomeRow row) =>
        JsonSerializer.Deserialize<SaveBatchResult>(row.ResultJson, StorageJsonOptions.Default)
        ?? throw new InvalidDataException("Повреждён исход операции.");
    // Вычисляет стабильный технический digest содержимого.
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // Собственный неизменяемый снимок входа после подготовки, без ссылок на изменяемые UI-данные.
    private sealed record PreparedBatch(string Hash, BusinessEntityDto[] Entities, PreparedPayload[] Payloads,
        BusinessEntityRelationDto[] Relations, FrozenSupplement Supplement, PreparedEntityFile[] Files);
    // Подготовленный manifest и точные изменения автора.
    private sealed record PreparedPayload(Guid EntityId, int BaseVersion, string Name, BusinessEntityTypeEnum Type,
        string Json, string SemanticContent, BusinessEntityDataChunkDto[]? Chunks, Guid[]? Order, string Description);
    // Дополнительные изменения замораживаются вместе с основным пакетом.
    private sealed record FrozenSupplement(IReadOnlyList<Guid> DeleteEntities, IReadOnlyList<Guid> DeleteRelations,
        IReadOnlyList<EntityPropertyChange> Properties, IReadOnlyList<EntityPropertyKey> DeleteProperties,
        IReadOnlyList<EntityCommentChange> Comments, IReadOnlyList<EntityFileChange> Files,
        IReadOnlyList<EntityPublicationChange> Publications, IReadOnlyList<EntityVersionDescriptionChange> VersionDescriptions,
        IReadOnlyList<EntityVersionRestore> Restores, GraphArchive? ImportSnapshot);
}

// Точки искусственного сбоя принадлежат внутренним интеграционным тестам хранилища.
internal enum StorageFaultPoint { AfterBusinessChanges, BeforeCommit, AfterCommit, BeforeFilePublish }
// Тестовый seam не передаётся ни в пакете, ни через публичную шину.
internal interface IStorageFaults
{
    // По умолчанию не вмешивается в применение операции.
    void Hit(StorageFaultPoint point);
}
// Штатная реализация без внесения сбоев.
internal sealed class NoStorageFaults : IStorageFaults
{
    // Ничего не меняет в производственном применении.
    public void Hit(StorageFaultPoint point) { }
}

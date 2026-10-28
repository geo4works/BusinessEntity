using System.Reactive.Linq;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity = BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

// Проверяет гарантии пакета на PostgreSQL, включая реальный commit, rollback и внешние ключи.
public sealed class TransactionTests
{
    // Ошибка после сохранения нескольких записей откатывает и граф, и outcome.
    [PostgresFact]
    public async Task FailureRollsBackWholeBatchAndOutcome()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var first = NewEntity("first", BusinessEntityTypeEnum.Folder);
        var second = NewEntity("second", BusinessEntityTypeEnum.Folder);
        var batch = Batch() with { Entities = [first, second], Relations = [new BusinessEntityRelation
            { ObjectAId = first.Id, ObjectBId = second.Id, RelationType = "Contains" }] };
        fixture.Faults.ThrowAt = StorageFaultPoint.AfterBusinessChanges;
        await Assert.ThrowsAsync<IOException>(() => fixture.Writer.SaveAsync(batch, Actor()));
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(0, await db.BusinessEntities.CountAsync());
        Assert.Equal(0, await db.BusinessEntityRelations.CountAsync());
        Assert.Equal(0, await db.OperationOutcomes.CountAsync());
    }

    // Повтор после нового экземпляра писателя читает outcome БД и не создаёт вторую версию.
    [PostgresFact]
    public async Task RetrySurvivesRestartAndRejectsDifferentPackage()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var entity = NewEntity("document", BusinessEntityTypeEnum.Document);
        var batch = Batch() with { Entities = [entity], Payloads = [new(entity.Id, 0, new Document { Name = entity.Name, Text = "v1" })] };
        var actor = Actor();
        var first = await fixture.Writer.SaveAsync(batch, actor);
        var restarted = fixture.CreateWriter();
        var repeated = await restarted.SaveAsync(batch, actor);
        Assert.Equal(first.OperationId, repeated.OperationId);
        Assert.Equal(first.CommittedAt, repeated.CommittedAt);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(1, await db.BusinessEntityDataItems.CountAsync());
        var conflict = batch with { Payloads = [new(entity.Id, 0, new Document { Name = entity.Name, Text = "different" })] };
        var error = await Assert.ThrowsAsync<BusRequestFailedException>(() => restarted.SaveAsync(conflict, actor));
        Assert.Equal("operation.id_conflict", error.Error.Code);
    }

    // Потеря ответа после commit выясняется по исходу без повторного применения.
    [PostgresFact]
    public async Task LostReplyAfterCommitHasDurableOutcome()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var batch = Batch() with { Entities = [NewEntity("committed", BusinessEntityTypeEnum.Folder)] };
        var actor = Actor(); fixture.Faults.ThrowAt = StorageFaultPoint.AfterCommit;
        await Assert.ThrowsAsync<IOException>(() => fixture.Writer.SaveAsync(batch, actor));
        fixture.Faults.ThrowAt = null;
        var restarted = fixture.CreateWriter();
        Assert.NotNull(await restarted.GetOutcomeAsync(batch.OperationId, actor));
        await restarted.SaveAsync(batch, actor);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(1, await db.BusinessEntities.CountAsync());
    }

    // B сохраняет свою v1+дельту; его v3 не подхватывает неизменённый им чанк из v2 автора A.
    [PostgresFact]
    public async Task ParallelAuthorsKeepFullOwnStateAndRestoreCreatesNewSnapshot()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var entity = NewEntity("article", BusinessEntityTypeEnum.RichTextDocument);
        var a = Chunk(entity.Id, "original A"); var b = Chunk(entity.Id, "original B");
        var manifest = new RichTextDocument { Name = entity.Name };
        await fixture.Writer.SaveAsync(Batch() with { Entities = [entity], Payloads = [new(entity.Id, 0, manifest, [a, b], [a.Id, b.Id])] }, Actor());
        var changedA = Chunk(entity.Id, "author A", a.Id); var changedB = Chunk(entity.Id, "author B", b.Id);
        var actorA = Actor(); var actorB = Actor();
        var resultA = await fixture.Writer.SaveAsync(Batch() with { Payloads = [new(entity.Id, 1, manifest, [changedA])] }, actorA);
        var resultB = await fixture.Writer.SaveAsync(Batch() with { Payloads = [new(entity.Id, 1, manifest, [changedB])] }, actorB);
        Assert.Equal(2, resultA.Versions.Single().Version); Assert.Equal(3, resultB.Versions.Single().Version);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        var v2 = await db.ChunkReferences.Where(x => x.Version == 2).OrderBy(x => x.Position).ToListAsync();
        var v3 = await db.ChunkReferences.Where(x => x.Version == 3).OrderBy(x => x.Position).ToListAsync();
        Assert.Equal(new[] { 2, 1 }, v2.Select(x => x.ChunkRowVersion));
        Assert.Equal(new[] { 1, 3 }, v3.Select(x => x.ChunkRowVersion));
        var restored = await fixture.Writer.SaveAsync(Batch() with { Restores = [new(entity.Id, 2)] }, actorB);
        Assert.Equal(4, restored.Versions.Single().Version);
        var v4 = await db.ChunkReferences.AsNoTracking().Where(x => x.Version == 4).OrderBy(x => x.Position).ToListAsync();
        Assert.Equal(v2.Select(x => (x.ChunkId, x.ChunkRowVersion)), v4.Select(x => (x.ChunkId, x.ChunkRowVersion)));
        Assert.Equal(4, await db.BusinessEntityDataChunks.CountAsync());
        Assert.Equal(actorA.UserId, (await db.BusinessEntityDataItems.FindAsync(entity.Id, 2))!.SavedByUserId);
    }

    // Указатель публикации, описание и no-op не переписывают payload и не увеличивают номер.
    [PostgresFact]
    public async Task PublicationAndDescriptionLeaveHistoryPayloadUntouched()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var entity = NewEntity("article", BusinessEntityTypeEnum.RichTextDocument);
        var manifest = new RichTextDocument { Name = entity.Name };
        var actor = Actor();
        await fixture.Writer.SaveAsync(Batch() with { Entities = [entity], Payloads = [new(entity.Id, 0, manifest, [], [])] }, actor);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        var before = await db.BusinessEntityDataItems.AsNoTracking().SingleAsync();
        await fixture.Writer.SaveAsync(Batch() with { Publications = [new(entity.Id, 1)], VersionDescriptions = [new(entity.Id, 1, "description")] }, actor);
        var unchanged = await fixture.Writer.SaveAsync(Batch() with { Payloads = [new(entity.Id, 1, manifest)] }, actor);
        Assert.False(unchanged.Versions.Single().CreatedVersion);
        var after = await db.BusinessEntityDataItems.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("publishedVersion", after.Data);
        Assert.Equal(before.Data, after.Data); Assert.Equal(before.CreatedDate, after.CreatedDate);
        Assert.Equal("description", after.VersionDescription);
        Assert.Equal(1, (await db.BusinessEntities.AsNoTracking().SingleAsync()).PublishedVersion);
    }

    // Вставка и удаление в новой статье не меняют состав предыдущего снимка.
    [PostgresFact]
    public async Task InsertAndDeletePreserveHistoricalMembership()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var entity = NewEntity("article", BusinessEntityTypeEnum.RichTextDocument);
        var manifest = new RichTextDocument { Name = entity.Name };
        var original = Chunk(entity.Id, "original"); var inserted = Chunk(entity.Id, "inserted");
        await fixture.Writer.SaveAsync(Batch() with { Entities = [entity], Payloads = [new(entity.Id, 0, manifest, [original], [original.Id])] }, Actor());
        await fixture.Writer.SaveAsync(Batch() with { Payloads = [new(entity.Id, 1, manifest, [inserted], [inserted.Id])] }, Actor());
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(original.Id, (await db.ChunkReferences.SingleAsync(x => x.Version == 1)).ChunkId);
        Assert.Equal(inserted.Id, (await db.ChunkReferences.SingleAsync(x => x.Version == 2)).ChunkId);
        Assert.NotNull(await db.BusinessEntityDataChunks.FindAsync(original.Id, 1));
    }

    // Событие не предшествует коммиту и отсутствует при откате пакета.
    [PostgresFact]
    public async Task DataEventsArePublishedAfterCommitOnly()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var eventCount = 0; var committedAtDelivery = false;
        using var subscription = fixture.Bus.Listen<EntityBatchCommitted>().Subscribe(message =>
        {
            eventCount++;
            using var read = new KmsBusinessEntityDbContext(fixture.Options);
            committedAtDelivery = read.OperationOutcomes.Any(x => x.OperationId == message.OperationId);
        });
        fixture.Faults.ThrowAt = StorageFaultPoint.BeforeCommit;
        await Assert.ThrowsAsync<IOException>(() => fixture.Writer.SaveAsync(Batch() with
            { Entities = [NewEntity("rollback", BusinessEntityTypeEnum.Folder)] }, Actor()));
        Assert.Equal(0, eventCount);
        fixture.Faults.ThrowAt = null;
        await fixture.Writer.SaveAsync(Batch() with { Entities = [NewEntity("commit", BusinessEntityTypeEnum.Folder)] }, Actor());
        Assert.Equal(1, eventCount); Assert.True(committedAtDelivery);
    }

    // Метаданные схемы обязательны, а собственные таблицы User создаются рядом без ALTER/backfill.
    [PostgresFact]
    public async Task SchemaInitializationIsStrictAndCreatesOwnedUserTablesOnce()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        Assert.False(await new DataProviderSchema(fixture.Options).InitializeAsync());
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        var userOptions = new DbContextOptionsBuilder<UserMiniAppDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options;
        await using (var users = new UserMiniAppDbContext(userOptions))
        {
            await UserMiniAppStorageSchema.InitializeAsync(users, true, default);
            await UserMiniAppStorageSchema.InitializeAsync(users, false, default);
            Assert.Equal(0, await users.Users.CountAsync());
        }
        var schema = await db.StorageSchema.SingleAsync(); schema.Version = 1; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new DataProviderSchema(fixture.Options).InitializeAsync());
    }

    // Каждая операция и каждый автор получают отдельную серверную корреляцию.
    private static EntitySaveBatch Batch() => new(Guid.NewGuid(), "test", Guid.NewGuid());
    // Создаёт доверенный тестовый адрес, не связанный с principal рабочего приложения.
    private static BusRequestContext Actor() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    // Создаёт легковесную карточку теста.
    private static Entity NewEntity(string name, BusinessEntityTypeEnum type) => new() { Name = name, EntityType = type };
    // Формирует содержимое технического чанка с сохраняемым идентификатором.
    private static RichTextDocumentChunk Chunk(Guid entity, string text, Guid? id = null) => new()
        { Id = id ?? Guid.NewGuid(), BusinessEntityId = entity, Blocks = [new RichTextBlock { Html = text }] };
}

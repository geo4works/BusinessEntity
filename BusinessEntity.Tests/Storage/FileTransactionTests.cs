using System.Text;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity = BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

// Проверяет диск отдельно от SQL-транзакции, включая подготовку и восстановление после сбоя.
public sealed class FileTransactionTests
{
    // Подготовленные bytes не становятся SQL-ссылкой при отказе коммита и убираются как сироты.
    [PostgresFact]
    public async Task CommitFailureLeavesNoReferencesOrPublishedFiles()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var entity = Entity(); var file = FileChange(entity.Id, "image", "bytes");
        fixture.Faults.ThrowAt = StorageFaultPoint.BeforeCommit;
        await Assert.ThrowsAsync<IOException>(() => fixture.Writer.SaveAsync(Batch(entity, file), Actor()));
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(0, await db.EntityFiles.CountAsync());
        Assert.Equal(0, await db.BusinessEntities.CountAsync());
        Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot, "*.blob", SearchOption.AllDirectories));
    }

    // Ошибка публикации файла откатывает бизнес-пакет ещё до появления SQL-ссылки.
    [PostgresFact]
    public async Task FileWriteFailureRollsBackBusinessRows()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var entity = Entity(); fixture.Faults.ThrowAt = StorageFaultPoint.BeforeFilePublish;
        await Assert.ThrowsAsync<IOException>(() => fixture.Writer.SaveAsync(Batch(entity, FileChange(entity.Id, "image", "bytes")), Actor()));
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(0, await db.EntityFiles.CountAsync()); Assert.Equal(0, await db.OperationOutcomes.CountAsync());
    }

    // Рестарт находит неподтверждённый файловый объект, оставленный прерыванием до SQL commit.
    [PostgresFact]
    public async Task StartupRecoveryRemovesOnlyUnreferencedPreparedObjects()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var entity = Entity(); var kept = FileChange(entity.Id, "kept", "committed");
        await fixture.Writer.SaveAsync(Batch(entity, kept), Actor());
        await using (var interrupted = new KmsBusinessEntityDbContext(fixture.Options))
        {
            var orphan = fixture.Files.Describe(FileChange(entity.Id, "orphan", "not committed"));
            await fixture.Files.PrepareAsync(interrupted, Guid.NewGuid(), [orphan], default);
        }
        Assert.Equal(2, Directory.EnumerateFiles(fixture.StorageRoot, "*.blob", SearchOption.AllDirectories).Count());
        await fixture.Files.RecoverAsync(fixture.Options);
        Assert.Single(Directory.EnumerateFiles(fixture.StorageRoot, "*.blob", SearchOption.AllDirectories));
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        var row = await db.EntityFiles.SingleAsync();
        Assert.Equal("committed", await File.ReadAllTextAsync(Path.Combine(fixture.StorageRoot, row.RelativePath)));
    }

    // Логический FileId не меняет содержимое под уже сохранёнными историческими ссылками.
    [PostgresFact]
    public async Task FileIdentityIsImmutableAndOldFilesRemainReadable()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var entity = Entity(); var actor = Actor();
        await fixture.Writer.SaveAsync(Batch(entity, FileChange(entity.Id, "image", "first")), actor);
        var error = await Assert.ThrowsAsync<BusRequestFailedException>(() => fixture.Writer.SaveAsync(
            new EntitySaveBatch(Guid.NewGuid(), "test", null) { Files = [FileChange(entity.Id, "image", "second")] }, actor));
        Assert.Equal("file.identity_conflict", error.Error.Code);
        await fixture.Writer.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "test", null)
            { Files = [FileChange(entity.Id, "another-image", "second")] }, actor);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        var rows = await db.EntityFiles.OrderBy(x => x.FileId).ToListAsync();
        Assert.Equal(2, rows.Count);
        var original = rows.Single(x => x.FileId == "image");
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(fixture.StorageRoot, original.RelativePath)));
    }

    // Временный путь не входит в идентичность пакета: повтор с теми же bytes возвращает тот же исход.
    [PostgresFact]
    public async Task TemporaryFileRetryUsesContentIdentity()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        Directory.CreateDirectory(fixture.StorageRoot);
        var sourceA = Path.Combine(fixture.StorageRoot, "source-a.tmp");
        var sourceB = Path.Combine(fixture.StorageRoot, "source-b.tmp");
        await File.WriteAllTextAsync(sourceA, "video bytes"); await File.WriteAllTextAsync(sourceB, "video bytes");
        var entity = Entity(); var actor = Actor();
        var batch = Batch(entity, new(entity.Id, "video", "original", "video.mp4", "video/mp4", [], sourceA));
        var first = await fixture.Writer.SaveAsync(batch, actor);
        var repeated = await fixture.CreateWriter().SaveAsync(batch with { Files = [batch.Files[0] with { TemporaryFilePath = sourceB }] }, actor);
        Assert.Equal(first.CommittedAt, repeated.CommittedAt);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(1, await db.EntityFiles.CountAsync());
    }

    // Одновременные авторы получают последовательные номера, каждый со своим полным содержимым от v1.
    [PostgresFact]
    public async Task ConcurrentSavesAreOrderedWithoutMergingAuthors()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var entity = Entity(); entity.EntityType = BusinessEntityTypeEnum.RichTextDocument;
        var manifest = new RichTextDocument { Name = entity.Name };
        var chunk = new RichTextDocumentChunk { BusinessEntityId = entity.Id, Blocks = [new RichTextBlock { Html = "base" }] };
        await fixture.Writer.SaveAsync(new(Guid.NewGuid(), "test", null) { Entities = [entity], Payloads = [new(entity.Id, 0, manifest, [chunk], [chunk.Id])] }, Actor());
        var tasks = Enumerable.Range(1, 6).Select(i => fixture.Writer.SaveAsync(new(Guid.NewGuid(), "test", null)
        {
            Payloads = [new(entity.Id, 1, manifest, [new RichTextDocumentChunk { Id = chunk.Id,
                BusinessEntityId = entity.Id, Blocks = [new RichTextBlock { Html = "author " + i }] }])]
        }, Actor())).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(Enumerable.Range(2, 6), results.Select(r => r.Versions.Single().Version).Order());
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(7, await db.BusinessEntityDataItems.CountAsync());
        Assert.Equal(7, await db.OperationOutcomes.CountAsync());
    }

    // Создаёт карточку, которую owns только тестовая БД.
    private static Entity Entity() => new() { Name = "files", EntityType = BusinessEntityTypeEnum.Folder };
    // Собирает атомарную карточку и её файл.
    private static EntitySaveBatch Batch(Entity entity, EntityFileChange file) => new(Guid.NewGuid(), "test", null) { Entities = [entity], Files = [file] };
    // Создаёт короткий файловый объект для настоящего дискового IO.
    private static EntityFileChange FileChange(Guid entity, string id, string content) => new(entity, id, "original", id + ".txt", "text/plain", Encoding.UTF8.GetBytes(content));
    // Задаёт доверенный серверный адрес тестового автора.
    private static BusRequestContext Actor() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
}

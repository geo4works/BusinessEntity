using BusinessEntity.MiniApps.UserMiniApp.Storage;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Repositories.EfPostgres;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Entities;
using BusinessEntity.Core.Chunks;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity = BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

// Проверяет целостность связанных изменений на реальной PostgreSQL.
public sealed class GraphIntegrityTests
{
    // Проверяет перенос и удаление с учётом связей того же пакета и запрет цикла.
    [PostgresFact]
    public async Task MoveThenDeleteOldParentUsesRelationsFromSameBatch()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var actor = Actor(); var old = Folder("old"); var next = Folder("next"); var child = Folder("child");
        var edge = new BusinessEntityRelation { ObjectAId = old.Id, ObjectBId = child.Id, RelationType = "Contains" };
        await fixture.Writer.SaveAsync(Batch() with { Entities = [old, next, child], Relations = [edge] }, actor);
        var result = await fixture.Writer.SaveAsync(Batch() with { DeleteRelations = [edge.Id],
            Relations = [new() { ObjectAId = next.Id, ObjectBId = child.Id, RelationType = "Contains" }], DeleteEntities = [old.Id] }, actor);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.NotNull(await db.BusinessEntities.FindAsync(child.Id)); Assert.Null(await db.BusinessEntities.FindAsync(old.Id));
        Assert.Equal(next.Id, (await db.BusinessEntityRelations.SingleAsync()).ObjectAId);
        Assert.Contains(edge.Id, result.DeletedRelations);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.SaveAsync(Batch() with
            { Relations = [new() { ObjectAId = child.Id, ObjectBId = next.Id, RelationType = "Contains" }] }, actor));
        Assert.Single(await db.BusinessEntityRelations.ToListAsync());
    }

    // Проверяет отсутствующий файл, защиту видео историей и полный экспорт зависимостей.
    [PostgresFact]
    public async Task MissingFileRejectsVersionAndHistoricalReferenceProtectsVideo()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var actor = Actor();
        var video = new Entity { Name = "video", EntityType = global::BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.MediaVideoType.Id };
        var article = new Entity { Name = "article", EntityType = global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id };
        var manifest = new RichTextDocument { Name = article.Name };
        var chunk = new BusinessEntityDataChunk { BusinessEntityId = article.Id,
            Blocks = [new BusinessEntityDataBlock { Kind = "video", VideoId = video.Id.ToString("D") }] };
        var batch = Batch() with { Entities = [article], Payloads = [new(article.Id, 0, manifest, [chunk], [chunk.Id])] };
        var missing = await Assert.ThrowsAsync<BusRequestFailedException>(() => fixture.Writer.SaveAsync(batch, actor));
        Assert.Equal("file.reference_missing", missing.Error.Code);
        await fixture.Writer.SaveAsync(batch with { Entities = [article, video],
            Payloads = [batch.Payloads.Single(), new(video.Id, 0, new MediaVideo { Name = video.Name, FileName = "v.mp4", OriginalSizeBytes = 3 })],
            Files = [new(video.Id, "video", "original", "v.mp4", "video/mp4", [1, 2, 3])] }, actor);
        // Новая версия убрала видео, но прежняя обязана оставаться читаемой.
        await fixture.Writer.SaveAsync(Batch() with { Payloads = [new(article.Id, 1, manifest, [], [])] }, actor);
        var error = await Assert.ThrowsAsync<BusRequestFailedException>(() => fixture.Writer.SaveAsync(Batch() with { DeleteEntities = [video.Id] }, actor));
        Assert.Equal("storage.reference_conflict", error.Error.Code);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(3, await db.BusinessEntityDataItems.CountAsync());
        Assert.Single(await db.EntityFiles.ToListAsync());
        var export = await fixture.Writer.ExportAsync(new(article.Id, [], []), actor, fixture.Leases);
        try { Assert.Contains(export.Archive.Entities, entity => entity.Card.Id == video.Id); }
        finally { fixture.Leases.Release(export.ExportId, actor); }
        await fixture.Writer.SaveAsync(Batch() with { DeleteEntities = [article.Id, video.Id] }, actor);
        Assert.Equal(0, await db.BusinessEntities.CountAsync());
    }

    // Проверяет откат свойств без владельца и циклических комментариев.
    [PostgresFact]
    public async Task OrphanPropertyAndCommentCycleRollbackTheEntireBatch()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var actor = Actor(); var root = Folder("root");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.SaveAsync(Batch() with { Entities = [root],
            Properties = [new(Guid.NewGuid(), Guid.NewGuid(), EntityPropertyOwner.Data, 1, "{}", "")] }, actor));
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.SaveAsync(Batch() with { Entities = [root],
            Comments = [new(a, root.Id, b, "a", DateTime.UtcNow), new(b, root.Id, a, "b", DateTime.UtcNow)] }, actor));
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(0, await db.BusinessEntities.CountAsync()); Assert.Equal(0, await db.OperationOutcomes.CountAsync());
    }
    // Проверяет запрет смешивать сохранение версии и удаление её владельца.
    [PostgresFact]
    public async Task DeleteCannotReturnSavedVersionForDeletedDescendantEvenForNoOp()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var actor = Actor();
        var root = Folder("root"); var doc = new Entity { Name = "document", EntityType = global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id };
        var data = new Document { Name = doc.Name, Text = "text" };
        await fixture.Writer.SaveAsync(Batch() with { Entities = [root, doc], Payloads = [new(doc.Id, 0, data)],
            Relations = [new() { ObjectAId = root.Id, ObjectBId = doc.Id, RelationType = "Contains" }] }, actor);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.SaveAsync(Batch() with
            { Payloads = [new(doc.Id, 1, data)], DeleteEntities = [root.Id] }, actor));
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Equal(2, await db.BusinessEntities.CountAsync()); Assert.Single(await db.BusinessEntityDataItems.ToListAsync());
    }

    // Проверяет создание обязательных карточки и payload в одном коммите.
    [PostgresFact]
    public async Task RequiredPayloadCannotBeCreatedInSeparateCommit()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Writer.SaveAsync(Batch() with
            { Entities = [new Entity { Name = "video", EntityType = global::BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.MediaVideoType.Id }] }, Actor()));
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        Assert.Empty(await db.BusinessEntities.ToListAsync());
    }

    // Создаёт обычный узел для проверки целостности Contains.
    // Одновременные очистки одного служебного свойства из окон не создают ложный отказ EF.
    [PostgresFact]
    public async Task ConcurrentUserPropertyDeletionIsIdempotent()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        await using var graph = new KmsBusinessEntityDbContext(fixture.Options);
        var options = new DbContextOptionsBuilder<UserMiniAppDbContext>().UseNpgsql(graph.Database.GetConnectionString()).Options;
        await using (var db = new UserMiniAppDbContext(options))
            await UserMiniAppStorageSchema.InitializeAsync(db, true, default);
        var repository = new UserPropertyDtoEfRepository(options);
        var property = new UserPropertyDto { ParentEntityId = Guid.NewGuid(), Data = "{}" };
        await repository.AddAsync(property);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 12).Select(async _ => { await start.Task; await repository.DeleteAsync(property.Id); }).ToArray();
        start.SetResult(); await Task.WhenAll(requests);
        Assert.Null(await repository.GetByIdAsync(property.Id));
    }

    private static Entity Folder(string name) => new() { Name = name, EntityType = global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id };
    // Создаёт независимого доверенного автора тестовой операции.
    private static BusRequestContext Actor() => new(Guid.NewGuid(), Guid.NewGuid());
    // Создаёт отдельную операцию сохранения тестового владельца.
    private static EntitySaveBatch Batch() => new(Guid.NewGuid(), "test", null);
}

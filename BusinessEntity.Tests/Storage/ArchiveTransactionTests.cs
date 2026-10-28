using BusinessEntity.MiniApps.BackupRestoreMiniApp.Internal;
using System.Text;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.Core.Chunks;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity = BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

// Весь граф, включая историю и файлы, переносится атомарно с новыми идентичностями.
public sealed class ArchiveTransactionTests
{
    // Проверяет полный перенос истории, авторов, свойств, комментариев, файлов и публикации.
    [PostgresFact]
    public async Task RoundTripPreservesHistoryAuthorsFilesCommentsAndPublication()
    {
        await using var source = await PostgresTestDatabase.CreateAsync();
        await using var target = await PostgresTestDatabase.CreateAsync();
        var actor = Actor(); var other = Actor();
        var root = new Entity { Name = "space", EntityType = global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id };
        var article = new Entity { Name = "article", EntityType = global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id };
        var chunk = new BusinessEntityDataChunk { BusinessEntityId = article.Id, Blocks = [new BusinessEntityDataBlock { Kind = "paragraph", Html = "<p>first</p>" }] };
        var comment = Guid.NewGuid();
        await source.Writer.SaveAsync(Batch() with { Entities = [root, article], Relations = [new() { ObjectAId = root.Id, ObjectBId = article.Id, RelationType = "Contains" }],
            Payloads = [new(article.Id, 0, new RichTextDocument { Name = article.Name }, [chunk], [chunk.Id])],
            Comments = [new(comment, article.Id, null, "comment", DateTime.UtcNow)],
            Properties = [new(Guid.NewGuid(), root.Id, EntityPropertyOwner.Entity, 1, "{}", "metadata")],
            Files = [new(article.Id, "image", "original", "image.png", "image/png", Encoding.UTF8.GetBytes("image bytes"))] }, actor);
        var changed = new BusinessEntityDataChunk { Id = chunk.Id, BusinessEntityId = article.Id,
            Blocks = [new BusinessEntityDataBlock { Kind = "paragraph", Html = $"<p><a href=\"/rich-document/{article.Id:D}\">article</a></p>" }] };
        await source.Writer.SaveAsync(Batch() with { Payloads = [new(article.Id, 1, new RichTextDocument { Name = "new title" }, [changed])], Publications = [new(article.Id, 1)] }, other);
        var exported = await source.Writer.ExportAsync(new(root.Id, ["Contains"], []), actor, source.Leases);
        try
        {
            var folder = Path.Combine(source.StorageRoot, "backup");
            var directory = new GraphArchiveDirectory();
            var initial = await directory.WriteAsync(folder, exported, default);
            Assert.Equal(2, initial.ChangedEntities); Assert.True(initial.ManifestUpdated);
            var repeated = await directory.WriteAsync(folder, exported, default);
            Assert.Equal(0, repeated.ChangedEntities); Assert.False(repeated.ManifestUpdated);
            var portable = await directory.ReadAsync(folder, default);
            var plan = target.Remapper.Remap(portable, "restored");
            var files = plan.Archive.Entities.SelectMany(e => e.Files.Select(f => new EntityFileChange(e.Card.Id, f.FileId, f.Variant,
                f.FileName, f.ContentType, [], GraphArchiveDirectory.Resolve(folder, f.ArchiveEntry)))).ToArray();
            await target.Writer.SaveAsync(Batch() with { ImportSnapshot = plan.Archive, Files = files }, actor);
            await using var db = new KmsBusinessEntityDbContext(target.Options);
            var mapped = plan.Identities.Entities[article.Id];
            Assert.Equal(2, await db.BusinessEntities.CountAsync());
            Assert.Equal(2, await db.BusinessEntityDataItems.CountAsync());
            Assert.Equal(actor.UserId, (await db.BusinessEntityDataItems.SingleAsync(x => x.Id == mapped && x.Version == 1)).SavedByUserId);
            Assert.Equal(other.UserId, (await db.BusinessEntityDataItems.SingleAsync(x => x.Id == mapped && x.Version == 2)).SavedByUserId);
            Assert.Equal(1, (await db.BusinessEntities.SingleAsync(x => x.Id == mapped)).PublishedVersion);
            Assert.Equal(mapped, (await db.BusinessEntityComments.SingleAsync()).BusinessEntityId);
            Assert.Single(await db.BusinessEntityProperties.ToListAsync());
            Assert.All(await db.BusinessEntityDataChunks.ToListAsync(), x => Assert.NotEqual(chunk.Id, x.Id));
            Assert.Contains(mapped.ToString("D"), (await db.BusinessEntityDataChunks.SingleAsync(x => x.Version == 2)).Data);
            var file = await db.EntityFiles.SingleAsync();
            Assert.Equal("image bytes", await File.ReadAllTextAsync(target.Files.ResolvePath(file)));
            var noOp = await target.Writer.SaveAsync(Batch() with { Payloads = [new(mapped, 2, new RichTextDocument { Name = "new title" })] }, actor);
            Assert.False(noOp.Versions.Single().CreatedVersion);
        }
        finally { source.Leases.Release(exported.ExportId, actor); }
    }

    // Проверяет атомарный отказ restore и повтор того же пакета после устранения сбоя.
    [PostgresFact]
    public async Task FailedRestoreLeavesNeitherGraphNorFilesAndCanRetry()
    {
        await using var source = await PostgresTestDatabase.CreateAsync();
        await using var target = await PostgresTestDatabase.CreateAsync();
        var actor = Actor(); var root = new Entity { Name = "space", EntityType = global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id };
        await source.Writer.SaveAsync(Batch() with { Entities = [root], Files = [new(root.Id, "file", "original", "file", "text/plain", [1, 2, 3])] }, actor);
        var export = await source.Writer.ExportAsync(new(root.Id, [], []), actor, source.Leases);
        try
        {
            var plan = target.Remapper.Remap(export.Archive, "restored"); var entry = plan.Archive.Entities.Single().Files.Single();
            var batch = Batch() with { ImportSnapshot = plan.Archive, Files = [new(plan.Archive.RootEntityId, entry.FileId, entry.Variant,
                entry.FileName, entry.ContentType, [], export.Files.Single().TemporaryPath)] };
            target.Faults.ThrowAt = StorageFaultPoint.BeforeCommit;
            await Assert.ThrowsAsync<IOException>(() => target.Writer.SaveAsync(batch, actor));
            await using var db = new KmsBusinessEntityDbContext(target.Options);
            Assert.Equal(0, await db.BusinessEntities.CountAsync()); Assert.Equal(0, await db.EntityFiles.CountAsync());
            Assert.Empty(Directory.EnumerateFiles(target.StorageRoot, "*.blob", SearchOption.AllDirectories));
            target.Faults.ThrowAt = null;
            await target.Writer.SaveAsync(batch, actor);
            Assert.Equal(1, await db.BusinessEntities.CountAsync());
            await Assert.ThrowsAsync<NotSupportedException>(() => target.Writer.SaveAsync(Batch() with { ImportSnapshot = plan.Archive with { FormatVersion = 1 } }, actor));
        }
        finally { source.Leases.Release(export.ExportId, actor); }
    }

    // Создаёт отдельную операцию сохранения тестового владельца.
    private static EntitySaveBatch Batch() => new(Guid.NewGuid(), "test", null);
    // Создаёт независимого доверенного автора тестовой операции.
    private static BusRequestContext Actor() => new(Guid.NewGuid(), Guid.NewGuid());
}

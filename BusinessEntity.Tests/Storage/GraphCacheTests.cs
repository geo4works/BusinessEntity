using System.Reactive.Linq;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Navigation;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Caching;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using BusinessEntity.MiniApps.TreeMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity = BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

// Проверяет RAM-проекции реального PostgreSQL storage и отделяет их от SQL прав пользователей.
public sealed class GraphCacheTests
{
    // После загрузки ни один структурный маршрут не делает SQL; копии не открывают состояние кэша наружу.
    [PostgresFact]
    public async Task WarmGraphUsesNoSqlAndReturnsIndependentCopies()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var actor = Actor();
        var root = Folder("root"); var folder = Folder("folder"); var doc = new Entity { Name = "Document", EntityType = BusinessEntityTypeEnum.Document };
        var relation = Edge(root.Id, folder.Id); var second = Edge(folder.Id, doc.Id);
        await fixture.Writer.SaveAsync(Batch() with { Entities = [root, folder, doc], Relations = [relation, second],
            Payloads = [new(doc.Id, 0, new Document { Name = doc.Name, Text = "content" })] }, actor);
        var before = fixture.Metrics.Reads;
        Assert.Equal(3, (await fixture.Reader.GetAllAsync()).Count);
        var copy = await fixture.Reader.GetByIdAsync(root.Id); copy!.Name = "client changed";
        var edgeCopy = await fixture.Reader.GetRelationByIdAsync(relation.Id); edgeCopy!.ObjectBId = Guid.NewGuid();
        Assert.Equal(2, (await fixture.Reader.GetRelationsAsync()).Count);
        Assert.Equal(folder.Id, (await fixture.Reader.GetRelationByIdAsync(relation.Id))!.ObjectBId);
        Assert.Equal("root", (await fixture.Reader.GetByIdAsync(root.Id))!.Name);
        Assert.Equal(folder.Id, (await fixture.Reader.QueryGraphAsync(GraphQueryKind.Children, root.Id, null, 100)).Entities.Single().Id);
        Assert.Equal(new[] { root.Id, folder.Id, doc.Id }, (await fixture.Reader.QueryGraphAsync(GraphQueryKind.PathToRoot, doc.Id, null, 100)).Entities.Select(x => x.Id));
        Assert.Equal(3, (await fixture.Reader.QueryGraphAsync(GraphQueryKind.Subgraph, root.Id, null, 100)).Entities.Count);
        Assert.Equal(doc.Id, (await fixture.Reader.QueryGraphAsync(GraphQueryKind.SearchName, root.Id, "DOC", 100)).Entities.Single().Id);
        Assert.Equal(before, fixture.Metrics.Reads);
        // Отдельный User DbContext выполняет настоящий SQL, который не выдаётся за промах кэша графа.
        await using var graph = new KmsBusinessEntityDbContext(fixture.Options);
        var usersOptions = new DbContextOptionsBuilder<UserMiniAppDbContext>().UseNpgsql(graph.Database.GetConnectionString()).Options;
        await using var users = new UserMiniAppDbContext(usersOptions);
        await UserMiniAppStorageSchema.InitializeAsync(users, true, default);
        Assert.Equal(0, await users.Users.CountAsync());
        Assert.Equal(before, fixture.Metrics.Reads);
        Assert.True((await fixture.Reader.GetStatisticsAsync()).GraphHits >= 9);
    }

    // Отказ не публикует кандидата; подписчик события видит уже новый снимок без дополнительного SQL.
    [PostgresFact]
    public async Task RollbackLeavesSnapshotAndCommitPublishesBeforeEvent()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var actor = Actor(); var entity = Folder("before");
        await fixture.Writer.SaveAsync(Batch() with { Entities = [entity] }, actor);
        var revision = fixture.Cache.Read().Revision;
        fixture.Faults.ThrowAt = StorageFaultPoint.BeforeCommit;
        await Assert.ThrowsAsync<IOException>(() => fixture.Writer.SaveAsync(Batch() with { Entities = [Folder("failed", entity.Id)] }, actor));
        Assert.Equal(revision, fixture.Cache.Read().Revision); Assert.Equal("before", (await fixture.Reader.GetByIdAsync(entity.Id))!.Name);
        var delivered = string.Empty; var deliveredRevision = 0L;
        using var subscription = fixture.Bus.Listen<EntityBatchCommitted>().Subscribe(_ =>
        {
            var snapshot = fixture.Cache.Read(); delivered = snapshot.Entities[entity.Id].Name; deliveredRevision = snapshot.Revision;
        });
        fixture.Faults.ThrowAt = null;
        await fixture.Writer.SaveAsync(Batch() with { Entities = [Folder("after", entity.Id)] }, actor);
        Assert.Equal("after", delivered); Assert.Equal(revision + 1, deliveredRevision);
        Assert.Equal("after", (await fixture.Reader.GetByIdAsync(entity.Id))!.Name);
    }

    // Размер, число entries и LRU проверяются фактическими SQL-промахами; runtime payload также выдаётся копией.
    [PostgresFact]
    public async Task PayloadCacheIsBoundedEvictsAndDoesNotShareRuntimeData()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(payloadBytes: 2000, payloadEntries: 1);
        var actor = Actor(); var first = new Entity { Name = "first", EntityType = BusinessEntityTypeEnum.Document };
        var second = new Entity { Name = "second", EntityType = BusinessEntityTypeEnum.Document };
        var huge = new Entity { Name = "huge", EntityType = BusinessEntityTypeEnum.Document };
        await fixture.Writer.SaveAsync(Batch() with { Entities = [first, second, huge], Payloads = [
            new(first.Id, 0, new Document { Name = first.Name, Text = "first" }),
            new(second.Id, 0, new Document { Name = second.Name, Text = "second" }),
            new(huge.Id, 0, new Document { Name = huge.Name, Text = new string('x', 5000) })] }, actor);
        var initial = fixture.Metrics.Reads;
        var data = Assert.IsType<Document>(await fixture.Reader.GetPayloadAsync(first.Id, null)); data.Text = "caller mutation";
        Assert.Equal(initial + 1, fixture.Metrics.Reads);
        Assert.Equal("first", Assert.IsType<Document>(await fixture.Reader.GetPayloadAsync(first.Id, null)).Text);
        Assert.Equal(initial + 1, fixture.Metrics.Reads);
        await fixture.Reader.GetPayloadAsync(second.Id, null); await fixture.Reader.GetPayloadAsync(first.Id, null);
        Assert.Equal(initial + 3, fixture.Metrics.Reads);
        var stats = await fixture.Reader.GetStatisticsAsync();
        Assert.Equal(1, stats.PayloadEntries); Assert.InRange(stats.PayloadBytes, 1, stats.PayloadLimitBytes);
        Assert.True(stats.PayloadEvictions >= 2); Assert.True(stats.PayloadHits >= 1);
        await fixture.Reader.GetPayloadAsync(huge.Id, null); await fixture.Reader.GetPayloadAsync(huge.Id, null);
        Assert.Equal(initial + 5, fixture.Metrics.Reads);
        Assert.Equal(1, (await fixture.Reader.GetStatisticsAsync()).PayloadEntries);
        await fixture.Writer.SaveAsync(Batch() with { Payloads = [new(first.Id, 1, new Document { Name = first.Name, Text = "saved change" })] }, actor);
        Assert.Equal("saved change", Assert.IsType<Document>(await fixture.Reader.GetPayloadAsync(first.Id, null)).Text);
        Assert.Equal("first", Assert.IsType<Document>(await fixture.Reader.GetPayloadAsync(first.Id, 1)).Text);
    }

    // Поздно завершившееся чтение не возвращает старую v1 после удаления и повторного создания того же ID.
    [PostgresFact]
    public async Task PayloadGenerationPreventsLateAttachAfterDeleteAndRecreate()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var actor = Actor(); var entity = Folder("old");
        await fixture.Writer.SaveAsync(Batch() with { Entities = [entity], Payloads = [new(entity.Id, 0, new Folder { Name = "old", Tag = "old" })] }, actor);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options);
        var row = await db.BusinessEntityDataItems.AsNoTracking().SingleAsync();
        var stale = new CachedPayload(row.Data, row.Name, row.Version, row.CreatedDate, row.LastModifiedDate);
        var oldGeneration = fixture.Cache.Read().Generations[entity.Id];
        await fixture.Writer.SaveAsync(Batch() with { DeleteEntities = [entity.Id] }, actor);
        await fixture.Writer.SaveAsync(Batch() with { Entities = [Folder("new", entity.Id)],
            Payloads = [new(entity.Id, 0, new Folder { Name = "new", Tag = "new" })] }, actor);
        fixture.Payloads.Put(new(entity.Id, 1, oldGeneration), stale);
        Assert.NotEqual(oldGeneration, fixture.Cache.Read().Generations[entity.Id]);
        Assert.Equal("new", Assert.IsType<Folder>(await fixture.Reader.GetPayloadAsync(entity.Id, null)).Tag);
    }

    // Дерево показывает читателю опубликованное имя статьи, актуальный общий Document и скрывает private.
    [PostgresFact]
    public async Task TreePermissionsFilterCachedProjectionAndPublishedNames()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var actor = Actor();
        var root = new Entity { Name = "space", EntityType = BusinessEntityTypeEnum.Space };
        var article = new Entity { Name = "published", EntityType = BusinessEntityTypeEnum.RichTextDocument, IsPublic = true };
        var doc = new Entity { Name = "plain", EntityType = BusinessEntityTypeEnum.Document, IsPublic = true };
        await fixture.Writer.SaveAsync(Batch() with { Entities = [root, article, doc], Relations = [Edge(root.Id, article.Id), Edge(root.Id, doc.Id)],
            Payloads = [new(article.Id, 0, new RichTextDocument { Name = article.Name }, [], []), new(doc.Id, 0, new Document { Name = doc.Name, Text = "plain" })],
            Publications = [new(article.Id, 1)] }, actor);
        await fixture.Writer.SaveAsync(Batch() with { Payloads = [new(article.Id, 1, new RichTextDocument { Name = "draft" }),
            new(doc.Id, 1, new Document { Name = "plain latest", Text = "latest" })] }, actor);
        var reader = Guid.NewGuid(); var permissions = new UserEffectivePermissions { UserId = reader, SpaceId = root.Id, CanViewPublished = true };
        var service = new TreeVisibilityService();
        var reads = fixture.Metrics.Reads;
        var projection = await fixture.Reader.QueryGraphAsync(GraphQueryKind.Subgraph, root.Id, null, 100);
        var visible = service.BuildVisibleChildren(root.Id, projection, reader, false, permissions, default);
        Assert.Equal("published", visible.Single(x => x.Entity.Id == article.Id).Entity.Name);
        Assert.Equal("plain latest", visible.Single(x => x.Entity.Id == doc.Id).Entity.Name);
        Assert.Equal("draft", (await fixture.Reader.GetByIdAsync(article.Id))!.Name);
        Assert.Empty(service.BuildVisibleChildren(root.Id, projection, reader, false, UserEffectivePermissions.Deny(reader, root.Id, false), default));
        Assert.Equal(reads, fixture.Metrics.Reads);
        var privateArticle = (await fixture.Reader.GetByIdAsync(article.Id))!; privateArticle.IsPublic = false;
        await fixture.Writer.SaveAsync(Batch() with { Entities = [privateArticle] }, actor);
        projection = await fixture.Reader.QueryGraphAsync(GraphQueryKind.Subgraph, root.Id, null, 100);
        Assert.Single(service.BuildVisibleChildren(root.Id, projection, reader, false, permissions, default));
    }

    // Создаёт отдельную тестовую сущность, сохраняя ID только для проверки повторного создания.
    private static Entity Folder(string name, Guid? id = null) => new() { Id = id ?? Guid.NewGuid(), Name = name, EntityType = BusinessEntityTypeEnum.Folder };
    // Возвращает независимый доверенный адрес автора.
    private static BusRequestContext Actor() => new(Guid.NewGuid(), Guid.NewGuid());
    // Каждая изменяющая проверка получает новый OperationId.
    private static EntitySaveBatch Batch() => new(Guid.NewGuid(), "cache-tests", null);
    // Строит единственное иерархическое ребро для структурных проекций.
    private static BusinessEntityRelation Edge(Guid parent, Guid child) => new() { ObjectAId = parent, ObjectBId = child, RelationType = "Contains" };
}

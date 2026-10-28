using BusinessEntity.MiniApps.DocumentsMiniApp.Registration;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Navigation;
using BusinessEntity.MiniApps.DataProviderMiniApp.Registration;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Registration;
using BusinessEntity.MiniApps.UserMiniApp.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessEntity.Tests.Storage;

// Проверяет весь путь public connector → общий transport → singleton writer → PostgreSQL.
public sealed class TransactionBusTests
{
    // Явный системный scope seed сохраняет пакет и получает устойчивый исход через тот же транспорт.
    [PostgresFact]
    public async Task PublicConnectorSavesAndReadsOutcomeOverBus()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync();
        var services = new ServiceCollection(); services.AddLogging(); services.AddBusinessEntityBus();
        services.AddDataProviderMiniApp();
        services.AddDocumentsMiniApp();
        services.AddSingleton<BusinessEntity.Core.Saving.IEntityArchivePolicy>(new BusinessEntity.Core.Saving.NoStoredReferencesArchivePolicy(global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, false));
        services.AddRichTextDocumentMiniApp();
        services.AddSingleton(fixture.Options); services.AddSingleton(fixture.Metrics);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Storage:RootPath"] = fixture.StorageRoot }).Build());
        services.AddScoped<IUserConnector, UserConnector>();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BusRequestScope>().MarkSystem();
        var connector = scope.ServiceProvider.GetRequiredService<IDataProviderConnector>();
        var batch = new EntitySaveBatch(Guid.NewGuid(), "test-seed", null)
            { Entities = [new() { Name = "via bus", EntityType = global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id }] };
        var result = await connector.SaveAsync(batch);
        var outcome = await connector.GetOperationOutcomeAsync(batch.OperationId);
        Assert.Equal(batch.OperationId, result.OperationId);
        Assert.Equal(result.CommittedAt, outcome!.CommittedAt);
        Assert.Null(outcome.AuthorUserId);
        var beforeGraph = await connector.GetCacheStatisticsAsync();
        Assert.Equal(batch.Entities[0].Id, (await connector.QueryGraphAsync(GraphQueryKind.All)).Entities.Single().Id);
        Assert.Equal(beforeGraph.StorageReads, (await connector.GetCacheStatisticsAsync()).StorageReads);
        Assert.Empty(await connector.GetPublishedEntityNamesAsync([batch.Entities[0].Id]));
        var entityId = batch.Entities[0].Id;
        var propertyId = Guid.NewGuid(); var commentId = Guid.NewGuid();
        await connector.SaveAsync(new(Guid.NewGuid(), "test", null)
        {
            Properties = [new(propertyId, entityId, EntityPropertyOwner.Entity, 999, "{}", "test")],
            Comments = [new(commentId, entityId, null, "{\"text\":\"comment\"}", DateTime.UtcNow)],
            Files = [new(entityId, "file", "original", "test.txt", "text/plain", [1, 2, 3])]
        });
        Assert.Equal(propertyId, (await connector.GetStoredPropertiesAsync(EntityPropertyOwner.Entity, entityId)).Single().Id);
        Assert.Equal(commentId, (await connector.GetStoredCommentsAsync(entityId)).Single().Id);
        Assert.True(File.Exists((await connector.GetStoredFileAsync(entityId, "file", "original"))!.PhysicalPath));
        var article = new BusinessEntity.Core.Classes.BusinessEntity { Name = "article", EntityType = global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id };
        var chunk = new BusinessEntityDataChunk { BusinessEntityId = article.Id, Blocks = [new BusinessEntityDataBlock { Html = "content" }] };
        await connector.SaveAsync(new(Guid.NewGuid(), "test", null)
        {
            Entities = [article], Payloads = [new(article.Id, 0, new RichTextDocument { Name = article.Name }, [chunk], [chunk.Id])]
        });
        var window = await connector.GetVersionChunkWindowAsync(article.Id, 1, 0, 5);
        Assert.Equal(1, window.DocumentVersion);
        Assert.Equal("content", window.Chunks.Single().Blocks.Single().Html);

    }
}

using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using Xunit;
namespace BusinessEntity.Tests.Modularity;

// Подтверждение статьи очищает только отправленные HTML, название и описание.
public sealed class RichTextWorkingSetRevisionTests
{
    // Поздние изменения остаются у автора; metadata результата относится к отправленному снимку.
    [Fact]
    public async Task AcknowledgementPreservesNewTitleHtmlAndDescription()
    {
        var context = new MiniAppSaveContext("RichTextDocumentMiniApp", Guid.NewGuid(), new BusRequestScope(), null!);
        var work = new RichTextWorkingSet(context, null!); var entityId = Guid.NewGuid();
        work.Open(entityId, "base", 1); work.SetTitle("sent"); work.SetDescription("sent description");
        work.Put(new() { ChunkId = Guid.NewGuid(), SortOrder = 0, CurrentHtml = "<p>sent zero</p>" });
        var changedChunk = Guid.NewGuid(); work.Put(new() { ChunkId = changedChunk, SortOrder = 1, CurrentHtml = "<p>sent one</p>" });
        var sent = work.Capture(Guid.NewGuid(), context.EditSessionId, context.Revision);
        // Асинхронная Prepare в production наполняет payload того же frozen пакета.
        sent = sent with { Payloads = [new(entityId, 1,
            new BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities.RichTextDocument { Id = entityId, Name = "sent" })] };
        work.SetTitle("later title"); work.SetDescription("later description");
        work.Put(new() { ChunkId = changedChunk, SortOrder = 1, CurrentHtml = "<p>later one</p>" });
        await work.AcknowledgeAsync(sent, new(sent.OperationId, DateTime.UtcNow, [new(entityId, 2, true)], [], [], [], []), context.Revision - 3, default);
        work.ClearConfirmedDescription();
        Assert.Equal("later title", work.Title); Assert.Equal("later description", work.Description);
        Assert.Equal("sent", work.ConfirmedTitle); Assert.Equal("sent description", work.ConfirmedDescription);
        Assert.Equal("<p>later one</p>", Assert.Single(work.Drafts).Value.CurrentHtml);
        Assert.Equal("<p>sent zero</p>", Assert.Single(work.AcknowledgedHtml).Value); Assert.Equal(2, work.BaseVersion);
    }
}

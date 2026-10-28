using BusinessEntity.MiniApps.BackupRestoreMiniApp.Internal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Xunit;
using Entity = BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

// Файловый формат проверяется независимо от доступности Docker и PostgreSQL.
public sealed class ArchiveDirectoryTests
{
    // Проверяет обновление backup после одного комментария и отсутствие повторной публикации.
    [Fact]
    public async Task CommentOnlyChangeUpdatesSnapshotAndRepeatedBackupIsUnchanged()
    {
        var root = Temporary();
        try
        {
            var directory = new GraphArchiveDirectory(); var export = Export();
            await directory.WriteAsync(root, export, default);
            var entity = export.Archive.Entities.Single();
            var now = DateTime.UtcNow;
            var changed = export with { Archive = export.Archive with { Entities = [entity with
                { Comments = [new(Guid.NewGuid(), entity.Card.Id, null, "comment", now, now)] }] } };
            var result = await directory.WriteAsync(root, changed, default);
            Assert.Equal(1, result.ChangedEntities); Assert.True(result.ManifestUpdated);
            Assert.Single((await directory.ReadAsync(root, default)).Entities.Single().Comments);
            Assert.False((await directory.WriteAsync(root, changed, default)).ManifestUpdated);
        }
        finally { Directory.Delete(root, true); }
    }

    // Проверяет читаемость прежнего manifest после отказа подготовки новых файлов.
    [Fact]
    public async Task FailedFilePublicationLeavesPreviousCompleteManifestReadable()
    {
        var root = Temporary(); var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var directory = new GraphArchiveDirectory(); var export = Export();
            await directory.WriteAsync(root, export, default);
            var previous = await File.ReadAllTextAsync(Path.Combine(root, "manifest.json"));
            await File.WriteAllBytesAsync(source, [9, 9]);
            var entity = export.Archive.Entities.Single();
            var digest = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2 }));
            var failed = export with { Archive = export.Archive with { Entities = [entity with
                { Files = [new("file", "original", "file", "text/plain", digest, 2, "files/source.blob")] }] },
                Files = [new("files/source.blob", source)] };
            await Assert.ThrowsAsync<InvalidDataException>(() => directory.WriteAsync(root, failed, default));
            Assert.Equal(previous, await File.ReadAllTextAsync(Path.Combine(root, "manifest.json")));
            Assert.Empty((await directory.ReadAsync(root, default)).Entities.Single().Files);
        }
        finally { Directory.Delete(root, true); File.Delete(source); }
    }

    // Проверяет явный отказ при отсутствии обязательной даты карточки.
    [Fact]
    public void MissingCardMetadataCannotBecomeFreshDefaultDate()
    {
        var options = GraphArchiveJson.CreateOptions();
        var card = JsonNode.Parse(JsonSerializer.Serialize(new Entity { Name = "card", EntityType = global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id }, options))!;
        card.AsObject().Remove("createdDate");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Entity>(card.ToJsonString(), options));
    }

    // Проверяет запрет выходящих за архив путей.
    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("files\\bad")]
    public void RejectsPathsOutsideArchive(string entry) => Assert.Throws<InvalidDataException>(() => GraphArchiveDirectory.Resolve(TemporaryRoot, entry));

    // Проверяет перепривязку относительных ссылок без изменения обычных GUID и внешних URL.
    [Fact]
    public void LinkRemapLeavesOrdinaryGuidTextUntouched()
    {
        var oldId = Guid.NewGuid(); var replacement = Guid.NewGuid();
        var rewriter = new EntityUrlRewriter([new("/document/")]);
        var text = $"text {oldId:D}; /document/{oldId:D}?v=2; /unrelated/{oldId:D}; https://external.example/document/{oldId:D}";
        var rewritten = rewriter.Rewrite(text, new Dictionary<Guid, Guid> { [oldId] = replacement });
        Assert.Equal($"text {oldId:D}; /document/{replacement:D}?v=2; /unrelated/{oldId:D}; https://external.example/document/{oldId:D}", rewritten);
    }

    private static readonly string TemporaryRoot = Path.Combine(Path.GetTempPath(), "be-archive-directory-tests");
    // Создаёт отдельный временный каталог тестового архива.
    private static string Temporary() { var path = Path.Combine(TemporaryRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    // Создаёт минимальный полный снимок пространства для файловых проверок.
    private static SubgraphExportResult Export()
    {
        var card = new Entity { Name = "space", EntityType = global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id };
        return new(Guid.NewGuid(), new(GraphArchiveFormat.CurrentVersion, card.Id, [new(card, [], [], [], [], [], [])], []), []);
    }
}

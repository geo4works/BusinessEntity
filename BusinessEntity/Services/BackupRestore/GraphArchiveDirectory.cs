using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;

namespace BusinessEntity.Services.BackupRestore;

// Публикует manifest последним: читатель видит только полный набор неизменяемых файлов архива.
internal sealed class GraphArchiveDirectory
{
    internal static readonly JsonSerializerOptions Json = GraphArchiveJson.CreateOptions(indented: true);
    // Записывает только изменённые entity snapshots; publication, comments и descriptions входят в digest.
    public async Task<ArchiveWriteResult> WriteAsync(string root, SubgraphExportResult export, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var manifestPath = Path.Combine(root, "manifest.json");
        ArchiveManifest? before = null;
        if (File.Exists(manifestPath)) before = await ReadJsonAsync<ArchiveManifest>(manifestPath, ct);
        if (before != null && (before.FormatVersion != GraphArchiveFormat.CurrentVersion || before.RootEntityId != export.Archive.RootEntityId))
            throw new InvalidDataException("Каталог занят другим пространством или старым форматом backup.");
        var entries = new List<ArchiveEntry>(); var changed = 0;
        foreach (var entity in export.Archive.Entities)
        {
            var name = string.Concat(entity.Card.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c));
            name = name.Trim().TrimEnd('.'); if (name.Length > 60) name = name[..60];
            var folder = $"entities/{entity.Card.EntityType}--{entity.Card.Id:D}--{name}";
            var stored = entity with { Files = entity.Files.Select(f => f with { ArchiveEntry = $"{folder}/files/{f.ContentHash}.blob" }).ToArray() };
            var text = JsonSerializer.Serialize(stored, Json); var hash = Digest(text);
            var entry = new ArchiveEntry(entity.Card.Id, $"{folder}/data/{hash}.json", hash);
            entries.Add(entry);
            foreach (var file in stored.Files)
            {
                var sourceEntry = entity.Files.Single(x => x.FileId == file.FileId && x.Variant == file.Variant).ArchiveEntry;
                var source = export.Files.Single(x => x.ArchiveEntry == sourceEntry).TemporaryPath;
                await CopyVerifiedAsync(source, Resolve(root, file.ArchiveEntry), file.ContentHash, file.Length, ct);
            }
            if (before?.Entities.Any(x => x.EntityId == entry.EntityId && x.ContentHash == hash) != true) changed++;
            await PublishTextAsync(Resolve(root, entry.RelativePath), text, ct);
        }
        var relations = JsonSerializer.Serialize(export.Archive.Relations, Json);
        var relationsHash = Digest(relations); var relationsPath = $"relations/{relationsHash}.json";
        await PublishTextAsync(Resolve(root, relationsPath), relations, ct);
        var relationsChanged = before?.RelationsHash != relationsHash;
        var updated = before == null || changed != 0 || relationsChanged || before.Entities.Count != entries.Count;
        if (updated)
            await PublishTextAsync(manifestPath, JsonSerializer.Serialize(new ArchiveManifest(GraphArchiveFormat.CurrentVersion,
                export.Archive.RootEntityId, DateTime.UtcNow, entries, relationsPath, relationsHash), Json), ct);
        return new(changed, relationsChanged, updated);
    }

    // Проверяет все digest до планирования restore; manifest неизвестной версии не интерпретируется.
    public async Task<GraphArchive> ReadAsync(string root, CancellationToken ct)
    {
        var manifest = await ReadJsonAsync<ArchiveManifest>(Path.Combine(root, "manifest.json"), ct);
        if (manifest.FormatVersion != GraphArchiveFormat.CurrentVersion) throw new NotSupportedException("Формат backup не поддерживается.");
        var entities = new List<ArchivedEntity>();
        foreach (var entry in manifest.Entities)
        {
            var text = await File.ReadAllTextAsync(Resolve(root, entry.RelativePath), ct);
            if (Digest(text) != entry.ContentHash) throw new InvalidDataException("Повреждены данные сущности backup.");
            var entity = JsonSerializer.Deserialize<ArchivedEntity>(text, Json) ?? throw new InvalidDataException("Пустая сущность backup.");
            if (entity.Card.Id != entry.EntityId) throw new InvalidDataException("Manifest ссылается на другую сущность.");
            foreach (var file in entity.Files) await VerifyFileAsync(Resolve(root, file.ArchiveEntry), file.ContentHash, file.Length, ct);
            entities.Add(entity);
        }
        var relationText = await File.ReadAllTextAsync(Resolve(root, manifest.RelationsPath), ct);
        if (Digest(relationText) != manifest.RelationsHash) throw new InvalidDataException("Повреждены отношения backup.");
        var relations = JsonSerializer.Deserialize<Core.Classes.BusinessEntityRelation[]>(relationText, Json)
            ?? throw new InvalidDataException("Нет отношений backup.");
        return new(manifest.FormatVersion, manifest.RootEntityId, entities, relations);
    }

    // Запрещает traversal и ссылки через symlink/reparse point внутри недоверенного архива.
    internal static string Resolve(string root, string entry)
    {
        if (Path.IsPathRooted(entry) || entry.Contains('\\')) throw new InvalidDataException("Недопустимый путь архива.");
        var fullRoot = Path.GetFullPath(root); var path = Path.GetFullPath(Path.Combine(fullRoot, entry));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Путь выходит за пределы архива.");
        for (var current = path; !string.Equals(current, fullRoot, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current)!)
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Ссылки файловой системы в архиве не допускаются.");
        return path;
    }
    // Не допускает использования повреждённой существующей копии или изменившегося временного источника.
    private static async Task CopyVerifiedAsync(string source, string destination, string hash, long length, CancellationToken ct)
    {
        if (File.Exists(destination)) { await VerifyFileAsync(destination, hash, length, ct); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                await input.CopyToAsync(output, ct);
            await VerifyFileAsync(temporary, hash, length, ct);
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    // Потоковый digest работает и для больших видео.
    private static async Task VerifyFileAsync(string path, string hash, long length, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length != length || Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)) != hash)
            throw new InvalidDataException("Повреждён файл backup.");
    }
    // Atomic rename сохраняет предыдущий manifest при отказе записи нового.
    private static async Task PublishTextAsync(string path, string text, CancellationToken ct)
    {
        if (File.Exists(path) && await File.ReadAllTextAsync(path, ct) == text) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, text, ct); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    // Читает обязательный JSON текущего формата без пустой подстановки.
    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken ct) =>
        JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new InvalidDataException("Пустой JSON backup.");
    // Вычисляет SHA256 точных UTF-8 bytes публикуемого JSON.
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    // Описывает единственный опубликованный snapshot сущности в manifest.
    private sealed record ArchiveEntry(Guid EntityId, string RelativePath, string ContentHash);
    // Связывает полное опубликованное состояние backup с проверяемыми digest.
    private sealed record ArchiveManifest(int FormatVersion, Guid RootEntityId, DateTime SavedAt,
        IReadOnlyList<ArchiveEntry> Entities, string RelationsPath, string RelationsHash);
}
// Фактические изменения опубликованного backup.
internal sealed record ArchiveWriteResult(int ChangedEntities, bool RelationsChanged, bool ManifestUpdated);

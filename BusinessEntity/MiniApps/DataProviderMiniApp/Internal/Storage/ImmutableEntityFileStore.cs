using System.Security.Cryptography;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

// Подготавливает неизменяемые объекты до коммита ссылок; убирает сироты только при достоверно доступной БД.
internal sealed class ImmutableEntityFileStore
{
    private readonly string _root;
    private readonly IStorageFaults _faults;
    private readonly ILogger<ImmutableEntityFileStore> _logger;
    // Постоянный storage задаётся явно; случайная папка внутри контейнера не используется.
    public ImmutableEntityFileStore(IConfiguration configuration, IStorageFaults faults, ILogger<ImmutableEntityFileStore> logger)
    {
        var root = configuration["Storage:RootPath"];
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Storage:RootPath обязателен.");
        _root = Path.GetFullPath(root); _faults = faults; _logger = logger;
    }

    // Фиксирует digest входа без загрузки большого временного видео целиком в память.
    public PreparedEntityFile Describe(EntityFileChange change)
    {
        if (change.EntityId == Guid.Empty || string.IsNullOrWhiteSpace(change.FileId) || string.IsNullOrWhiteSpace(change.Variant))
            throw new ArgumentException("Не задан логический адрес файла.");
        byte[] digest; long length;
        if (change.TemporaryFilePath != null)
        {
            if (change.Content.Length != 0 || !Path.IsPathFullyQualified(change.TemporaryFilePath))
                throw new ArgumentException("Допустим только один явно заданный источник файла.");
            using var stream = File.OpenRead(change.TemporaryFilePath);
            length = stream.Length; digest = SHA256.HashData(stream);
        }
        else { length = change.Content.LongLength; digest = SHA256.HashData(change.Content); }
        return new(change, Convert.ToHexString(digest), length);
    }

    // Сначала публикует проверенные bytes на диске, затем добавляет метаданные в ещё незакоммиченный DbContext.
    public async Task PrepareAsync(KmsBusinessEntityDbContext db, Guid operationId,
        IReadOnlyList<PreparedEntityFile> files, CancellationToken ct)
    {
        if (files.Select(f => (f.Change.EntityId, f.Change.FileId, f.Change.Variant)).Distinct().Count() != files.Count)
            throw new ArgumentException("Пакет содержит повторный логический файл.");
        foreach (var file in files)
        {
            var change = file.Change;
            var old = await db.EntityFiles.FindAsync([change.EntityId, change.FileId, change.Variant], ct);
            if (old != null && (old.ContentHash != file.Hash || old.FileName != change.FileName || old.ContentType != change.ContentType))
                throw new BusRequestFailedException(new("file.identity_conflict", "Постоянный файл неизменяем: используйте новый FileId."));
            var relative = Path.Combine("business-entities", change.EntityId.ToString("D"), "objects", file.Hash + ".blob");
            var destination = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                await using var existing = File.OpenRead(destination);
                if (Convert.ToHexString(await SHA256.HashDataAsync(existing, ct)) != file.Hash)
                    throw new InvalidDataException("Повреждён неизменяемый файловый объект.");
            }
            else
            {
                var staging = Path.Combine(_root, ".staging", operationId.ToString("N"));
                Directory.CreateDirectory(staging);
                var temporary = Path.Combine(staging, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        if (change.TemporaryFilePath != null)
                        {
                            await using var input = File.OpenRead(change.TemporaryFilePath);
                            await input.CopyToAsync(output, ct);
                        }
                        else await output.WriteAsync(change.Content, ct);
                        await output.FlushAsync(ct);
                    }
                    await using (var written = File.OpenRead(temporary))
                        if (Convert.ToHexString(await SHA256.HashDataAsync(written, ct)) != file.Hash)
                            throw new InvalidDataException("Источник файла изменился во время сохранения.");
                    _faults.Hit(StorageFaultPoint.BeforeFilePublish);
                    File.Move(temporary, destination);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            if (old == null) db.EntityFiles.Add(new EntityFileRow { EntityId = change.EntityId, FileId = change.FileId,
                Variant = change.Variant, FileName = change.FileName, ContentType = change.ContentType,
                ContentHash = file.Hash, RelativePath = relative, Length = file.Length });
        }
    }

    // Проверяет новые зависимости до коммита; SQL FK дополнительно защищает уже существующие версии.
    public async Task AssertVersionReferencesAsync(KmsBusinessEntityDbContext db, CancellationToken ct)
    {
        foreach (var reference in db.VersionFileReferences.Local)
        {
            var file = await db.EntityFiles.FindAsync([reference.FileEntityId, reference.FileId, reference.Variant], ct)
                ?? throw new BusRequestFailedException(new("file.reference_missing", "Содержимое ссылается на незаписанный файл."));
            ResolvePath(file);
        }
    }

    // Вызывается под блокировкой единственного писателя или до старта runtime; не трогает архивы бэкапов.
    public async Task RecoverAsync(DbContextOptions<KmsBusinessEntityDbContext> options, CancellationToken ct = default)
    {
        await using var db = new KmsBusinessEntityDbContext(options);
        // Если этот запрос не прошёл, ничего на диске не удаляется.
        var rows = await db.EntityFiles.AsNoTracking().ToListAsync(ct);
        var referenced = rows.Select(ResolvePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entitiesRoot = Path.Combine(_root, "business-entities");
        if (Directory.Exists(entitiesRoot))
            foreach (var directory in Directory.EnumerateDirectories(entitiesRoot))
            {
                var objects = Path.Combine(directory, "objects");
                if (!Directory.Exists(objects)) continue;
                foreach (var file in Directory.EnumerateFiles(objects, "*.blob"))
                    if (!referenced.Contains(Path.GetFullPath(file))) File.Delete(file);
            }
        var staging = Path.Combine(_root, ".staging");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
    }

    // Не позволяет метаданным БД превратить file-result в чтение произвольного пути.
    public string ResolvePath(EntityFileRow row)
    {
        if (row.ContentHash.Length != 64 || !row.ContentHash.All(Uri.IsHexDigit))
            throw new InvalidDataException("Неверный digest файлового объекта.");
        var expected = Path.Combine("business-entities", row.EntityId.ToString("D"), "objects", row.ContentHash + ".blob");
        if (row.RelativePath != expected) throw new InvalidDataException("Неверный путь файлового объекта.");
        var path = Path.Combine(_root, expected);
        if (!File.Exists(path)) throw new InvalidDataException("Постоянный файл не найден на диске.");
        return path;
    }

    // Отложенная уборка не маскирует исходную ошибку и никогда не объявляет откат уже принятого пакета.
    public async Task TryRecoverAsync(DbContextOptions<KmsBusinessEntityDbContext> options)
    {
        try { await RecoverAsync(options); }
        catch (Exception exception) { _logger.LogError(exception, "File recovery deferred; committed references remain authoritative."); }
    }
}

// Digest и замороженный источник подготовленного файла.
internal sealed record PreparedEntityFile(EntityFileChange Change, string Hash, long Length);

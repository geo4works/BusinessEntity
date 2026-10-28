using BusinessEntity.Core.DomainEntities;

namespace BusinessEntity.Services.BackupRestore;

// Общий алгоритм настроенных путей backup и отображения host bind mount.
public sealed class SpaceArchivePaths
{
    private readonly IConfiguration _configuration;
    // Получает явные настройки storage и отображения host-путей.
    public SpaceArchivePaths(IConfiguration configuration) => _configuration = configuration;
    public string StorageRoot => Path.GetFullPath(_configuration["Storage:RootPath"]
        ?? throw new InvalidOperationException("Storage:RootPath обязателен."));
    public string BackupRoot => Path.GetFullPath(Path.Combine(StorageRoot, _configuration["SpaceBackup:RootPath"] ?? "backups"));
    // Выбирает каталог backup и запрещает пересечение с рабочими данными.
    public string ForSpace(Guid id, GenericSpaceProperties settings)
    {
        var path = string.IsNullOrWhiteSpace(settings.BackupFolder) ? Path.Combine(BackupRoot, "spaces", $"Space--{id:D}") : ResolveInput(settings.BackupFolder);
        foreach (var reserved in new[] { StorageRoot, Path.Combine(StorageRoot, "business-entities"), Path.Combine(StorageRoot, ".staging"), Path.Combine(StorageRoot, "restore-reports") })
            if (path.Equals(reserved, StringComparison.OrdinalIgnoreCase) || IsInside(reserved, path) || (reserved != StorageRoot && IsInside(path, reserved)))
                throw new InvalidOperationException("Каталог backup пересекается с рабочим storage.");
        return path;
    }
    // Преобразует настроенный путь bind mount или относительный путь backup.
    public string ResolveInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Путь backup обязателен.");
        var external = value.Trim().Replace('\\', '/').TrimEnd('/');
        var host = _configuration["Storage:HostRootPath"]?.Trim().Replace('\\', '/').TrimEnd('/');
        if (!string.IsNullOrEmpty(host) && (external.Equals(host, StringComparison.OrdinalIgnoreCase) || external.StartsWith(host + "/", StringComparison.OrdinalIgnoreCase)))
            return Path.GetFullPath(Path.Combine(StorageRoot, external[host.Length..].TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(BackupRoot, value));
    }
    // Показывает путь хоста вместо container-пути внутри bind mount.
    public string Display(string path)
    {
        var host = _configuration["Storage:HostRootPath"];
        if (string.IsNullOrWhiteSpace(host) || !IsInside(path, StorageRoot)) return path;
        return host.TrimEnd('\\', '/') + (host.Contains('\\') ? "\\" : "/") + Path.GetRelativePath(StorageRoot, path).Replace('/', host.Contains('\\') ? '\\' : '/');
    }
    // Проверяет вложенность нормализованного пути с границей каталога.
    public static bool IsInside(string candidate, string root) => Path.GetFullPath(candidate).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

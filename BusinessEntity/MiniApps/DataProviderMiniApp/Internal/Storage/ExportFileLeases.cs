using System.Collections.Concurrent;
using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

// Управляет только временными копиями экспорта, не каноническим хранилищем и не Docker volumes.
internal sealed class ExportFileLeases
{
    private readonly ConcurrentDictionary<Guid, Lease> _leases = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "be-data-provider-exports");
    // Создаёт отдельный каталог подготовки и серверную привязку владельца.
    public (Guid Id, string Directory) Create(BusRequestContext actor)
    {
        foreach (var expired in _leases.Where(x => x.Value.ExpiresAt < DateTime.UtcNow).Select(x => x.Key)) Release(expired, null);
        var id = Guid.NewGuid(); var directory = Path.Combine(_root, id.ToString("N"));
        Directory.CreateDirectory(directory);
        _leases[id] = new(actor.UserId, actor.IsSystem, directory, DateTime.UtcNow.AddHours(1));
        return (id, directory);
    }
    // Освобождает только каталог, созданный этим сервисом для данного автора.
    public bool Release(Guid id, BusRequestContext? actor)
    {
        if (!_leases.TryGetValue(id, out var lease)) return false;
        if (actor != null && !actor.IsSystem && (lease.IsSystem || lease.UserId != actor.UserId)) throw new UnauthorizedAccessException();
        if (Directory.Exists(lease.Directory)) Directory.Delete(lease.Directory, true);
        return _leases.TryRemove(id, out _);
    }
    // До запуска runtime убирает только временные каталоги предшествующего процесса.
    public void RecoverAfterRestart()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var directory in Directory.EnumerateDirectories(_root))
            if (Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) Directory.Delete(directory, true);
    }
    // Lease хранит адрес владельца и срок только для временного вывода операции.
    private sealed record Lease(Guid? UserId, bool IsSystem, string Directory, DateTime ExpiresAt);
}

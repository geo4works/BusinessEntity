using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Modularity;
namespace BusinessEntity.Infrastructure.Saving;

// Три осознанных решения о незавершённом рабочем наборе окна.
public enum NavigationSaveDecision { Save, Discard, Cancel }
// Разрешение ухода отделено от успешности одной из нескольких операций сохранения.
public sealed record NavigationSaveResolution(bool CanLeave, string? Error = null);
// Общая механика guard вызывает владельца целого набора и заново проверяет его право.
public sealed class WindowSaveGuard(WindowModularityManager manager)
{
    // Не подтверждает переход, пока остаётся хотя бы одна новая или неподтверждённая правка.
    public async Task<NavigationSaveResolution> ResolveAsync(NavigationSaveDecision decision, CancellationToken ct = default)
    {
        if (decision == NavigationSaveDecision.Cancel) return new(false);
        await manager.WaitForPendingEditsAsync(ct);
        var contexts = manager.DirtyContexts;
        if (decision == NavigationSaveDecision.Discard && contexts.Any(x => x.OperationId.HasValue))
            return new(false, "Сначала проверьте исход начатого сохранения кнопкой «Сохранить».");
        try
        {
            foreach (var context in contexts)
            {
                if (decision == NavigationSaveDecision.Discard) { await context.DiscardAsync(ct); continue; }
                var owner = manager.Modules.Where(x => x.SaveContext == context)
                    .OrderByDescending(x => x.Definition.Placement == ModulePlacement.Page).FirstOrDefault(IsLive);
                if (owner == null) return new(false, "Владелец несохранённых правок закрыт. Вернитесь к редактору.");
                MiniAppSaveAttempt? result = null;
                await manager.ExecuteAsync(owner.ModuleId, async _ => result = await context.SaveAsync(owner.ModuleId, ct));
                if (result?.Success != true) return new(false, result?.Error ?? "Сохранение не завершено.");
            }
            await manager.WaitForPendingEditsAsync(ct);
            return manager.DirtyContexts.Count == 0 ? new(true) :
                new(false, "Во время операции появились новые правки. Сохраните их или останьтесь на странице.");
        }
        catch (Exception error) { return new(false, BusError.FromException(error).Message); }
    }
    // Неактивный адрес не используется для обхода guard и текущих прав доступа.
    private bool IsLive(MiniAppModule module)
    {
        try { return manager.GetLive(module.ModuleId) == module; }
        catch (Exception error) when (error is KeyNotFoundException or ObjectDisposedException) { return false; }
    }
}

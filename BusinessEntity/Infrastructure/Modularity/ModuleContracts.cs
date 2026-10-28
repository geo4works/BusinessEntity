using BusinessEntity.Infrastructure.Saving;

namespace BusinessEntity.Infrastructure.Modularity;

// Размещения приложения с переходами между страницами.
public enum ModulePlacement { Headless, ShellModuleHostArea, Page }
// Области уникальности экземпляров; пользовательский редактор никогда не AppSingleton.
public enum ModuleInstancePolicy { AppSingleton, WindowSingleton, PerContext, MultiInstance }
// Декларация владельца не содержит исполняемого UI или предметного состояния.
public sealed record ModuleDefinition(string MiniAppId, string ModuleDefinitionId, string Title,
    string? Icon, ModulePlacement Placement, ModuleInstancePolicy InstancePolicy,
    string? ComponentKey = null, string? HostArea = null, int Order = 0, bool OpenOnStartup = false,
    string? RequiredPermission = null, bool HasSaveContext = false)
{
    public string Key => MiniAppId + ":" + ModuleDefinitionId;
}
// Объявляет виды модулей; ответы принимаются и после первого открытия UI.
public sealed record GiveMeModuleDefinitions(Guid RequestId);
// Один ответ одного владельца с неизменяемыми значениями деклараций.
public sealed record HereAreModuleDefinitions(Guid RequestId, string MiniAppId, IReadOnlyList<ModuleDefinition> Definitions);
// Этап жизни адресован конкретному runtime-экземпляру.
public enum ModuleLifecycle { Opened, Activated, Deactivated, Closed }
// Общая шина несёт адрес и metadata без ссылок на компоненты.
public sealed record ModuleLifecycleChanged(Guid ModuleId, string MiniAppId, string DefinitionId,
    Guid LogicalWindowId, Guid? ContextId, Guid? EditSessionId, ModuleLifecycle Lifecycle);
// Модуль представляет рабочий экземпляр; dirty читается из его общего контекста.
public sealed record MiniAppModule(Guid ModuleId, ModuleDefinition Definition, Guid LogicalWindowId,
    Guid? ContextId, MiniAppSaveContext? SaveContext)
{
    public Guid? EditSessionId => SaveContext?.EditSessionId;
    public bool IsDirty => SaveContext?.IsDirty == true;
}
// Runtime фонового модуля отделён от декларативного host-service.
public interface IModuleRuntime
{
    // Выполняет старт владельца после завершения предыдущих startup-модулей.
    Task StartAsync(CancellationToken ct);
    // Завершает фоновые ресурсы перед освобождением scope.
    Task StopAsync(CancellationToken ct);
}
// Проверка права при каждом открытии модуля; скрытие ссылки её не заменяет.
public interface IModuleAccessPolicy
{
    // Возвращает доступ для текущей server identity и предметного контекста.
    Task<bool> CanOpenAsync(ModuleDefinition definition, Guid? contextId, CancellationToken ct);
}

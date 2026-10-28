using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.Infrastructure.Commands;

// Декларация команды/ссылки не содержит обработчиков или компонентов.
public sealed record ButtonDefinition(string MiniAppId, string CommandId, string HostId, string Title,
    string? Icon = null, int Order = 0, string? Group = null, string? RequiredPermission = null,
    string? TargetRoute = null, string? ExecutionMiniAppId = null, string CssClass = "btn btn-sm btn-outline-secondary", IReadOnlyList<string>? EntityTypes = null, string? Argument = null);
// Запрос деклараций известного места.
public sealed record GiveMeButtons(Guid RequestId, string HostId);
// Ответ одного владельца, включая пустой набор.
public sealed record HereAreButtons(Guid RequestId, string MiniAppId, string HostId, IReadOnlyList<ButtonDefinition> Buttons);
// Локально вычисленное владельцем состояние команды.
public sealed record ButtonState(bool Visible, bool Enabled, string? Title = null);
// Адресное состояние UI одного рабочего экземпляра.
public sealed record ButtonStateSnapshot(Guid LogicalWindowId, Guid ModuleId, Guid? EditSessionId,
    Guid? ContextId, string MiniAppId, Guid BindingId, IReadOnlyDictionary<string, ButtonState> States);
// Адрес устанавливается сервером; браузер не задаёт автора или получателя.
public sealed record ButtonCommandInvoked(Guid RequestId, string DeclarationOwner, string CommandId, Guid BindingId) : BusRequest(RequestId);
// Явное подтверждение выполнения или типизированный отказ.
public sealed record ButtonCommandResult(Guid RequestId, BusError? Error = null) : BusResponse(RequestId, Error);
// Регистрация декларации принадлежит владельцу.
public sealed record ButtonRegistration(ButtonDefinition Definition);

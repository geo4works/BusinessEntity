using System.Data.Common;

namespace BusinessEntity.Infrastructure.Bus;

// Стабильный типизированный отказ без раскрытия внутренних ошибок хранилища.
public sealed record BusError(string Code, string Message)
{
    // Преобразует исключение сервиса в публичную причину отказа.
    public static BusError FromException(Exception exception) => exception switch
    {
        BusRequestFailedException failure => failure.Error,
        PublicOperationException failure => new(failure.Code, failure.Message),
        UnauthorizedAccessException => new("access.denied", "Недостаточно прав для операции."),
        ArgumentException => new("validation.failed", exception.Message),
        KeyNotFoundException => new("record.not_found", "Запрошенный объект не найден."),
        NotSupportedException => new("payload.unsupported", "Формат данных не поддерживается."),
        System.Text.Json.JsonException or InvalidDataException => new("payload.invalid", "Данные повреждены или имеют неизвестный формат."),
        _ when HasDatabaseFailure(exception) => new("storage.unavailable", "Хранилище недоступно. Повторите операцию позже."),
        OperationCanceledException => new("operation.cancelled", "Операция отменена."),
        _ => new("operation.failed", "Не удалось выполнить операцию. Подробности записаны в журнал.")
    };

    // EF может оборачивать отказ драйвера; технические детали всё равно остаются только в журнале.
    private static bool HasDatabaseFailure(Exception exception)
    {
        for (Exception? cause = exception; cause != null; cause = cause.InnerException)
            if (cause is DbException) return true;
        return false;
    }
}

// Адрес и доверенный серверный автор запроса в пределах одного scope.
public sealed record BusRequestContext(Guid ScopeId, Guid? UserId,
    Guid LogicalWindowId = default, Guid? ModuleId = null, Guid? EditSessionId = null,
    Guid? ContextId = null, bool IsSystem = false, bool HasApplicationAdminAuthority = false, long AuthEpoch = 0,
    BusExecutionKind ExecutionKind = BusExecutionKind.Ui, Guid? ApiSessionId = null,
    string? ApiClientId = null, Guid? ActivationPermit = null,Guid? ApiOperationId=null,string? ApiRequestHash=null,DateTime? ApiAccessExpiresAt=null);

// База предметных запросов; контекст устанавливается клиентом перед отправкой.
public abstract record BusRequest(Guid RequestId)
{
    public BusRequestContext Context { get; init; } = new(Guid.Empty, null);
}

// База предметных ответов с единым контрактом отказа.
public abstract record BusResponse(Guid RequestId, BusError? Error)
{
    public BusRequestContext Context { get; init; } = new(Guid.Empty, null);
}

// Отказ обработчика сохраняет стабильный код для вызывающего слоя.
public sealed class BusRequestFailedException : Exception
{
    public BusError Error { get; }

    // Сохраняет типизированный отказ вместо разбора текста исключения.
    public BusRequestFailedException(BusError error) : base(error.Message) => Error = error;
}

// Ограничивает ожидание и сохраняет корреляцию для диагностики.
public sealed class BusRequestTimeoutException : TimeoutException
{
    public Guid RequestId { get; }

    // Сообщает об истечении ожидания без утверждения об откате записи.
    public BusRequestTimeoutException(Guid requestId, TimeSpan timeout)
        : base($"Не получен ответ на запрос {requestId} за {timeout.TotalSeconds:g} с.")
        => RequestId = requestId;
}

// Ожидаемый предметный отказ сохраняет стабильный код при проходе через шину.
public class PublicOperationException(string code,string message) : Exception(message)
{
    public string Code {get;}=code;
}

// Собственный обработчик миниаппа подключается инфраструктурой, а не коннектором.
public interface IBusEndpoint
{
    // Обеспечивает однократное подключение подписок текущего экземпляра.
    void EnsureSubscribed();
}

// HTTP и фоновые операции не являются логическими окнами пользовательского интерфейса.
public enum BusExecutionKind { Ui, Api, Background }

// Техническая политика приёма транзакции внедряется инфраструктурой без знания User в storage.
public interface ITransactionAdmission
{
    // Lease удерживается до установления результата записи, не только до отправки запроса.
    IDisposable Enter(BusRequestContext actor);
}

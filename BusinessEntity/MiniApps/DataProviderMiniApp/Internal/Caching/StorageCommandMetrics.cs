using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Caching;

// Считает реальные команды EF DataProvider, не перехватывая отдельный DbContext пользователей.
internal sealed class StorageCommandMetrics : DbCommandInterceptor
{
    private long _reads, _writes;
    public long Reads => Interlocked.Read(ref _reads);
    public long Writes => Interlocked.Read(ref _writes);
    // Классифицирует команду по первому оператору, включая Reader-результат INSERT RETURNING.
    private void Record(DbCommand command)
    {
        if (command.CommandText.AsSpan().TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref _reads);
        else Interlocked.Increment(ref _writes);
    }
    // Перехватывает синхронный SQL reader, используемый также проверками событий.
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Record(command); return result; }
    // Перехватывает асинхронное чтение и DML с RETURNING.
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Record(command); return ValueTask.FromResult(result); }
    // Считает синхронный scalar-запрос.
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    { Record(command); return result; }
    // Считает асинхронный scalar-запрос.
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { Record(command); return ValueTask.FromResult(result); }
    // Считает синхронную команду без результата.
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Record(command); return result; }
    // Считает асинхронные DDL и команды без результата.
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Record(command); return ValueTask.FromResult(result); }
}

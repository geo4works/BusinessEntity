using Npgsql;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Одна установка обслуживается одним процессом; offline recovery не конкурирует с работающим сервером.
internal sealed class IdentityHostLease(IConfiguration configuration) : IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private const long LockId = 4775302520876581961;

    // Session advisory lock освобождается PostgreSQL и при аварийном завершении процесса.
    public async Task AcquireAsync(CancellationToken ct)
    {
        if (_connection != null) throw new InvalidOperationException("Identity host lease уже получен.");
        // Advisory lock нельзя вернуть в pool вместе с живой серверной сессией.
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DockerConnection"))
            { Pooling = false, KeepAlive = 30 }.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lock)", connection);
            command.Parameters.AddWithValue("lock", LockId);
            if (await command.ExecuteScalarAsync(ct) is not true)
                throw new InvalidOperationException("Эта БД уже обслуживается другим процессом BusinessEntity. Перед identity-recover остановите сервер.");
            _connection = connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    // Закрытие выделенного соединения снимает только собственный lock.
    public async ValueTask DisposeAsync()
    { if (_connection != null) { await _connection.DisposeAsync(); _connection = null; } }
}

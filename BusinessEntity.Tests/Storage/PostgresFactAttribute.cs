using Xunit;

namespace BusinessEntity.Tests.Storage;

// Интеграционные проверки запускаются только с явно заданным отдельным PostgreSQL.
public sealed class PostgresFactAttribute : FactAttribute
{
    // Не выдаёт отсутствие тестовой БД за успешно выполненную проверку.
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BE_TEST_POSTGRES")))
            Skip = "Нужна отдельная PostgreSQL в BE_TEST_POSTGRES.";
    }
}

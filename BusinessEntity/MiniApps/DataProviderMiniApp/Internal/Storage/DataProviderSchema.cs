using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

// Создаёт только актуальную пустую схему и явно отвергает другую установленную схему.
internal sealed class DataProviderSchema
{
    public const int CurrentVersion = 2;
    private readonly DbContextOptions<KmsBusinessEntityDbContext> _options;
    // Получает конфигурацию своего storage без подключения к пользовательской сессии.
    public DataProviderSchema(DbContextOptions<KmsBusinessEntityDbContext> options) => _options = options;
    // Возвращает true только для впервые созданной схемы, чтобы композиционный корень создал свои таблицы User.
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = new KmsBusinessEntityDbContext(_options);
        var creator = context.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken)) await creator.CreateAsync(cancellationToken);
        if (!await creator.HasTablesAsync(cancellationToken))
        {
            await creator.CreateTablesAsync(cancellationToken);
            context.StorageSchema.Add(new StorageSchemaRow { Id = 1, Version = CurrentVersion });
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        try
        {
            var metadata = await context.StorageSchema.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
            if (metadata?.Version != CurrentVersion)
                throw new InvalidDataException("Несовместимая схема DataProvider. Требуется пустая БД текущей версии.");
        }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == "42P01")
        {
            throw new InvalidDataException("В БД отсутствует версия схемы DataProvider. Старые данные не поддерживаются.", exception);
        }
        return false;
    }
}

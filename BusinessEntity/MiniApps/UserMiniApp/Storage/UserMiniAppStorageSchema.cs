using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessEntity.MiniApps.UserMiniApp.Storage;

// Создаёт текущие собственные таблицы User только при первичной установке общей БД.
internal static class UserMiniAppStorageSchema
{
    private const int CurrentVersion = 1;
    // Существующая несовместимая схема даёт отказ; миграции и backfill отсутствуют.
    public static async Task InitializeAsync(UserMiniAppDbContext context, bool freshDatabase, CancellationToken ct)
    {
        if (freshDatabase)
        {
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
            context.SchemaMetadata.Add(new UserStorageVersionRow { Id = 1, Version = CurrentVersion });
            await context.SaveChangesAsync(ct);
            return;
        }
        try
        {
            var metadata = await context.SchemaMetadata.SingleOrDefaultAsync(x => x.Id == 1, ct);
            if (metadata?.Version != CurrentVersion) throw new InvalidDataException("Несовместимая схема UserMiniApp.");
        }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == "42P01")
        {
            throw new InvalidDataException("Отсутствует версия схемы UserMiniApp. Требуется пустая БД актуального формата.", exception);
        }
    }
}

// Метаданные только своей схемы пользователей и ролей.
internal sealed class UserStorageVersionRow
{
    public int Id { get; set; }
    public int Version { get; set; }
}

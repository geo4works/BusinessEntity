using System.Data.Common;
using System.Text.Json;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Единственное разрешённое расширение служебного storage: User пишет свои таблицы в транзакции DataProvider.
internal sealed class UserOwnedStorageWriter : IOwnedServiceStorageWriter
{
    public string MiniAppId => "UserMiniApp";
    // Бизнес-граф и OperationOutcomes остаются у DataProvider; User получает только открытую SQL-транзакцию.
    public async Task ApplyAsync(OwnedServiceStorageChange change, BusRequestContext actor, DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        if (change.Kind != "UserFormsV1") throw new NotSupportedException("Неизвестный пакет служебных форм User.");
        var changes = JsonSerializer.Deserialize<UserTableMutation[]>(change.Data, GraphArchiveJson.CreateOptions()) ?? throw new InvalidDataException();
        var options = new DbContextOptionsBuilder<UserMiniAppDbContext>().UseNpgsql((NpgsqlConnection)connection).Options;
        await using var db = new UserMiniAppDbContext(options); await db.Database.UseTransactionAsync(transaction, ct);
        var isAdmin = actor.IsSystem || actor.HasApplicationAdminAuthority || await IsGlobalAdminAsync(db, actor.UserId, ct);
        foreach (var mutation in changes)
        {
            if (!isAdmin && !await IsOwnProfileAsync(db, mutation, actor.UserId, ct)) throw new UnauthorizedAccessException();
            switch (mutation.TypeKey)
            {
                case nameof(UserDto): await ApplyAsync<UserDto>(db, mutation, ct); break;
                case nameof(UserPropertyDto): await ApplyAsync<UserPropertyDto>(db, mutation, ct); break;
                case nameof(UserRoleDto): await ApplyAsync<UserRoleDto>(db, mutation, ct); break;
                case nameof(UserGroupDto): await ApplyAsync<UserGroupDto>(db, mutation, ct); break;
                case nameof(UserGroupMemberDto): await ApplyAsync<UserGroupMemberDto>(db, mutation, ct); break;
                case nameof(UserRoleAssignmentDto): await ApplyAsync<UserRoleAssignmentDto>(db, mutation, ct); break;
                default: throw new NotSupportedException("Неизвестная служебная таблица User.");
            }
        }
        await db.SaveChangesAsync(ct);
    }
    // Upsert выполняется в той же транзакции, без отдельного commit репозитория.
    private static async Task ApplyAsync<T>(UserMiniAppDbContext db, UserTableMutation mutation, CancellationToken ct) where T : class
    {
        var existing = await db.Set<T>().FindAsync([mutation.Id], ct);
        if (mutation.Data == null) { if (existing != null) db.Remove(existing); return; }
        var value = JsonSerializer.Deserialize<T>(mutation.Data, GraphArchiveJson.CreateOptions()) ?? throw new InvalidDataException();
        if ((Guid)typeof(T).GetProperty("Id")!.GetValue(value)! != mutation.Id) throw new InvalidDataException("Id служебной записи изменён.");
        if (existing == null) db.Add(value); else db.Entry(existing).CurrentValues.SetValues(value);
    }
    // Обычный пользователь может менять свою запись профиля, сохраняя внешнюю identity и системный статус.
    private static async Task<bool> IsOwnProfileAsync(UserMiniAppDbContext db, UserTableMutation mutation, Guid? userId, CancellationToken ct)
    {
        if (mutation.TypeKey != nameof(UserDto) || mutation.Id != userId || mutation.Data == null) return false;
        var existing = await db.Set<UserDto>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == mutation.Id, ct);
        var updated = JsonSerializer.Deserialize<UserDto>(mutation.Data, GraphArchiveJson.CreateOptions());
        return existing != null && updated != null && existing.ExternalId == updated.ExternalId &&
            UserValueRules.IsSystemUser(existing) == UserValueRules.IsSystemUser(updated);
    }
    // Проверяет глобальные назначения напрямую в той же SQL-транзакции, без самозапроса User по шине.
    private static async Task<bool> IsGlobalAdminAsync(UserMiniAppDbContext db, Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return false;
        var groups = (await db.Set<UserGroupMemberDto>().Where(x => x.UserId == userId.Value).ToArrayAsync(ct)).Select(x => x.GroupId).ToHashSet();
        var assignments = await db.Set<UserRoleAssignmentDto>().Where(x => x.SpaceId == Guid.Empty || x.Subject == UserRoleAssignmentSubjects.AllSpaces).ToArrayAsync(ct);
        var ids = assignments.Where(x => UserValueRules.IsRoleAssignmentApplicableToUser(x, userId.Value, groups)).Select(x => x.RoleId).ToHashSet();
        var roles = await db.Set<UserRoleDto>().Where(x => ids.Contains(x.Id)).ToArrayAsync(ct);
        return roles.Any(x => UserValueRules.ParsePermissionCodes(x.Permissions).Contains(UserRolePermissionCodes.GlobalAdmin));
    }
}

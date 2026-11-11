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
        if (change.Kind is not ("UserFormsV1" or "UserIdentityV1")) throw new NotSupportedException("Неизвестный пакет служебных форм User.");
        var changes = JsonSerializer.Deserialize<UserTableMutation[]>(change.Data, GraphArchiveJson.CreateOptions()) ?? throw new InvalidDataException();
        var options = new DbContextOptionsBuilder<UserMiniAppDbContext>().UseNpgsql((NpgsqlConnection)connection).Options;
        await using var db = new UserMiniAppDbContext(options); await db.Database.UseTransactionAsync(transaction, ct);
        var isAdmin = actor.IsSystem || await IsGlobalAdminAsync(db, actor.UserId, ct);
        foreach (var mutation in changes)
        {
            if (!isAdmin && !await IsOwnProfileAsync(db, mutation, actor.UserId, ct)) throw new UnauthorizedAccessException();
            switch (mutation.TypeKey)
            {
                case nameof(IdentityProfileRevisionRow): await ApplyAsync<IdentityProfileRevisionRow>(db, mutation, ct); break;
                case nameof(IdentityProfileRow): await ApplyAsync<IdentityProfileRow>(db, mutation, ct); break;
                case nameof(IdentityInstallationRow): await ApplyAsync<IdentityInstallationRow>(db, mutation, ct); break;
                case nameof(ExternalIdentityLinkRow): await ApplyAsync<ExternalIdentityLinkRow>(db, mutation, ct); break;
                case nameof(IdentitySwitchPlanRow): await ApplyAsync<IdentitySwitchPlanRow>(db, mutation, ct); break;
                case nameof(ApiAccessGrantRow): await ApplyAsync<ApiAccessGrantRow>(db, mutation, ct); break;
                case nameof(ApiSessionRow): await ApplyAsync<ApiSessionRow>(db, mutation, ct); break;
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
        if (mutation.Data == null)
        {
            if(existing is IdentityInstallationRow)throw new InvalidOperationException("Identity installation нельзя удалить.");
            if(existing is IdentityProfileRow profile && (await db.IdentityInstallation.AnyAsync(x=>x.ActiveProviderId==profile.Id,ct)
                ||await db.ExternalIdentities.AnyAsync(x=>x.ProviderId==profile.Id,ct)
                ||await db.IdentitySwitchPlans.AnyAsync(x=>x.SourceProviderId==profile.Id||x.TargetProviderId==profile.Id,ct)))
                throw new InvalidOperationException("Использованный профиль нельзя удалить.");
            if (existing != null) db.Remove(existing); return;
        }
        var value = JsonSerializer.Deserialize<T>(mutation.Data, GraphArchiveJson.CreateOptions()) ?? throw new InvalidDataException();
        if ((Guid)typeof(T).GetProperty("Id")!.GetValue(value)! != mutation.Id) throw new InvalidDataException("Id служебной записи изменён.");
        if (existing is IdentityProfileRow current && value is IdentityProfileRow updated
            && !((updated.Revision == current.Revision && updated.Json == current.Json) || updated.Revision == current.Revision + 1))
            throw new InvalidOperationException("identity.revision_conflict");
        if(existing is ApiAccessGrantRow oldGrant && value is ApiAccessGrantRow newGrant
            &&(newGrant.Revision!=oldGrant.Revision+1||newGrant.UserId!=oldGrant.UserId||newGrant.ClientId!=oldGrant.ClientId))
            throw new PublicOperationException("api.grant_conflict","Допуск изменён; обновите сохранённую ревизию.");
        if (existing is IdentityProfileRevisionRow oldRevision && value is IdentityProfileRevisionRow newRevision && oldRevision.Json != newRevision.Json)
            throw new InvalidOperationException("Ревизия профиля неизменяема.");
        if (existing is IdentityInstallationRow oldSelection && value is IdentityInstallationRow newSelection && newSelection.Epoch != oldSelection.Epoch + 1)
            throw new InvalidOperationException("identity.activation_conflict");
        if (existing == null) db.Add(value); else db.Entry(existing).CurrentValues.SetValues(value);
    }
    // Обычный пользователь может менять свою запись профиля, сохраняя внешнюю identity и системный статус.
    private static async Task<bool> IsOwnProfileAsync(UserMiniAppDbContext db, UserTableMutation mutation, Guid? userId, CancellationToken ct)
    {
        if(mutation.TypeKey==nameof(UserPropertyDto))
        {
            if(!userId.HasValue)return false;
            var old=await db.UserProperties.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==mutation.Id,ct);
            if(old!=null&&(old.ParentEntityId!=userId||!OwnPreferenceType(old.PropertyType)))return false;
            if(mutation.Data==null)return old!=null;
            var value=JsonSerializer.Deserialize<UserPropertyDto>(mutation.Data,GraphArchiveJson.CreateOptions());
            return value!=null&&value.ParentEntityId==userId&&OwnPreferenceType(value.PropertyType)&&(old==null||old.PropertyType==value.PropertyType);
        }
        if (mutation.TypeKey != nameof(UserDto) || mutation.Id != userId || mutation.Data == null) return false;
        var existing = await db.Set<UserDto>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == mutation.Id, ct);
        var updated = JsonSerializer.Deserialize<UserDto>(mutation.Data, GraphArchiveJson.CreateOptions());
        return existing != null && updated != null && existing.Login == updated.Login && existing.NormalizedLogin == updated.NormalizedLogin &&
            existing.Kind == updated.Kind && existing.Enabled == updated.Enabled &&
            UserValueRules.IsSystemUser(existing) == UserValueRules.IsSystemUser(updated);
    }
    private static bool OwnPreferenceType(int type)=>type is (int)UserPropertyTypeEnum.DocPrintPresets or (int)UserPropertyTypeEnum.RichDocDisplayedLevelProperty;
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

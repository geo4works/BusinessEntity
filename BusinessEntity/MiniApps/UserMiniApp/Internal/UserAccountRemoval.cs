using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Удаляет локальную учётную запись и её служебные зависимости в одной SQL-транзакции User.
internal sealed class UserAccountRemoval(DbContextOptions<UserMiniAppDbContext> options)
{
    // User владеет своими таблицами; записи бизнес-графа этим сервисом не затрагиваются.
    public async Task DeleteAsync(Guid userId, CancellationToken ct)
    {
        await using var db = new UserMiniAppDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Set<UserPropertyDto>().Where(x => x.ParentEntityId == userId).ExecuteDeleteAsync(ct);
        await db.Set<UserGroupMemberDto>().Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Set<UserRoleAssignmentDto>().Where(x => x.SubjectId == userId && x.AssignmentType == UserRoleAssignmentTypes.UserToRole).ExecuteDeleteAsync(ct);
        await db.Set<UserDto>().Where(x => x.Id == userId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

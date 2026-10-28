using BusinessEntity.MiniApps.UserMiniApp.Internal;
using System.Linq.Expressions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Repositories.EfPostgres;

// Базовый EF/Postgres repository user mini-app поверх собственного DbContext.
public abstract class UserMiniAppEfRepositoryBase<T> : IUserMiniAppRepository<T> where T : class
{
    private readonly DbContextOptions<UserMiniAppDbContext> _options;

    protected UserMiniAppEfRepositoryBase(DbContextOptions<UserMiniAppDbContext> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        await using var context = new UserMiniAppDbContext(_options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        IQueryable<T> query = context.Set<T>();
        if (UserMutationCapture.Current is { } capture)
        {
            var values = capture.Overlay(await query.ToListAsync(ct));
            return filter == null ? values : values.Where(filter.Compile()).ToArray();
        }
        if (filter != null)
        {
            query = query.Where(filter);
        }

        return await query.ToListAsync(ct);
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (UserMutationCapture.Current is not null)
            return (await GetAllAsync(null, ct)).SingleOrDefault(x => (Guid)typeof(T).GetProperty("Id")!.GetValue(x)! == id);
        await using var context = new UserMiniAppDbContext(_options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return await context.Set<T>().FindAsync(new object?[] { id }, ct);
    }

    public async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        if (UserMutationCapture.Current is { } capture) { capture.Upsert(entity); return entity; }
        await using var context = new UserMiniAppDbContext(_options);
        await context.Set<T>().AddAsync(entity, ct);
        await context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        if (UserMutationCapture.Current is { } capture) { capture.Upsert(entity); return; }
        await using var context = new UserMiniAppDbContext(_options);
        context.Entry(entity).State = EntityState.Modified;
        await context.SaveChangesAsync(ct);
    }

    // Атомарное удаление допускает повтор из другого окна без гонки между Find и Delete.
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (UserMutationCapture.Current is { } capture) { capture.Delete<T>(id); return; }
        await using var context = new UserMiniAppDbContext(_options);
        await context.Set<T>().Where(entity => EF.Property<Guid>(entity, "Id") == id).ExecuteDeleteAsync(ct);
    }
}

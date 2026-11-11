using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Читает только служебные таблицы User; административные изменения фиксирует общим SQL writer.
internal sealed class IdentityStateStore(DbContextOptions<UserMiniAppDbContext> options,
    BusRequestClient client, BusRequestScope scope, IdentityMutationCoordinator mutations)
{
    internal static readonly Guid InstallationId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    // Один технический gate сериализует административные изменения этого владельца.
    public Task<T> ExclusiveAsync<T>(Func<Task<T>> action,CancellationToken ct) => mutations.RunAsync(action,ct);
    // Каждый вызов получает отдельный контекст, не разделяемый соседними HTTP-запросами.
    public async Task<T> ReadAsync<T>(Func<UserMiniAppDbContext, Task<T>> read)
    { await using var db = new UserMiniAppDbContext(options); return await read(db); }
    // Все связанные административные строки и outcome попадают в одну транзакцию DataProvider.
    public async Task WriteAsync(Guid actorId, IEnumerable<object> rows, Guid? operationId = null, CancellationToken ct = default)
    {
        scope.SetUser(actorId);
        var mutations = rows.Select(value => new UserTableMutation(value.GetType().Name,
            (Guid)value.GetType().GetProperty("Id")!.GetValue(value)!, JsonSerializer.Serialize(value, value.GetType(), GraphArchiveJson.CreateOptions()))).ToArray();
        await WriteMutationsAsync(actorId, mutations, operationId, ct);
    }
    // Удаление собственных служебных строк проходит тот же writer и durable outcome.
    internal async Task WriteMutationsAsync(Guid actorId, IReadOnlyList<UserTableMutation> mutations, Guid? operationId, CancellationToken ct)
    {
        scope.SetUser(actorId);
        var batch = new EntitySaveBatch(operationId ?? Guid.NewGuid(), "UserMiniApp", null)
        { OwnedServiceChanges = [new("UserMiniApp", "UserIdentityV1", JsonSerializer.Serialize(mutations, GraphArchiveJson.CreateOptions()))] };
        await client.SendAsync<SaveEntityBatchRequest, SaveEntityBatchResponse>(new(Guid.NewGuid(), batch), ct);
    }
    // Только новая пустая установка получает исходный профиль из серверной конфигурации.
    public async Task InitializeAsync(IConfiguration configuration, IdentityRuntime runtime, IdentityProfileValidation validation, CancellationToken ct)
    {
        await using var db = new UserMiniAppDbContext(options);
        if (!await db.IdentityInstallation.AnyAsync(ct))
        {
            var profile = configuration.GetSection("Identity:InitialProvider").Get<IdentityProviderProfile>()
                ?? throw new InvalidOperationException("Новая установка требует Identity:InitialProvider.");
            if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Issuer)) throw new InvalidDataException("Неполный исходный профиль identity.");
            if(configuration["Identity:Bootstrap:AdministratorIssuer"]!=profile.Issuer
                ||string.IsNullOrWhiteSpace(configuration["Identity:Bootstrap:AdministratorSubject"]))
                throw new InvalidOperationException("Новая установка требует pinned issuer/subject первого администратора.");
            // Ошибка конфигурации не должна навсегда фиксировать неработоспособный первый профиль.
            await validation.ValidateAsync(profile,ct);
            db.IdentityProfiles.Add(new() { Id = profile.Id, Revision = profile.Revision, Json = JsonSerializer.Serialize(profile, Json) });
            db.IdentityProfileRevisions.Add(new() { Id = Guid.NewGuid(), ProviderId = profile.Id, Revision = profile.Revision, Json = JsonSerializer.Serialize(profile, Json) });
            db.IdentityInstallation.Add(new() { Id = InstallationId, ActiveProviderId = profile.Id, ActiveRevision = profile.Revision, Epoch = 1 });
            await db.SaveChangesAsync(ct);
        }
        await LoadAsync(runtime, ct);
    }
    // Recovery загружает только сохранённый выбор и никогда не bootstrap-ит новую установку.
    public async Task LoadAsync(IdentityRuntime runtime, CancellationToken ct)
    {
        await using var db = new UserMiniAppDbContext(options);
        foreach (var row in await db.IdentityProfileRevisions.ToArrayAsync(ct))
            await runtime.AddAsync(JsonSerializer.Deserialize<IdentityProviderProfile>(row.Json, Json) ?? throw new InvalidDataException());
        var state = await db.IdentityInstallation.SingleAsync(ct);
        runtime.Activate(state.ActiveProviderId, state.ActiveRevision, state.Epoch);
    }
}

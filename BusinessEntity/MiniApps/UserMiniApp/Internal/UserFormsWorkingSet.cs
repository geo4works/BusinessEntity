using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Формы одного рабочего экземпляра User сохраняются одним служебным пакетом и имеют общий dirty.
internal sealed class UserFormsWorkingSet(MiniAppSaveContext context, UserAccountsService accounts,
    UserRoleCatalogService roles, UserGroupService groups) : IMiniAppSaveParticipant
{
    private readonly Dictionary<(UserFormKind, Guid), Entry> _entries = new();
    private readonly Dictionary<(UserFormKind, Guid), object> _results = new();
    private Captured[] _captured = [];
    private IReadOnlyList<Action> _postCommit = [];
    // Открытие нового UI не заменяет уже существующий черновик данного объекта.
    public T Open<T>(UserFormKind kind, Guid id, T model, object? result = null) where T : class
    {
        var key = (kind, id);
        if (_entries.TryGetValue(key, out var entry) && (entry.Current != entry.Saved || context.OperationId.HasValue)) return (T)entry.Model;
        var json = JsonSerializer.Serialize(model);
        _entries[key] = new(model, typeof(T), json, json);
        if (result != null) _results[key] = result;
        return model;
    }
    // Снимок формы обновляется при вводе; пароль остаётся только в памяти владельца до подготовки Authentik.
    public void Changed(UserFormKind kind, Guid id)
    {
        if (!_entries.TryGetValue((kind, id), out var entry)) return;
        var json = JsonSerializer.Serialize(entry.Model, entry.Type);
        if (entry.Current == json) return;
        entry.Current = json; context.MarkChanged();
    }
    // Фиксирует значения всех изменённых форм до любого сетевого запроса.
    public EntitySaveBatch Capture(Guid operationId, Guid editSessionId, long revision)
    {
        _captured = _entries.Where(x => x.Value.Current != x.Value.Saved)
            .Select(x => new Captured(x.Key.Item1, x.Key.Item2, x.Value.Type, x.Value.Current)).ToArray();
        return new(operationId, "UserMiniApp", editSessionId);
    }
    // Существующие предметные правила готовят DTO, репозитории собирают изменения вместо отдельных commit.
    public async Task<EntitySaveBatch> PrepareAsync(EntitySaveBatch captured, CancellationToken ct)
    {
        using var mutations = new UserMutationCapture();
        foreach (var item in _captured)
        {
            object result = item.Kind switch
            {
                UserFormKind.Profile => await accounts.UpdateProfileAsync(Read<UserProfileUpdateRequest>(item), ct),
                UserFormKind.Account => await accounts.UpdateAdministrationUserAsync(item.Id, Read<UserAdministrationSaveRequest>(item), ct),
                UserFormKind.Role => await roles.UpdateRoleAsync(item.Id, Read<UserRoleSaveRequest>(item), ct),
                UserFormKind.Group => await groups.UpdateUserGroupAsync(item.Id, Read<UserGroupSaveRequest>(item), ct),
                UserFormKind.Members => await groups.UpdateUserGroupMembershipsAsync(item.Id,
                    new() { UserIds = Read<List<UserGroupMembershipRecord>>(item).Where(x => x.IsMember).Select(x => x.UserId).ToList() }, ct),
                _ => throw new NotSupportedException()
            };
            _results[(item.Kind, item.Id)] = result;
        }
        _postCommit = mutations.PostCommit.ToArray();
        // Storage DTO не содержит введённых паролей; они никогда не попадают в пакет и OperationOutcomes.
        return captured with { OwnedServiceChanges = [new("UserMiniApp", "UserFormsV1",
            JsonSerializer.Serialize(mutations.Changes, GraphArchiveJson.CreateOptions()))] };
    }
    // Подтверждает только отправленные формы и удаляет уже использованные секреты из памяти.
    public Task AcknowledgeAsync(EntitySaveBatch sent, SaveBatchResult result, long revision, CancellationToken ct)
    {
        foreach (var item in _captured)
        {
            var entry = _entries[(item.Kind, item.Id)];
            var sentModel = JsonSerializer.Deserialize(item.Json, item.Type)!;
            ClearConfirmedPasswords(entry.Model, sentModel); ClearConfirmedPasswords(sentModel, sentModel);
            entry.Current = JsonSerializer.Serialize(entry.Model, entry.Type);
            entry.Saved = JsonSerializer.Serialize(sentModel, item.Type);
        }
        foreach (var action in _postCommit) action();
        _postCommit = []; _captured = []; return Task.CompletedTask;
    }
    // Восстанавливает все формы вместе; подготовленная неизвестная операция защищена общим контекстом.
    public Task DiscardAsync(CancellationToken ct)
    {
        foreach (var entry in _entries.Values)
        { entry.Model = JsonSerializer.Deserialize(entry.Saved, entry.Type)!; entry.Current = entry.Saved; }
        _captured = []; return Task.CompletedTask;
    }
    // После успеха UI получает значения предметного результата без повторного сохранения.
    public T Result<T>(UserFormKind kind, Guid id) => (T)_results[(kind, id)];
    // Сырой снимок имеет точный тип формы и не разделяет mutable object с UI.
    private static T Read<T>(Captured item) => JsonSerializer.Deserialize<T>(item.Json)!;
    // Более новый пароль не очищается ответом предыдущей операции.
    private static void ClearConfirmedPasswords(object live, object sent)
    {
        if (live is UserAdministrationSaveRequest account && sent is UserAdministrationSaveRequest savedAccount && account.Password == savedAccount.Password) account.Password = string.Empty;
        if (live is UserProfileUpdateRequest profile && sent is UserProfileUpdateRequest saved)
        {
            if (profile.OldPassword == saved.OldPassword) profile.OldPassword = string.Empty;
            if (profile.NewPassword == saved.NewPassword) profile.NewPassword = string.Empty;
            if (profile.RepeatPassword == saved.RepeatPassword) profile.RepeatPassword = string.Empty;
        }
    }
    // Только внутренний владелец содержит изменяемые формы и строки со временными секретами.
    private sealed class Entry(object model, Type type, string current, string saved)
    { public object Model = model; public Type Type = type; public string Current = current; public string Saved = saved; }
    // Снимок операции не зависит от последующих событий renderer.
    private sealed record Captured(UserFormKind Kind, Guid Id, Type Type, string Json);
}
// Виды собственных форм User не передаются другим миниаппам.
internal enum UserFormKind { Profile, Account, Role, Group, Members }
// Factory связывает собственные предметные сценарии с контекстом, которым владеет менеджер.
internal sealed class UserFormsWorkingSetFactory(UserAccountsService accounts, UserRoleCatalogService roles, UserGroupService groups)
{
    // Все компоненты одного edit session получают тот же рабочий экземпляр.
    public UserFormsWorkingSet Get(MiniAppModule module)
    {
        var save = module.SaveContext ?? throw new InvalidOperationException("У формы User нет контекста сохранения.");
        return save.GetOrCreateParticipant(() => new UserFormsWorkingSet(save, accounts, roles, groups));
    }
}

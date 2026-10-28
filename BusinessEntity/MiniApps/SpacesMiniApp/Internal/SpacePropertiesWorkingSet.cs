using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Internal;

// Рабочий набор форм настройки пространства принадлежит модулю управления; dirty самого пространства не вводится.
public sealed class SpacePropertiesWorkingSet(MiniAppSaveContext context, SpaceHelper spaces, IUserConnector users) : IMiniAppSaveParticipant
{
    private readonly Dictionary<Guid, GenericSpaceProperties> _current = new();
    private readonly Dictionary<Guid, string> _saved = new();
    private Dictionary<Guid, string> _captured = new();
    private readonly Dictionary<Guid, string> _names = new(), _savedNames = new();
    private Dictionary<Guid, string> _sentNames = new();
    // Открытая форма продолжает существующий локальный черновик.
    public GenericSpaceProperties Open(Guid id, GenericSpaceProperties settings)
    {
        if (_current.TryGetValue(id, out var draft)) return draft;
        _saved[id] = JsonSerializer.Serialize(settings); return _current[id] = Copy(settings);
    }
    // Переименование входит в тот же рабочий набор модуля управления.
    public string OpenName(Guid id, string name)
    { if (_names.TryGetValue(id, out var current)) return current; _savedNames[id] = name; return _names[id] = name; }
    // Ввод названия отмечает общую dirty ревизию.
    public void SetName(Guid id, string name)
    { if (!_names.TryGetValue(id, out var old) || old != name) { _names[id] = name; context.MarkChanged(); } }
    // Отмена отдельной команды не сбрасывает другие формы этого рабочего набора.
    public void CancelName(Guid id) { if (_savedNames.TryGetValue(id, out var name)) _names[id] = name; }
    // Владелец сообщает изменение общего контекста вместо второго флага dirty.
    public void Changed() => context.MarkChanged();
    // Все изменённые строки копируются синхронно до проверки прав и чтения storage.
    public EntitySaveBatch Capture(Guid operationId, Guid editSessionId, long revision)
    {
        _sentNames = _names.Where(x => x.Value != _savedNames.GetValueOrDefault(x.Key)).ToDictionary(x => x.Key, x => x.Value);
        _captured = _current.Where(x => JsonSerializer.Serialize(x.Value) != _saved[x.Key]).ToDictionary(x => x.Key, x => JsonSerializer.Serialize(x.Value));
        return new(operationId, "SpacesMiniApp", editSessionId);
    }
    // Права и состав свойств проверяются заново на сервере для всего сохраняемого набора.
    public async Task<EntitySaveBatch> PrepareAsync(EntitySaveBatch captured, CancellationToken ct)
    {
        var properties = new List<EntityPropertyChange>();
        foreach (var (id, json) in _captured)
        {
            var user = await users.GetCurrentUserAsync(ct);
            var permissions = await users.GetCurrentUserPermissionsForSpaceAsync(id, ct);
            if (user?.IsAkadmin != true && user?.IsGeneralAdmin != true && !permissions.CanAdminSpace && !permissions.CanGlobalAdmin)
                throw new UnauthorizedAccessException();
            properties.Add(await spaces.PrepareGenericSpacePropertiesAsync(id, JsonSerializer.Deserialize<GenericSpaceProperties>(json)!, ct));
        }
        var entities = new List<BusinessEntity.Core.Classes.BusinessEntity>();
        foreach (var (id, name) in _sentNames)
        {
            var permissions = await users.GetCurrentUserPermissionsForSpaceAsync(id, ct);
            var user = await users.GetCurrentUserAsync(ct);
            if (user?.IsAkadmin != true && user?.IsGeneralAdmin != true && !permissions.CanAdminSpace && !permissions.CanGlobalAdmin) throw new UnauthorizedAccessException();
            var space = await spaces.GetSpaceByIdAsync(id) ?? throw new KeyNotFoundException("Пространство удалено.");
            space.Name = SpaceHelper.NormalizeName(name); entities.Add(space);
        }
        return captured with { Properties = properties, Entities = entities };
    }
    // Подтверждает только сохранённые значения, оставляя более новые настройки в current.
    public Task AcknowledgeAsync(EntitySaveBatch sent, SaveBatchResult result, long revision, CancellationToken ct)
    {
        foreach (var (id, json) in _captured) _saved[id] = json;
        foreach (var entity in sent.Entities)
        { _savedNames[entity.Id] = entity.Name; if (_names[entity.Id] == _sentNames[entity.Id]) _names[entity.Id] = entity.Name; }
        _captured.Clear(); _sentNames.Clear(); return Task.CompletedTask;
    }
    // Discard возвращает весь набор к последнему подтверждённому состоянию.
    public Task DiscardAsync(CancellationToken ct)
    { foreach (var (id, json) in _saved) _current[id] = JsonSerializer.Deserialize<GenericSpaceProperties>(json)!; foreach (var (id, name) in _savedNames) _names[id] = name; return Task.CompletedTask; }
    // Копия не разделяет редактируемые ссылки с общим кэшем чтения.
    private static GenericSpaceProperties Copy(GenericSpaceProperties value) => JsonSerializer.Deserialize<GenericSpaceProperties>(JsonSerializer.Serialize(value))!;
}
// Регистрирует один предметный набор на edit session модуля управления.
public sealed class SpacePropertiesWorkingSetFactory(SpaceHelper spaces, IUserConnector users)
{
    // Экземпляр и общий контекст создаёт менеджер; factory лишь подключает владельца.
    public SpacePropertiesWorkingSet Get(MiniAppModule module)
    { var save = module.SaveContext!; return save.GetOrCreateParticipant(() => new SpacePropertiesWorkingSet(save, spaces, users)); }
}

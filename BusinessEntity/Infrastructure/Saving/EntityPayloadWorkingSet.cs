using System.Text.Json;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Types;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.Infrastructure.Saving;

// Общая механика рабочего набора payload; предметные формы используют собственные типы и правила.
public sealed class EntityPayloadWorkingSet(MiniAppSaveContext context, EntityTypeRegistry types) : IMiniAppSaveParticipant
{
    private readonly Dictionary<Guid, BusinessEntityData> _current = new();
    private readonly Dictionary<Guid, BusinessEntityData> _saved = new();
    // Возвращает рабочую копию, которая переживает пересоздание формы своего миниаппа.
    public T Open<T>(T data) where T : BusinessEntityData
    {
        if (_current.TryGetValue(data.Id, out var current) && (context.IsDirty || data.Version < current.Version)) return (T)current;
        _saved[data.Id] = Copy(data); return (T)(_current[data.Id] = Copy(data));
    }
    // Возвращает рабочий payload без раскрытия предметных типов общему слою.
    public T Get<T>(Guid id) where T : BusinessEntityData => (T)_current[id];
    // Завершённое удаление исключает объект из последующих пакетов рабочего набора.
    public void Forget(Guid id) { _current.Remove(id); _saved.Remove(id); }
    // Форме не требуется собственный флаг dirty.
    public void MarkChanged() => context.MarkChanged();
    // Все сущности рабочего набора сохраняются одним пакетом от имени владельца.
    public EntitySaveBatch Capture(Guid operationId, Guid editSessionId, long revision) =>
        new(operationId, context.MiniAppId, editSessionId)
        { Payloads = _current.Values.Where(data => JsonSerializer.Serialize(data, data.GetType()) != JsonSerializer.Serialize(_saved[data.Id], data.GetType()))
                .Select(data => new EntityPayloadChange(data.Id, data.Version, Copy(data))).ToArray() };
    // Подтверждает только отправленные значения; более новые изменения остаются в current.
    public Task AcknowledgeAsync(EntitySaveBatch sent, SaveBatchResult result, long revision, CancellationToken ct)
    {
        foreach (var payload in sent.Payloads)
        {
            var version = result.Versions.Single(x => x.EntityId == payload.EntityId).Version;
            var saved = Copy((BusinessEntityData)payload.Data); saved.Version = version; _saved[payload.EntityId] = saved;
            _current[payload.EntityId].Version = version;
        }
        return Task.CompletedTask;
    }
    // Discard восстанавливает последний подтверждённый набор целиком.
    public Task DiscardAsync(CancellationToken ct)
    { foreach (var (id, saved) in _saved) _current[id] = Copy(saved); return Task.CompletedTask; }
    // Конкретную модель знает только описатель владельца.
    private BusinessEntityData Copy(BusinessEntityData data) => types.Get(data.EntityType).Clone(data);
}

using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Types;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using DocumentData = BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Internal;

// Рабочий экземпляр держит весь набор документов до Save; UI не является хранилищем черновика.
public sealed class DocumentsWorkingSet(MiniAppSaveContext context, EntityTypeRegistry types, IUserConnector users) : IMiniAppSaveParticipant
{
    private readonly Dictionary<Guid, DocumentData> _current = new();
    private readonly Dictionary<Guid, DocumentData> _saved = new();
    private readonly Dictionary<Guid, string> _sentNames = new();
    public bool IsEditing { get; set; }
    // Открывает документ только один раз, сохраняя текущий черновик при пересоздании компонента.
    public DocumentData Open(DocumentData data)
    {
        if (_current.TryGetValue(data.Id, out var current) && (context.IsDirty || IsEditing || data.Version < current.Version)) return current;
        _saved[data.Id] = Copy(data); return _current[data.Id] = Copy(data);
    }
    // Фиксирует изменение формы в общем dirty рабочего экземпляра.
    public void Update(Guid id, string title, string text)
    {
        var value = _current[id];
        if (value.Name == title && value.Text == text) return;
        value.Name = title; value.Text = text; context.MarkChanged();
    }
    // Копирует полный набор payload до первого await.
    public EntitySaveBatch Capture(Guid operationId, Guid editSessionId, long revision)
    {
        _sentNames.Clear();
        var payloads = _current.Values.Where(data => data.Name.Trim() != _saved[data.Id].Name || data.Text != _saved[data.Id].Text)
            .Select(data =>
            {
                var copy = Copy(data); _sentNames[data.Id] = data.Name; copy.Name = copy.Name.Trim();
                if (copy.Name.Length == 0) throw new ArgumentException("Название документа обязательно.");
                return new EntityPayloadChange(copy.Id, copy.Version, copy);
            }).ToArray();
        return new(operationId, "DocumentsMiniApp", editSessionId) { Payloads = payloads };
    }
    // Save/guard проверяет права всего набора заново, даже если прежняя кнопка уже скрыта.
    public async Task<EntitySaveBatch> PrepareAsync(EntitySaveBatch captured, CancellationToken ct)
    {
        foreach (var payload in captured.Payloads)
            if (!(await users.GetEntityContentAccessAsync(payload.EntityId, ct)).CanEditDraft) throw new UnauthorizedAccessException("Нет прав на редактирование документа.");
        return captured;
    }
    // Подтверждает только отправленную версию; новый текст текущего черновика остаётся на месте.
    public Task AcknowledgeAsync(EntitySaveBatch sent, SaveBatchResult result, long revision, CancellationToken ct)
    {
        foreach (var payload in sent.Payloads)
        {
            var version = result.Versions.Single(x => x.EntityId == payload.EntityId).Version;
            var saved = Copy((DocumentData)payload.Data); saved.Version = version; _saved[saved.Id] = saved;
            _current[saved.Id].Version = version;
            if (_current[saved.Id].Name == _sentNames[saved.Id]) _current[saved.Id].Name = saved.Name;
        }
        IsEditing = context.Revision != revision; return Task.CompletedTask;
    }
    // Возвращает все формы к последнему подтверждённому состоянию.
    public Task DiscardAsync(CancellationToken ct)
    {
        foreach (var (id, saved) in _saved) _current[id] = Copy(saved);
        IsEditing = false; return Task.CompletedTask;
    }
    // Повторно открывает текущее состояние после Save или Discard.
    public DocumentData Get(Guid id) => _current[id];
    // Типоспецифичную копию делает зарегистрированный владельцем описатель.
    private DocumentData Copy(DocumentData data) => (DocumentData)types.Get(data.EntityType).Clone(data);
}

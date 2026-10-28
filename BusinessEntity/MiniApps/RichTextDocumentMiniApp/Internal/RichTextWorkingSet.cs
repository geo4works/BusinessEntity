using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Один рабочий экземпляр статьи сохраняет название, все изменённые чанки и их файлы вместе.
public sealed class RichTextWorkingSet(MiniAppSaveContext context, RichTextEditService editor) : IMiniAppSaveParticipant
{
    public Dictionary<long, RichTextEditorDraft> Drafts { get; } = new();
    public HashSet<long> DirtySortOrders { get; } = new();
    public Guid EntityId { get; private set; }
    public int BaseVersion { get; set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsEditing { get; set; }
    private string _acknowledgedDescription = string.Empty;
    private string _savedTitle = string.Empty;
    private Captured? _captured;
    public string ConfirmedTitle => _savedTitle;
    public string ConfirmedDescription => _acknowledgedDescription;
    public RichTextDocumentSaveResult? LastSave { get; private set; }
    public IReadOnlyDictionary<long, string> AcknowledgedHtml { get; private set; } = new Dictionary<long, string>();
    public IReadOnlyList<long> AcknowledgedSortOrders { get; private set; } = [];
    // Инициализация не перезаписывает уже существующий черновик после пересоздания UI.
    public void Open(Guid entityId, string title, int version = 0)
    {
        if (EntityId != Guid.Empty && EntityId != entityId) throw new InvalidOperationException("Чужая статья в рабочем экземпляре.");
        if (EntityId == Guid.Empty) { EntityId = entityId; Title = _savedTitle = title; }
        if ((BaseVersion == 0 || (!context.IsDirty && !IsEditing)) && version > 0)
        { BaseVersion = version; Title = _savedTitle = title; }
    }
    // Изменение названия отслеживается тем же контекстом, что и содержимое.
    public void SetTitle(string title)
    { if (Title != title) { Title = title; context.MarkChanged(); } }
    // Описание входит в пакет, но не увеличивает версию само по себе.
    public void SetDescription(string description)
    { if (Description != description) { Description = description; context.MarkChanged(); } }
    // Очистка поля после успеха не является новой предметной правкой; поздний ввод остаётся на месте.
    public void ClearConfirmedDescription() { if (Description == _acknowledgedDescription) Description = string.Empty; }
    // JS сообщает правку сразу; конкретный HTML захватывается отдельно.
    public void MarkChunkChanged(long position)
    { DirtySortOrders.Add(position); context.MarkChanged(); }
    // Черновик хранится у владельца, а не в viewport-компоненте.
    public void Put(RichTextEditorDraft draft)
    {
        if (!Drafts.TryGetValue(draft.SortOrder, out var previous) || previous.CurrentHtml != draft.CurrentHtml)
            context.MarkChanged();
        Drafts[draft.SortOrder] = draft; DirtySortOrders.Add(draft.SortOrder);
    }
    // Синхронно фиксирует сырой полный снимок до конвертации и запросов storage.
    public EntitySaveBatch Capture(Guid operationId, Guid editSessionId, long revision)
    {
        _captured = new(BaseVersion, RichTextTitleRules.NormalizeRichTextDocumentTitle(Title), Description,
            Drafts.Values.OrderBy(x => x.SortOrder).Select(x => new RichTextDocumentChunkEditDraft
            { ChunkId = x.ChunkId, SortOrder = x.SortOrder, Html = x.CurrentHtml }).ToArray());
        return new(operationId, "RichTextDocumentMiniApp", editSessionId);
    }
    // Асинхронная подготовка использует только ранее захваченные значения автора.
    public Task<EntitySaveBatch> PrepareAsync(EntitySaveBatch captured, CancellationToken ct)
    {
        var draft = _captured ?? throw new InvalidOperationException("Нет снимка редактора.");
        return editor.PrepareEditorBatchAsync(captured.OperationId, captured.EditSessionId!.Value, EntityId,
            draft.BaseVersion, draft.Title, draft.Description, draft.Drafts, ct);
    }
    // Удаляет только подтверждённые HTML; новые правки никогда не очищает результатом старой операции.
    public Task AcknowledgeAsync(EntitySaveBatch sent, SaveBatchResult result, long revision, CancellationToken ct)
    {
        var captured = _captured ?? throw new InvalidOperationException("Нет снимка для подтверждения.");
        var saved = result.Versions.Single(x => x.EntityId == EntityId);
        var clean = new List<long>();
        foreach (var draft in captured.Drafts)
            if (Drafts.TryGetValue(draft.SortOrder, out var live) && live.CurrentHtml == draft.Html)
            { Drafts.Remove(draft.SortOrder); DirtySortOrders.Remove(draft.SortOrder); clean.Add(draft.SortOrder); }
        AcknowledgedSortOrders = clean; AcknowledgedHtml = captured.Drafts.Where(x => clean.Contains(x.SortOrder)).ToDictionary(x => x.SortOrder, x => x.Html); BaseVersion = saved.Version;
        LastSave = new(saved.Version, saved.CreatedVersion ? sent.Payloads.Single().ChangedChunks?.Count ?? 0 : 0,
            saved.CreatedVersion, _savedTitle != captured.Title);
        _savedTitle = captured.Title; _acknowledgedDescription = captured.Description; _captured = null; return Task.CompletedTask;
    }
    // Отбрасывает весь рабочий набор статьи после проверки неизвестного исхода в общем контексте.
    public Task DiscardAsync(CancellationToken ct)
    {
        Drafts.Clear(); DirtySortOrders.Clear(); Title = _savedTitle; Description = string.Empty;
        IsEditing = false; _captured = null; return Task.CompletedTask;
    }
    // Значения исходной авторской правки не разделяют изменяемые ссылки с UI.
    private sealed record Captured(int BaseVersion, string Title, string Description, IReadOnlyList<RichTextDocumentChunkEditDraft> Drafts);
}
// Данные черновика являются внутренней моделью редактора владельца.
public sealed class RichTextEditorDraft
{
    public Guid ChunkId { get; init; }
    public long SortOrder { get; init; }
    public string OriginalHtml { get; init; } = string.Empty;
    public string CurrentHtml { get; init; } = string.Empty;
}
// Возвращает один объект владельца для всех UI одного edit session.
public sealed class RichTextWorkingSetFactory(RichTextEditService editor)
{
    // Создаёт рабочий набор только через сохранение, которым владеет менеджер модулей.
    public RichTextWorkingSet Get(MiniAppModule module)
    {
        var save = module.SaveContext ?? throw new InvalidOperationException("У статьи нет контекста сохранения.");
        return save.GetOrCreateParticipant(() => new RichTextWorkingSet(save, editor));
    }
}

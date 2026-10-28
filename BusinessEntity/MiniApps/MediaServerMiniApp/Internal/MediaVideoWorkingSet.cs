using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Types;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.Entities;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

// Метаданные всех изменяемых видео образуют один рабочий набор библиотеки.
public sealed class MediaVideoWorkingSet(MiniAppSaveContext context, MediaServerService media, EntityTypeRegistry types) : IMiniAppSaveParticipant
{
    private readonly EntityPayloadWorkingSet _payloads = new(context, types);
    // Загружает полные metadata один раз и сохраняет черновики при обновлении списка загрузок.
    public async Task OpenAsync(IEnumerable<MediaVideoInfo> videos, CancellationToken ct = default)
    { foreach (var video in videos) { ct.ThrowIfCancellationRequested(); _payloads.Open(await media.GetEditableVideoDataAsync(video.Id, ct)); } }
    // UI работает с принадлежащим владельцу payload, а не с собственной копией dirty.
    public MediaVideo Get(Guid id) => _payloads.Get<MediaVideo>(id);
    // Название и комментарий попадут в один payload одной транзакции.
    public void SetName(Guid id, string name)
    { var data = Get(id); if (data.DisplayName != name) { data.DisplayName = data.Name = name; _payloads.MarkChanged(); } }
    // Комментарий относится к тому же рабочему набору всех видео библиотеки.
    public void SetComment(Guid id, string comment)
    { var data = Get(id); if (data.Comment != comment) { data.Comment = comment; _payloads.MarkChanged(); } }
    // Удалённое завершённой операцией видео не возвращается следующим Save metadata.
    public void Forget(Guid id) => _payloads.Forget(id);
    // Фиксирует и проверяет полный набор метаданных до await.
    public EntitySaveBatch Capture(Guid operationId, Guid editSessionId, long revision)
    {
        var captured = _payloads.Capture(operationId, editSessionId, revision);
        foreach (var payload in captured.Payloads)
        {
            var data = (MediaVideo)payload.Data; var name = data.DisplayName.Trim();
            if (name.Length == 0) throw new ArgumentException("Отображаемое имя видео не должно быть пустым.");
            data.Name = data.DisplayName = name;
        }
        return captured;
    }
    // Подтверждает только отправленную ревизию через общую механику набора payload.
    public Task AcknowledgeAsync(EntitySaveBatch sent, SaveBatchResult result, long revision, CancellationToken ct) => _payloads.AcknowledgeAsync(sent, result, revision, ct);
    // Возвращает все изменённые metadata библиотеки к подтверждённым значениям.
    public Task DiscardAsync(CancellationToken ct) => _payloads.DiscardAsync(ct);
}
// Factory обслуживает один рабочий экземпляр библиотеки внутри её окна.
public sealed class MediaVideoWorkingSetFactory(MediaServerService media, EntityTypeRegistry types)
{
    // Менеджер создаёт контекст; предметная factory подключает к нему только своего владельца.
    public MediaVideoWorkingSet Get(MiniAppModule module)
    { var save = module.SaveContext!; return save.GetOrCreateParticipant(() => new MediaVideoWorkingSet(save, media, types)); }
}

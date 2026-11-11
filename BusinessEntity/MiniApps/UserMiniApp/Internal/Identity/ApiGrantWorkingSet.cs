using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// API grant участвует в стандартном dirty/Save/Discard владельца User, не в отдельной SQL записи формы.
internal sealed class ApiGrantWorkingSet(MiniAppSaveContext context,ApiIntegrationAdministration administration):IMiniAppSaveParticipant
{
    public ApiGrantForm Form {get;private set;}=new();
    private ApiGrantForm _saved=new();
    private ApiGrantForm? _captured;
    private ApiGrantForm? _prepared;
    // Выбор другой записи не теряет несохранённый допуск.
    public void Load(ApiGrantForm form){if(context.IsDirty)throw new InvalidOperationException("Сначала сохраните или отмените правки.");_saved=form.Copy();Form=form.Copy();}
    // Синхронный input отмечает ревизию до любого await.
    public void Changed()=>context.MarkChanged();
    // Capture изолирует отправляемую форму от позднего ввода.
    public EntitySaveBatch Capture(Guid operationId,Guid editSessionId,long revision)
    {_captured=Form.Copy();return new(operationId,"UserMiniApp",editSessionId);}
    // В prepared batch нет внешних credentials.
    public async Task<EntitySaveBatch> PrepareAsync(EntitySaveBatch captured,CancellationToken ct)
    {
        var row=await administration.PrepareAsync(_captured!,ct);_prepared=ApiGrantForm.From(row);
        return captured with {OwnedServiceChanges=[new("UserMiniApp","UserIdentityV1",JsonSerializer.Serialize(new[]{new UserTableMutation(row.GetType().Name,row.Id,
            JsonSerializer.Serialize(row,GraphArchiveJson.CreateOptions()))},GraphArchiveJson.CreateOptions()))]};
    }
    // Только подтверждённая запись меняет baseline; поздний ввод остаётся dirty.
    public Task AcknowledgeAsync(EntitySaveBatch sent,SaveBatchResult result,long revision,CancellationToken ct)
    {_saved=_prepared!.Copy();if(context.Revision==revision)Form=_saved.Copy();else Form.Revision=_saved.Revision;return Task.CompletedTask;}
    // Discard затрагивает локальные поля, не отозванные/активные сессии других клиентов.
    public Task DiscardAsync(CancellationToken ct){Form=_saved.Copy();return Task.CompletedTask;}
}

using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Caching;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Координирует согласованный снимок и события: после SQL commit сначала cache swap, затем уведомления.
internal sealed class CommittedGraphCoordinator(DataProviderGraphCache graph, DataProviderCommitPublisher publisher, BusinessEntity.Infrastructure.Bus.ITransactionAdmission? admission = null,
    BusinessEntity.Infrastructure.Api.IApiCommitAuthorization? apiAuthorization=null)
{
    // Политика авторизации является общей инфраструктурой; storage не знает конкретный identity provider.
    public IDisposable? EnterWrite(BusinessEntity.Infrastructure.Bus.BusRequestContext actor) => admission?.Enter(actor);
    // Канонический API writer без User admission не получает неявного разрешения.
    public Task AuthorizeApiAsync(ApiOperationIdentity identity,ApiOperationCommit commit,BusinessEntity.Infrastructure.Bus.BusRequestContext actor,CancellationToken ct)
        =>(apiAuthorization??throw new InvalidOperationException("API commit authorization не зарегистрирован.")).ValidateAsync(identity,commit,actor,ct);
    // Гарантирует наличие начального снимка до операции.
    public Task InitializeAsync(CancellationToken ct) => graph.InitializeAsync(ct);
    // Готовит кандидат внутри той же SQL-транзакции; отказ не меняет опубликованный кэш.
    public Task<GraphSnapshot> PrepareAsync(KmsBusinessEntityDbContext db, IReadOnlySet<Guid> changed, CancellationToken ct) => graph.PrepareAsync(db, changed, ct);
    // Вызывается только после commit, сохраняя порядок cache → events.
    public void Publish(GraphSnapshot snapshot, IReadOnlySet<Guid> changed, SaveBatchResult result)
    { graph.Publish(snapshot, changed); publisher.Publish(result); }
}

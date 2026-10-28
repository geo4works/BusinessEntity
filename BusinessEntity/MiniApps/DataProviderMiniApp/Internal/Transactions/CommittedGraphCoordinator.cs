using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Caching;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Координирует согласованный снимок и события: после SQL commit сначала cache swap, затем уведомления.
internal sealed class CommittedGraphCoordinator(DataProviderGraphCache graph, DataProviderCommitPublisher publisher)
{
    // Гарантирует наличие начального снимка до операции.
    public Task InitializeAsync(CancellationToken ct) => graph.InitializeAsync(ct);
    // Готовит кандидат внутри той же SQL-транзакции; отказ не меняет опубликованный кэш.
    public Task<GraphSnapshot> PrepareAsync(KmsBusinessEntityDbContext db, IReadOnlySet<Guid> changed, CancellationToken ct) => graph.PrepareAsync(db, changed, ct);
    // Вызывается только после commit, сохраняя порядок cache → events.
    public void Publish(GraphSnapshot snapshot, IReadOnlySet<Guid> changed, SaveBatchResult result)
    { graph.Publish(snapshot, changed); publisher.Publish(result); }
}

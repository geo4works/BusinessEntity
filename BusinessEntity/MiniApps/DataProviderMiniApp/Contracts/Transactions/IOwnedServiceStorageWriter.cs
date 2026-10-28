using System.Data.Common;
using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

// Разрешённое исключение User: служебные данные владельца участвуют в той же транзакции и durable outcome.
public sealed record OwnedServiceStorageChange(string MiniAppId, string Kind, string Data);
// Writer не получает DbContext бизнес-графа; он вправе писать только свои служебные таблицы.
public interface IOwnedServiceStorageWriter
{
    string MiniAppId { get; }
    // Применяет объявленное изменение через уже открытую транзакцию DataProvider.
    Task ApplyAsync(OwnedServiceStorageChange change, BusRequestContext actor, DbConnection connection, DbTransaction transaction, CancellationToken ct);
}

using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.Infrastructure.Bus;
using System.Data.Common;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

// Проверяет и индексирует правила истории и архива, зарегистрированные предметными владельцами.
internal sealed class EntityPersistencePolicies
{
    public IReadOnlyDictionary<BusinessEntityTypeEnum, IEntityVersionPolicy> Versions { get; }
    public IReadOnlyDictionary<BusinessEntityTypeEnum, IEntityArchivePolicy> Archives { get; }
    private readonly IReadOnlyDictionary<string, IOwnedServiceStorageWriter> _serviceWriters;
    public TimeSpan OutcomeRetention { get; }
    // Дубликаты типов и неверный срок durable outcome прерывают запуск.
    public EntityPersistencePolicies(IEnumerable<IEntityVersionPolicy> versions, IEnumerable<IEntityArchivePolicy> archives, IConfiguration configuration, IEnumerable<IOwnedServiceStorageWriter> serviceWriters)
    {
        _serviceWriters = serviceWriters.ToDictionary(x => x.MiniAppId, StringComparer.Ordinal);
        Versions = versions.ToDictionary(x => x.EntityType); Archives = archives.ToDictionary(x => x.EntityType);
        var days = configuration.GetValue("DataProvider:OperationRetentionDays", 7);
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(configuration), "Срок исходов должен быть положительным.");
        OutcomeRetention = TimeSpan.FromDays(days);
    }
    // Владелец исключения обязан совпадать с адресным владельцем всего пакета.
    public async Task ApplyOwnedAsync(string miniAppId, IReadOnlyList<OwnedServiceStorageChange> changes, BusRequestContext actor,
        DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        foreach (var change in changes)
        {
            if (change.MiniAppId != miniAppId || !_serviceWriters.TryGetValue(change.MiniAppId, out var writer))
                throw new UnauthorizedAccessException("Не объявлен writer служебных данных владельца.");
            await writer.ApplyAsync(change, actor, connection, transaction, ct);
        }
    }
}

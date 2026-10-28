using BusinessEntity.Core.Classes;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Navigation;

// Согласованная структурная проекция без payload; права накладывает вызывающий владелец.
public sealed record GraphProjection(long Revision, IReadOnlyList<Core.Classes.BusinessEntity> Entities,
    IReadOnlyList<BusinessEntityRelation> Relations, IReadOnlyDictionary<Guid, string> PublishedNames);
// Статистика физического storage отделена от собственных SQL-запросов прав UserMiniApp.
public sealed record DataProviderCacheStatistics(long Revision, int EntityCount, int RelationCount, long EstimatedGraphBytes,
    long GraphHits, long PayloadHits, long PayloadMisses, long PayloadEvictions,
    int PayloadEntries, long PayloadBytes, long PayloadLimitBytes, int PayloadLimitEntries,
    long StorageReads, long StorageWrites);

// Запросы структурного графа используют одну ревизию и не читают тяжёлое содержимое.
public enum GraphQueryKind { All, Roots, Children, Subgraph, PathToRoot, SearchName }

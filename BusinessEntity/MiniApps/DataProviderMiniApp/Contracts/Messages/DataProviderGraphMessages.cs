using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Navigation;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;

// Структурный запрос выполняется на одной опубликованной ревизии.
public sealed record QueryGraphRequest(Guid RequestId, GraphQueryKind Kind, Guid? RootId = null, string? SearchText = null, int Take = 100) : BusRequest(RequestId);
// Снимок не содержит тяжёлых данных и пользовательских прав.
public sealed record QueryGraphResponse(Guid RequestId, GraphProjection? Projection, BusError? Error = null) : BusResponse(RequestId, Error);
// Metadata имён опубликованных версий для проекции, фильтруемой владельцем доступа.
public sealed record GetPublishedEntityNamesRequest(Guid RequestId, IReadOnlyList<Guid> EntityIds) : BusRequest(RequestId);
// Название возвращается только для существующего опубликованного снимка.
public sealed record GetPublishedEntityNamesResponse(Guid RequestId, IReadOnlyDictionary<Guid, string>? Names, BusError? Error = null) : BusResponse(RequestId, Error);
// Диагностический запрос кэша не смешивает graph SQL с запросами прав User.
public sealed record GetDataProviderCacheStatisticsRequest(Guid RequestId) : BusRequest(RequestId);
// Числа памяти являются оценкой удерживаемых UTF-16 данных и metadata, не метрикой всего CLR heap.
public sealed record GetDataProviderCacheStatisticsResponse(Guid RequestId, DataProviderCacheStatistics? Statistics, BusError? Error = null) : BusResponse(RequestId, Error);

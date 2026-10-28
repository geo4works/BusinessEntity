using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
namespace BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;

// Сообщение команды GetVideosAsync.
public sealed record MediaLibraryGetVideosRequest(Guid RequestId, Guid? SpaceId, string? Filter) : BusRequest(RequestId);
// Результат команды GetVideosAsync.
public sealed record MediaLibraryGetVideosResponse(Guid RequestId, IReadOnlyList<MediaVideoInfo> Value, BusError? Error = null) : BusResponse(RequestId, Error);

using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
namespace BusinessEntity.MiniApps.SpacesMiniApp.Contracts;

// Сообщение команды GetSpaceByIdAsync.
public sealed record SpacesGetSpaceByIdRequest(Guid RequestId, Guid SpaceId) : BusRequest(RequestId);
// Результат команды GetSpaceByIdAsync.
public sealed record SpacesGetSpaceByIdResponse(Guid RequestId, BusinessEntity.Core.Classes.BusinessEntity? Value, BusError? Error = null) : BusResponse(RequestId, Error);
// Сообщение команды GetSpacesAsync.
public sealed record SpacesGetSpacesRequest(Guid RequestId) : BusRequest(RequestId);
// Результат команды GetSpacesAsync.
public sealed record SpacesGetSpacesResponse(Guid RequestId, IReadOnlyList<BusinessEntity.Core.Classes.BusinessEntity> Value, BusError? Error = null) : BusResponse(RequestId, Error);
// Сообщение команды GetGenericSpacePropertiesAsync.
public sealed record SpacesGetGenericSpacePropertiesRequest(Guid RequestId, Guid SpaceId) : BusRequest(RequestId);
// Результат команды GetGenericSpacePropertiesAsync.
public sealed record SpacesGetGenericSpacePropertiesResponse(Guid RequestId, GenericSpaceProperties Value, BusError? Error = null) : BusResponse(RequestId, Error);

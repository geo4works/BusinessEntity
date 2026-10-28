using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
namespace BusinessEntity.MiniApps.SampleDataMiniApp.Contracts;

// Сообщение команды ForceReseedAsync.
public sealed record SampleDataForceReseedRequest(Guid RequestId) : BusRequest(RequestId);
// Результат команды ForceReseedAsync.
public sealed record SampleDataForceReseedResponse(Guid RequestId, bool Value, BusError? Error = null) : BusResponse(RequestId, Error);

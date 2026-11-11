using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

// Версионное членство обязательно задаёт владелец версионного документа, не HTTP клиент.
public sealed record OpenApiFileLeaseRequest(Guid RequestId,Guid EntityId,string FileId,string Variant,int? Version,Guid SpaceId,string CapabilityId,IReadOnlyList<ApiPermissionCheck> Checks):BusRequest(RequestId);
public sealed record OpenApiFileLeaseResponse(Guid RequestId,ApiFileDownload? Result,BusError? Error=null):BusResponse(RequestId,Error);

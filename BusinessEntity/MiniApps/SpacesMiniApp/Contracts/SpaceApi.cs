using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Contracts;

public sealed record ApiSpace(Guid Id,string Name,bool IsPublic,DateTime CreatedAt,Guid? CreatedBy);
public sealed record ApiSpaceCreate(string Name,bool IsPublic=false,bool DoBackup=true,int BackupIntervalMinutes=5);
public sealed record ApiSpaceUpdate(string? Name=null,bool? IsPublic=null);
public sealed record ApiSpaceSettings(bool DoBackup,int BackupIntervalMinutes);
public sealed record ListApiSpacesRequest(Guid RequestId,int Limit,string? Cursor):BusRequest(RequestId);
public sealed record ListApiSpacesResponse(Guid RequestId,ApiPage<ApiSpace>? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ReadApiSpaceRequest(Guid RequestId,Guid SpaceId):BusRequest(RequestId);
public sealed record ReadApiSpaceResponse(Guid RequestId,ApiSpace? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record CreateApiSpaceRequest(Guid RequestId,ApiSpaceCreate Input):BusRequest(RequestId);
public sealed record UpdateApiSpaceRequest(Guid RequestId,Guid SpaceId,ApiSpaceUpdate Input):BusRequest(RequestId);
public sealed record DeleteApiSpaceRequest(Guid RequestId,Guid SpaceId,Guid PlanId):BusRequest(RequestId);
public sealed record ApiSpaceWriteResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ReadApiSpaceSettingsRequest(Guid RequestId,Guid SpaceId):BusRequest(RequestId);
public sealed record ReadApiSpaceSettingsResponse(Guid RequestId,ApiSpaceSettings? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record UpdateApiSpaceSettingsRequest(Guid RequestId,Guid SpaceId,ApiSpaceSettings Input):BusRequest(RequestId);
public sealed record PlanApiSpaceDeletionRequest(Guid RequestId,Guid SpaceId):BusRequest(RequestId);
public sealed record PlanApiSpaceDeletionResponse(Guid RequestId,ApiDeletionPlan? Result,BusError? Error=null):BusResponse(RequestId,Error);

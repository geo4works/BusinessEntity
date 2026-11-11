using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.MiniApps.TreeMiniApp.Contracts;

public sealed record ApiTreeNode(Guid Id,string Type,string Name,Guid SpaceId,Guid ParentId,bool HasVisibleChildren);
public sealed record ApiFolderCreate(string Name,Guid? ParentId=null);
public sealed record ApiFolderUpdate(string Name);
public sealed record ApiTreeMove(Guid SpaceId,Guid[] EntityIds,Guid TargetParentId);
public sealed record ApiTreeDelete(Guid SpaceId,Guid[] EntityIds,Guid? PlanId=null);
public sealed record ApiTreeDeletePlan(Guid SpaceId,Guid[] EntityIds);
public sealed record ListApiTreeRequest(Guid RequestId,Guid SpaceId,Guid? ParentId,int Limit,string? Cursor):BusRequest(RequestId);
public sealed record ListApiTreeResponse(Guid RequestId,ApiPage<ApiTreeNode>? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ReadApiFolderRequest(Guid RequestId,Guid FolderId):BusRequest(RequestId);
public sealed record ReadApiFolderResponse(Guid RequestId,ApiTreeNode? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record CreateApiFolderRequest(Guid RequestId,Guid SpaceId,ApiFolderCreate Input):BusRequest(RequestId);
public sealed record UpdateApiFolderRequest(Guid RequestId,Guid FolderId,ApiFolderUpdate Input):BusRequest(RequestId);
public sealed record MoveApiTreeRequest(Guid RequestId,ApiTreeMove Input):BusRequest(RequestId);
public sealed record DeleteApiTreeRequest(Guid RequestId,ApiTreeDelete Input):BusRequest(RequestId);
public sealed record ApiTreeWriteResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record PlanApiTreeDeletionRequest(Guid RequestId,ApiTreeDeletePlan Input):BusRequest(RequestId);
public sealed record PlanApiTreeDeletionResponse(Guid RequestId,ApiDeletionPlan? Result,BusError? Error=null):BusResponse(RequestId,Error);

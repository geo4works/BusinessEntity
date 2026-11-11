using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;
using BusinessEntity.MiniApps.TreeMiniApp.Internal;

namespace BusinessEntity.MiniApps.TreeMiniApp.Registration;

internal static class TreeApiRegistration
{
    public static void AddTreeApi(this IServiceCollection services)
    {
        services.AddScoped<TreeApiReader>();services.AddScoped<TreeApiCommands>();services.AddScoped<TreeApiHandler>();services.AddScoped<TreeApiPlanHandler>();
        services.AddBusEndpoint<TreeApiHandler>(typeof(ListApiTreeRequest),typeof(ReadApiFolderRequest),typeof(CreateApiFolderRequest),typeof(UpdateApiFolderRequest),typeof(MoveApiTreeRequest),typeof(DeleteApiTreeRequest));services.AddBusEndpoint<TreeApiPlanHandler>(typeof(PlanApiTreeDeletionRequest));
        services.AddApiBinding<ApiEmptyInput,ListApiTreeRequest,ListApiTreeResponse,ApiPage<ApiTreeNode>>(Cap("tree.children","GET","/spaces/{spaceId:guid}/tree"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h,"spaceId"),ApiHttpInput.QueryId(h,"parentId"),ApiHttpInput.Limit(h),ApiHttpInput.Cursor(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ReadApiFolderRequest,ReadApiFolderResponse,ApiTreeNode>(Cap("folders.read","GET","/folders/{id:guid}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiFolderCreate,CreateApiFolderRequest,ApiTreeWriteResponse,ApiMutationOutcome>(Cap("folders.create","POST","/spaces/{spaceId:guid}/folders",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h,"spaceId"),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiFolderUpdate,UpdateApiFolderRequest,ApiTreeWriteResponse,ApiMutationOutcome>(Cap("folders.update","PATCH","/folders/{id:guid}",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiTreeMove,MoveApiTreeRequest,ApiTreeWriteResponse,ApiMutationOutcome>(Cap("tree.move","POST","/tree/move",true),static(i,_)=>new(Guid.NewGuid(),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiTreeDelete,DeleteApiTreeRequest,ApiTreeWriteResponse,ApiMutationOutcome>(Cap("tree.delete","POST","/tree/delete",true),static(i,_)=>new(Guid.NewGuid(),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiTreeDeletePlan,PlanApiTreeDeletionRequest,PlanApiTreeDeletionResponse,ApiDeletionPlan>(Cap("tree.deletion-plan","POST","/tree/deletion-plan",true,ApiWriteMode.Technical),static(i,_)=>new(Guid.NewGuid(),i),static r=>r.Result??throw new InvalidDataException());
        services.AddApiToolset("TreeMiniApp");
    }
    private static ApiCapability Cap(string id,string method,string route,bool mutates=false,ApiWriteMode mode=ApiWriteMode.Canonical)=>new("tree",1,id,1,"TreeMiniApp",id,method,route,id+".input.v1",id+".output.v1",mutates,mode);
}

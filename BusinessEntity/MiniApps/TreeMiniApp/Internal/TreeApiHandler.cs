using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal;

internal sealed class TreeApiHandler(TreeApiReader reader,TreeApiCommands commands,IMessageBus bus,BusRequestScope scope,ILogger<TreeApiHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<ListApiTreeRequest,ListApiTreeResponse>(async r=>new(r.RequestId,await reader.ChildrenAsync(r,default)),(id,e)=>new(id,null,e));
        Subscribe<ReadApiFolderRequest,ReadApiFolderResponse>(async r=>new(r.RequestId,await reader.FolderAsync(r.FolderId,default)),(id,e)=>new(id,null,e));
        Subscribe<CreateApiFolderRequest,ApiTreeWriteResponse>(async r=>{await commands.CreateAsync(r.SpaceId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<UpdateApiFolderRequest,ApiTreeWriteResponse>(async r=>{await commands.UpdateAsync(r.FolderId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<MoveApiTreeRequest,ApiTreeWriteResponse>(async r=>{await commands.MoveAsync(r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<DeleteApiTreeRequest,ApiTreeWriteResponse>(async r=>{await commands.DeleteAsync(r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
    }
}
internal sealed class TreeApiPlanHandler(TreeApiCommands commands,BusRequestClient client,ApiWriteContext write,IMessageBus bus,BusRequestScope scope,ILogger<TreeApiPlanHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()=>Subscribe<PlanApiTreeDeletionRequest,PlanApiTreeDeletionResponse>(async r=>{var ids=await commands.ManageSetAsync(r.Input.SpaceId,r.Input.EntityIds,default);var result=await client.SendAsync<PrepareApiDeletionPlanRequest,PrepareApiDeletionPlanResponse>(new(Guid.NewGuid(),write.Id("deletion-plan"),ids,r.Input.SpaceId,ids.Select(id=>new ApiPermissionCheck("AdminItems",id,r.Input.SpaceId)).ToArray()));return new(r.RequestId,result.Result);},(id,e)=>new(id,null,e));
}

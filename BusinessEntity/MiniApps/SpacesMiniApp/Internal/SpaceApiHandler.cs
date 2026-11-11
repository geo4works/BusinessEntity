using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Internal;

internal sealed class SpaceApiHandler(SpaceApiReader reader,SpaceApiCommands commands,IMessageBus bus,BusRequestScope scope,ILogger<SpaceApiHandler> logger)
    :ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()
    {
        Subscribe<ListApiSpacesRequest,ListApiSpacesResponse>(async r=>new(r.RequestId,await reader.ListAsync(r,default)),(id,e)=>new(id,null,e));
        Subscribe<ReadApiSpaceRequest,ReadApiSpaceResponse>(async r=>new(r.RequestId,await reader.ReadAsync(r.SpaceId,default)),(id,e)=>new(id,null,e));
        Subscribe<CreateApiSpaceRequest,ApiSpaceWriteResponse>(async r=>{await commands.CreateAsync(r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<UpdateApiSpaceRequest,ApiSpaceWriteResponse>(async r=>{await commands.UpdateAsync(r.SpaceId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<DeleteApiSpaceRequest,ApiSpaceWriteResponse>(async r=>{await commands.DeleteAsync(r.SpaceId,r.PlanId,default);return new(r.RequestId);},(id,e)=>new(id,e));
        Subscribe<ReadApiSpaceSettingsRequest,ReadApiSpaceSettingsResponse>(async r=>new(r.RequestId,await reader.SettingsAsync(r.SpaceId,default)),(id,e)=>new(id,null,e));
        Subscribe<UpdateApiSpaceSettingsRequest,ApiSpaceWriteResponse>(async r=>{await commands.SettingsAsync(r.SpaceId,r.Input,default);return new(r.RequestId);},(id,e)=>new(id,e));
    }
}
// Подготовка технического плана отделена от canonical/read endpoint, без упаковки DI зависимостей.
internal sealed class SpaceApiPlanHandler(SpaceApiCommands commands,BusRequestClient client,ApiWriteContext write,IMessageBus bus,BusRequestScope scope,ILogger<SpaceApiPlanHandler> logger):ScopedBusEndpoint(bus,scope,logger)
{
    protected override void SubscribeRequests()=>Subscribe<PlanApiSpaceDeletionRequest,PlanApiSpaceDeletionResponse>(async r=>{await commands.ManageAsync(r.SpaceId,default);var response=await client.SendAsync<PrepareApiDeletionPlanRequest,PrepareApiDeletionPlanResponse>(new(Guid.NewGuid(),write.Id("deletion-plan"),[r.SpaceId],r.SpaceId,[new("AdminSpace",r.SpaceId,r.SpaceId)]));return new(r.RequestId,response.Result);},(id,e)=>new(id,null,e));
}

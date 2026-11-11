using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.SpacesMiniApp.Internal;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Registration;

// Владелец объявляет только собственные типизированные routes, никакого CRUD в ExternalApi.
internal static class SpaceApiRegistration
{
    public static void AddSpaceApi(this IServiceCollection services)
    {
        services.AddScoped<SpaceApiReader>();services.AddScoped<SpaceApiCommands>();services.AddScoped<SpaceApiHandler>();services.AddScoped<SpaceApiPlanHandler>();
        services.AddBusEndpoint<SpaceApiHandler>(typeof(ListApiSpacesRequest),typeof(ReadApiSpaceRequest),typeof(CreateApiSpaceRequest),typeof(UpdateApiSpaceRequest),typeof(DeleteApiSpaceRequest),typeof(ReadApiSpaceSettingsRequest),typeof(UpdateApiSpaceSettingsRequest));services.AddBusEndpoint<SpaceApiPlanHandler>(typeof(PlanApiSpaceDeletionRequest));
        services.AddApiBinding<ApiEmptyInput,ListApiSpacesRequest,ListApiSpacesResponse,ApiPage<ApiSpace>>(Cap("spaces.list","GET","/spaces"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Limit(h),ApiHttpInput.Cursor(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ReadApiSpaceRequest,ReadApiSpaceResponse,ApiSpace>(Cap("spaces.read","GET","/spaces/{id:guid}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiSpaceCreate,CreateApiSpaceRequest,ApiSpaceWriteResponse,ApiMutationOutcome>(Cap("spaces.create","POST","/spaces",true),static(i,_)=>new(Guid.NewGuid(),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiSpaceUpdate,UpdateApiSpaceRequest,ApiSpaceWriteResponse,ApiMutationOutcome>(Cap("spaces.update","PATCH","/spaces/{id:guid}",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiEmptyInput,DeleteApiSpaceRequest,ApiSpaceWriteResponse,ApiMutationOutcome>(Cap("spaces.delete","DELETE","/spaces/{id:guid}",true),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),ApiHttpInput.RequiredQueryId(h,"planId")),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiEmptyInput,PlanApiSpaceDeletionRequest,PlanApiSpaceDeletionResponse,ApiDeletionPlan>(Cap("spaces.deletion-plan","POST","/spaces/{id:guid}/deletion-plan",true,ApiWriteMode.Technical),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ReadApiSpaceSettingsRequest,ReadApiSpaceSettingsResponse,ApiSpaceSettings>(Cap("spaces.settings.read","GET","/spaces/{id:guid}/settings"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiSpaceSettings,UpdateApiSpaceSettingsRequest,ApiSpaceWriteResponse,ApiMutationOutcome>(Cap("spaces.settings.update","PATCH","/spaces/{id:guid}/settings",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiToolset("SpacesMiniApp");
    }
    private static ApiCapability Cap(string id,string method,string route,bool mutates=false,ApiWriteMode mode=ApiWriteMode.Canonical)=>new("spaces",1,id,1,"SpacesMiniApp",id,method,route,id+".input.v1",id+".output.v1",mutates,mode);
}

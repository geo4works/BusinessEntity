using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Registration;

internal static class DataProviderApiJobRegistration
{
    public static void AddDataProviderApiJobs(this IServiceCollection services)
    {
        services.AddHostedService<ApiJobRuntime>();services.AddSingleton<DataProviderApiJobHandler>();services.AddScoped<DataProviderApiJobControlHandler>();
        services.AddBusEndpoint<DataProviderApiJobHandler>(typeof(AbandonApiPreparedChunksRequest),typeof(OpenApiPreparedChunksRequest),typeof(SealApiPreparedChunksRequest),typeof(CreateApiJobRequest),typeof(ReadApiJobRequest),typeof(ClaimApiJobRequest),typeof(UpdateApiJobRequest),typeof(CancelApiJobRequest));services.AddBusEndpoint<DataProviderApiJobControlHandler>(typeof(ReauthorizeApiJobControlRequest));
        services.AddApiBinding<ApiEmptyInput,ReadApiJobRequest,ReadApiJobResponse,ApiJobStatus>(Cap("jobs.read","GET","/jobs/{id:guid}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),"jobs.read"),static r=>r.Result?.Public??throw new KeyNotFoundException());
        services.AddApiBinding<ApiEmptyInput,CancelApiJobRequest,UpdateApiJobResponse,ApiJobStatus>(Cap("jobs.cancel","DELETE","/jobs/{id:guid}",true),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ReauthorizeApiJobControlRequest,UpdateApiJobResponse,ApiJobStatus>(Cap("jobs.reauthorize","POST","/jobs/{id:guid}/reauthorize",true),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
    }
    private static ApiCapability Cap(string id,string method,string route,bool mutation=false)=>new("jobs",1,id,1,"DataProviderMiniApp",id,method,route,id+".input.v1",id+".output.v1",mutation,ApiWriteMode.Technical);
}

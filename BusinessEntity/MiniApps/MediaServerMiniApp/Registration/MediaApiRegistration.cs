using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Registration;

internal static class MediaApiRegistration
{
    public static void AddMediaApi(this IServiceCollection services)
    {
        services.AddSingleton<IApiUploadPurpose,MediaUploadPurpose>();services.AddScoped<MediaApiJobs>();services.AddScoped<MediaApiCommands>();services.AddScoped<MediaApiReader>();services.AddScoped<MediaApiHandler>();services.AddBusEndpoint<MediaApiHandler>(typeof(RunApiMediaJobRequest),typeof(ListApiMediaRequest),typeof(ReadApiMediaRequest),typeof(DownloadApiMediaRequest),typeof(CreateApiMediaRequest),typeof(UpdateApiMediaRequest),typeof(DeleteApiMediaRequest));
        services.AddApiBinding<ApiEmptyInput,ListApiMediaRequest,ListApiMediaResponse,ApiPage<ApiMediaVideo>>(Cap("media-videos.list","GET","/spaces/{spaceId:guid}/media-videos"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h,"spaceId"),h.Query["search"],ApiHttpInput.Limit(h),ApiHttpInput.Cursor(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ReadApiMediaRequest,ReadApiMediaResponse,ApiMediaVideo>(Cap("media-videos.read","GET","/media-videos/{id:guid}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiMediaCreate,CreateApiMediaRequest,CreateApiJobResponse,ApiJobAccepted>(Cap("media-videos.create","POST","/spaces/{spaceId:guid}/media-videos",true) with{WriteMode=ApiWriteMode.Deferred},static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h,"spaceId"),i),static r=>r.Result??throw new InvalidDataException());
        services.AddApiJobRuntime<RunApiMediaJobRequest,ApiMediaWriteResponse>(new("MediaServerMiniApp","media.original.v1","media-videos.create"),static id=>new(Guid.NewGuid(),id));
        services.AddApiBinding<ApiMediaUpdate,UpdateApiMediaRequest,ApiMediaWriteResponse,ApiMutationOutcome>(Cap("media-videos.update","PATCH","/media-videos/{id:guid}",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiEmptyInput,DeleteApiMediaRequest,ApiMediaWriteResponse,ApiMutationOutcome>(Cap("media-videos.delete","DELETE","/media-videos/{id:guid}",true),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static _=>throw new NotSupportedException());
        services.AddApiDownload<DownloadApiMediaRequest,DownloadApiMediaResponse>(Cap("media-videos.download","GET","/media-videos/{id:guid}/file"),static h=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());services.AddApiToolset("MediaServerMiniApp");
    }
    private static ApiCapability Cap(string id,string method,string route,bool mutation=false)=>new("media-videos",1,id,1,"MediaServerMiniApp",id,method,route,id+".input.v1",id+".output.v1",mutation);
}

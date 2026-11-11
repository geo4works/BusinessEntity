using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Registration;

internal static class DataProviderApiUploadRegistration
{
    public static void AddDataProviderApiUploads(this IServiceCollection services)
    {
        services.AddHostedService<ApiUploadMaintenance>();services.AddSingleton<DataProviderApiUploadHandler>();services.AddBusEndpoint<DataProviderApiUploadHandler>(typeof(OpenApiFileLeaseRequest),typeof(CreateApiUploadRequest),typeof(ReadApiUploadRequest),typeof(CompleteApiUploadRequest),typeof(CancelApiUploadRequest),typeof(OpenApiUploadPartRequest),typeof(CompleteApiUploadPartRequest),typeof(ReadApiUploadLeaseRequest));
        services.AddApiBinding<ApiUploadCreate,CreateApiUploadRequest,ApiUploadResponse,ApiUploadStatus>(Cap("uploads.create","POST","/uploads",true),static(i,h)=>new(Guid.NewGuid(),i),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ReadApiUploadRequest,ApiUploadResponse,ApiUploadStatus>(Cap("uploads.read","GET","/uploads/{id:guid}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,CompleteApiUploadRequest,ApiUploadResponse,ApiUploadStatus>(Cap("uploads.complete","POST","/uploads/{id:guid}/complete",true),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,CancelApiUploadRequest,ApiUploadResponse,ApiUploadStatus>(Cap("uploads.cancel","DELETE","/uploads/{id:guid}",true),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddSingleton<IApiRouteBinding>(new UploadPartBinding(Cap("uploads.part.put","PUT","/uploads/{id:guid}/parts/{partNumber:int}",true)));
        services.AddApiToolset("DataProviderMiniApp");
    }
    private static ApiCapability Cap(string id,string method,string route,bool mutation=false)=>new("uploads",1,id,1,"DataProviderMiniApp",id,method,route,id+".input.v1",id+".output.v1",mutation,ApiWriteMode.Technical);
    // Transport знает только checksum/length и typed upload requests; формат/назначение проверяет владелец.
    private sealed class UploadPartBinding(ApiCapability definition):IApiRouteBinding
    {
        public ApiCapability Definition=>definition;
        public void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPut("/api/v1"+definition.Route,async(HttpContext http,ApiRequestDispatcher dispatcher,ApiOperationExecutor operations,BusRequestClient bus,IApiStagingDataPlane plane)=>
            {
                var session=await dispatcher.SessionAsync(http,definition.Id,http.RequestAborted);
                if(http.Request.ContentType!="application/octet-stream")throw new ApiFault(415,"payload.unsupported","Требуется application/octet-stream.");
                var id=ApiHttpInput.Id(http.Request);if(!int.TryParse(http.Request.RouteValues["partNumber"]?.ToString(),out var number)||http.Request.ContentLength is not {} length)throw new ArgumentException("Нужны partNumber и Content-Length.");
                var hash=http.Request.Headers["X-Part-SHA256"].ToString().ToUpperInvariant();var opened=await bus.SendAsync<OpenApiUploadPartRequest,OpenApiUploadPartResponse>(new(Guid.NewGuid(),id,number,length,hash),http.RequestAborted);
                var lease=opened.LeaseId??throw new InvalidDataException();await plane.CopyPartAsync(lease,http.Request.Body,http.RequestAborted);
                var response=await operations.ExecuteRegisteredAsync<CompleteApiUploadPartRequest,ApiUploadResponse>(http,definition,new(Guid.NewGuid(),id,number,length,hash){LeaseId=lease},session,http.RequestAborted);
                return Results.Json(response.Result??throw new InvalidDataException());
            }).RequireAuthorization("ExternalApi").RequireRateLimiting("external-api").WithName(definition.Id).WithMetadata(definition,new ApiBinaryRequestLimit(DataProviderTransactionService.UploadPartSize)).WithTags("External API v1").Produces<ApiUploadStatus>();
        }
    }
}

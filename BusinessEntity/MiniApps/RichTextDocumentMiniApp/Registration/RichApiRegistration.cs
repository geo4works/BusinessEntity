using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Registration;

internal static class RichApiRegistration
{
    public static void AddRichApi(this IServiceCollection services)
    {
        services.AddScoped<RichApiAccess>();services.AddScoped<RichApiBlocks>();services.AddScoped<RichApiReader>();services.AddScoped<RichApiCommands>();services.AddScoped<RichApiHistoryCommands>();services.AddScoped<RichApiReadHandler>();services.AddScoped<RichApiWriteHandler>();
        services.AddBusEndpoint<RichApiReadHandler>(typeof(ReadApiRichRequest),typeof(ReadApiRichContentRequest),typeof(ListApiRichVersionsRequest),typeof(ReadApiRichPublicationRequest));
        services.AddBusEndpoint<RichApiWriteHandler>(typeof(CreateApiRichRequest),typeof(SaveApiRichRevisionRequest),typeof(UpdateApiRichMetadataRequest),typeof(RestoreApiRichRequest),typeof(DescribeApiRichVersionRequest),typeof(PublishApiRichRequest),typeof(DeleteApiRichRequest));
        services.AddApiBinding<ApiEmptyInput,ReadApiRichRequest,ReadApiRichResponse,ApiRichDocument>(Cap("rich-documents.read","GET","/rich-documents/{id:guid}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ReadApiRichRequest,ReadApiRichResponse,ApiRichDocument>(Cap("rich-documents.version.read","GET","/rich-documents/{id:guid}/versions/{version:int}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),int.Parse(h.RouteValues["version"]!.ToString()!),true),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ReadApiRichContentRequest,ReadApiRichContentResponse,ApiRichContent>(Cap("rich-documents.content","GET","/rich-documents/{id:guid}/content"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),QueryVersion(h),ApiHttpInput.Limit(h),ApiHttpInput.Cursor(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ListApiRichVersionsRequest,ListApiRichVersionsResponse,ApiPage<ApiRichVersion>>(Cap("rich-documents.versions","GET","/rich-documents/{id:guid}/versions"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),ApiHttpInput.Limit(h),ApiHttpInput.Cursor(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiRichCreate,CreateApiRichRequest,ApiRichWriteResponse,ApiMutationOutcome>(Cap("rich-documents.create","POST","/spaces/{spaceId:guid}/rich-documents",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h,"spaceId"),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiRichRevision,SaveApiRichRevisionRequest,ApiRichWriteResponse,ApiMutationOutcome>(Cap("rich-documents.revisions.save","POST","/rich-documents/{id:guid}/revisions",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiRichMetadata,UpdateApiRichMetadataRequest,ApiRichWriteResponse,ApiMutationOutcome>(Cap("rich-documents.metadata.update","PATCH","/rich-documents/{id:guid}",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiRichVersionSelection,RestoreApiRichRequest,ApiRichWriteResponse,ApiMutationOutcome>(Cap("rich-documents.restore-version","POST","/rich-documents/{id:guid}/restore-version",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiRichVersionDescription,DescribeApiRichVersionRequest,ApiRichWriteResponse,ApiMutationOutcome>(Cap("rich-documents.version.describe","PATCH","/rich-documents/{id:guid}/versions/{version:int}/description",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),int.Parse(h.RouteValues["version"]!.ToString()!),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiEmptyInput,ReadApiRichPublicationRequest,ReadApiRichPublicationResponse,ApiRichPublication>(Cap("rich-documents.publication.read","GET","/rich-documents/{id:guid}/publication"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiRichVersionSelection,PublishApiRichRequest,ApiRichWriteResponse,ApiMutationOutcome>(Cap("rich-documents.publication.update","PUT","/rich-documents/{id:guid}/publication",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiEmptyInput,DeleteApiRichRequest,ApiRichWriteResponse,ApiMutationOutcome>(Cap("rich-documents.delete","DELETE","/rich-documents/{id:guid}",true),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),ApiHttpInput.QueryId(h,"planId")),static _=>throw new NotSupportedException());
        services.AddSingleton<IApiUploadPurpose,RichImageUploadPurpose>();services.AddScoped<RichApiImages>();services.AddScoped<RichApiImageHandler>();services.AddBusEndpoint<RichApiImageHandler>(typeof(AttachApiRichImageRequest),typeof(DownloadApiRichImageRequest));
        services.AddApiBinding<ApiRichImageAttach,AttachApiRichImageRequest,ApiRichWriteResponse,ApiMutationOutcome>(Cap("rich-documents.images.attach","POST","/rich-documents/{id:guid}/images",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiDownload<DownloadApiRichImageRequest,DownloadApiRichImageResponse>(Cap("rich-documents.images.download","GET","/rich-documents/{id:guid}/images/{fileId}/{variant}"),static h=>new(Guid.NewGuid(),ApiHttpInput.Id(h),h.RouteValues["fileId"]!.ToString()!,h.RouteValues["variant"]!.ToString()!,QueryVersion(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddSingleton<IApiUploadPurpose,RichBookUploadPurpose>();services.AddScoped<RichApiImportSubmit>();services.AddScoped<RichBookPreparedWriter>();services.AddScoped<RichApiImportWorker>();services.AddScoped<RichApiImportHandler>();services.AddBusEndpoint<RichApiImportHandler>(typeof(SubmitApiRichImportRequest),typeof(RunApiRichImportJobRequest));
        services.AddApiBinding<ApiRichImport,SubmitApiRichImportRequest,CreateApiJobResponse,ApiJobAccepted>(Cap("rich-documents.import","POST","/rich-documents/imports/{targetId:guid}",true) with{WriteMode=ApiWriteMode.Deferred},static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h,"targetId"),i),static r=>r.Result??throw new InvalidDataException());
        services.AddApiJobRuntime<RunApiRichImportJobRequest,RunApiRichImportJobResponse>(new("RichTextDocumentMiniApp","rich.import.v1","rich-documents.import"),static id=>new(Guid.NewGuid(),id));
        services.AddApiToolset("RichTextDocumentMiniApp");
    }
    private static int? QueryVersion(HttpRequest request)=>string.IsNullOrEmpty(request.Query["version"])?null:int.TryParse(request.Query["version"],out var version)&&version>0?version:throw new ArgumentException("Неверная версия.");
    private static ApiCapability Cap(string id,string method,string route,bool mutates=false)=>new("rich-documents",1,id,1,"RichTextDocumentMiniApp",id,method,route,id+".input.v1",id+".output.v1",mutates);
}

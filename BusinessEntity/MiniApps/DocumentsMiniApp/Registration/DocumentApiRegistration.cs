using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts;
using BusinessEntity.MiniApps.DocumentsMiniApp.Internal;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Registration;

internal static class DocumentApiRegistration
{
    public static void AddDocumentApi(this IServiceCollection services)
    {
        services.AddScoped<DocumentApiReader>();services.AddScoped<DocumentApiCommands>();services.AddScoped<DocumentApiHandler>();services.AddBusEndpoint<DocumentApiHandler>(typeof(ReadApiDocumentRequest),typeof(ListApiDocumentVersionsRequest),typeof(CreateApiDocumentRequest),typeof(UpdateApiDocumentRequest),typeof(RestoreApiDocumentRequest),typeof(DeleteApiDocumentRequest));
        services.AddApiBinding<ApiEmptyInput,ReadApiDocumentRequest,ReadApiDocumentResponse,ApiDocument>(Cap("documents.read","GET","/documents/{id:guid}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ReadApiDocumentRequest,ReadApiDocumentResponse,ApiDocument>(Cap("documents.version.read","GET","/documents/{id:guid}/versions/{version:int}"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),int.Parse(h.RouteValues["version"]!.ToString()!)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiEmptyInput,ListApiDocumentVersionsRequest,ListApiDocumentVersionsResponse,ApiPage<ApiDocumentVersion>>(Cap("documents.versions","GET","/documents/{id:guid}/versions"),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),ApiHttpInput.Limit(h),ApiHttpInput.Cursor(h)),static r=>r.Result??throw new InvalidDataException());
        services.AddApiBinding<ApiDocumentCreate,CreateApiDocumentRequest,ApiDocumentWriteResponse,ApiMutationOutcome>(Cap("documents.create","POST","/spaces/{spaceId:guid}/documents",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h,"spaceId"),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiDocumentUpdate,UpdateApiDocumentRequest,ApiDocumentWriteResponse,ApiMutationOutcome>(Cap("documents.update","PUT","/documents/{id:guid}",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiDocumentRestore,RestoreApiDocumentRequest,ApiDocumentWriteResponse,ApiMutationOutcome>(Cap("documents.restore-version","POST","/documents/{id:guid}/restore-version",true),static(i,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),i),static _=>throw new NotSupportedException());
        services.AddApiBinding<ApiEmptyInput,DeleteApiDocumentRequest,ApiDocumentWriteResponse,ApiMutationOutcome>(Cap("documents.delete","DELETE","/documents/{id:guid}",true),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h),ApiHttpInput.QueryId(h,"planId")),static _=>throw new NotSupportedException());
        services.AddApiToolset("DocumentsMiniApp");
    }
    private static ApiCapability Cap(string id,string method,string route,bool mutates=false)=>new("documents",1,id,1,"DocumentsMiniApp",id,method,route,id+".input.v1",id+".output.v1",mutates);
}

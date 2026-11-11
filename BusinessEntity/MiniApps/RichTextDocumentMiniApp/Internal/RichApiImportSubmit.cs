using System.Text.Json;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

internal sealed record RichImportJobInput(Guid TargetId,ApiRichImport Input);
internal sealed class RichApiImportSubmit(RichApiAccess access,ApiDomainScope scope,IDataProviderConnector data,BusRequestClient bus,ApiWriteContext write)
{
    public async Task<ApiJobAccepted> SubmitAsync(Guid targetId,ApiRichImport input,CancellationToken ct)
    {
        var id=write.Id("job");var existing=(await bus.SendAsync<ReadApiJobRequest,ReadApiJobResponse>(new(Guid.NewGuid(),id),ct)).Result;
        if(existing!=null)return new(existing.Public.Id,existing.Operation.OperationId,"/api/v1/jobs/"+id);
        if(input.Mode is not("create" or "replace" or "append"))throw new ArgumentException("Mode: create/replace/append.");RichTextTitleRules.NormalizeRichTextDocumentTitle(input.Title);
        var(entity,space)=await scope.EntityAsync(targetId,null,ct);ApiPermissionCheck check;
        if(input.Mode=="create")
        {if(input.BaseVersion.HasValue||entity.EntityType!=SpaceType.Id&&entity.EntityType!=FolderType.Id)throw new ArgumentException("Create требует Space/Folder без BaseVersion.");await access.ParentAsync(space,targetId==space?null:targetId,ct);check=new("AdminItems",targetId,space);}
        else
        {if(input.BaseVersion is not >0||input.IsPublic.HasValue)throw new ArgumentException("Replace/append требуют BaseVersion, без изменения common flag.");var(_,_,decision)=await access.ContextAsync(targetId,ct);if(!decision.CanEditDraft)throw new UnauthorizedAccessException();await access.ReadAsync(targetId,input.BaseVersion,true,ct);check=new("EditDraft",targetId,space);}
        var ready=(await bus.SendAsync<ReadApiUploadLeaseRequest,ReadApiUploadLeaseResponse>(new(Guid.NewGuid(),input.ReadyUploadId,"rich.book"),ct)).Upload??throw new InvalidDataException();if(ready.TargetId!=targetId||ready.SpaceId!=space)throw new ArgumentException("Вход другого контекста.");
        var authority=(await bus.SendAsync<CaptureApiJobAuthorizationRequest,CaptureApiJobAuthorizationResponse>(new(Guid.NewGuid(),"rich-documents.import"),ct)).Result??throw new UnauthorizedAccessException();
        var result=await bus.SendAsync<CreateApiJobRequest,CreateApiJobResponse>(new(Guid.NewGuid(),id,"RichTextDocumentMiniApp","rich.import.v1",JsonSerializer.Serialize(new RichImportJobInput(targetId,input)),space,authority,[check],[ready.Id]),ct);return result.Result??throw new InvalidDataException();
    }
}

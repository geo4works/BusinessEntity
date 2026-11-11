using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Services;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

internal sealed class RichApiImportWorker(BusinessEntityHelper graph,IDataProviderConnector data,RichApiAccess access,BusRequestClient bus,ApiWriteContext write,RichBookPreparedWriter prepared)
{
    public async Task RunAsync(Guid jobId,CancellationToken ct)
    {
        var job=(await bus.SendAsync<ReadApiJobRequest,ReadApiJobResponse>(new(Guid.NewGuid(),jobId),ct)).Result??throw new KeyNotFoundException();
        if(job.Public.Owner!="RichTextDocumentMiniApp"||job.Public.Kind!="rich.import.v1"||job.Operation.OperationId!=write.OperationId||job.Public.State!="Running"||job.CancelRequested)throw new UnauthorizedAccessException();
        var request=JsonSerializer.Deserialize<RichImportJobInput>(job.InputJson)??throw new InvalidDataException();var input=request.Input;var space=job.Public.SpaceId;var id=input.Mode=="create"?write.Id("article"):request.TargetId;
        EntitySaveBatch batch;ApiPermissionCheck check;Guid[] baseline=[];
        if(input.Mode=="create")
        {
            var target=await data.GetByIdAsync(request.TargetId,ct)??throw new KeyNotFoundException();if(target.EntityType!=SpaceType.Id&&target.EntityType!=FolderType.Id)throw new ArgumentException("Неверный родитель.");var(parent,_)=await access.ParentAsync(space,request.TargetId==space?null:request.TargetId,ct);
            var plan=await graph.PrepareChildAsync(parent,RichTextDocumentType.Id,input.Title,id,write.Id("contains"),ct:ct);if(input.IsPublic.HasValue)plan.Entity.IsPublic=input.IsPublic.Value;batch=plan.Batch with{OperationId=write.OperationId};check=new("AdminItems",parent.Id,space);
        }
        else
        {
            var(_,actual,decision)=await access.ContextAsync(id,ct);if(!decision.CanEditDraft||actual!=space)throw new UnauthorizedAccessException();var(_,_,snapshot)=await access.ReadAsync(id,input.BaseVersion,true,ct);snapshot.Name=RichTextTitleRules.NormalizeRichTextDocumentTitle(input.Title);batch=new(write.OperationId,"RichTextDocumentMiniApp",null){Payloads=[new(id,input.BaseVersion!.Value,snapshot)]};check=new("EditDraft",id,space);
            if(input.Mode=="append")baseline=(await data.GetVersionChunkMembershipAsync(id,input.BaseVersion!.Value,ct)).Select(x=>x.ChunkId).ToArray();else if(input.Mode!="replace")throw new ArgumentException("Неизвестный mode.");
        }
        var ready=await bus.SendAsync<ReadApiUploadLeaseRequest,ReadApiUploadLeaseResponse>(new(Guid.NewGuid(),input.ReadyUploadId,"rich.book"),ct);if(ready.Upload?.TargetId!=request.TargetId||ready.Upload.SpaceId!=space)throw new ArgumentException("Вход другого контекста.");var set=await prepared.PrepareAsync(jobId,id,ready,ct);var order=baseline.Concat(set.Index.Select(x=>x.Id)).ToArray();if(order.Length>100000)throw new ArgumentException("Состав статьи превышает 100000 чанков.");
        var payload=batch.Payloads.Single();batch=write.Attach(batch with{Payloads=[payload with{ChangedChunks=null,ChunkOrder=order,PreparedChunkSetId=set.Id}]},id,space,[check]);batch=batch with{ApiOperation=batch.ApiOperation! with{ConsumeUploads=[ready.Upload.Id]}};
        await bus.SendAsync<SaveEntityBatchRequest,SaveEntityBatchResponse>(new(Guid.NewGuid(),batch),ct,TimeSpan.FromHours(2));
    }
}

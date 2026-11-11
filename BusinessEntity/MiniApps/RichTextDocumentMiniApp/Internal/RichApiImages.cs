using BusinessEntity.Core.Chunks;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

internal sealed class RichApiImages(RichApiAccess access,IDataProviderConnector data,BusRequestClient bus,IApiStagingDataPlane plane,ApiWriteContext write,RichTextEditService editor)
{
    public async Task AttachAsync(Guid id,ApiRichImageAttach input,CancellationToken ct)
    {
        var(_,space,decision)=await access.ContextAsync(id,ct);if(!decision.CanEditDraft||!decision.CanViewDraft)throw new UnauthorizedAccessException();var(_,_,snapshot)=await access.ReadAsync(id,input.BaseVersion,true,ct);
        if(input.BaseVersion<1||input.Width is <0 or >16384||input.Height is <0 or >16384||input.AltText==null||input.AltText.Length>8000)throw new ArgumentException("Неправильные параметры изображения.");
        var ready=await bus.SendAsync<ReadApiUploadLeaseRequest,ReadApiUploadLeaseResponse>(new(Guid.NewGuid(),input.ReadyUploadId,"rich.image"),ct);var upload=ready.Upload??throw new InvalidDataException();
        if(upload.TargetId!=id||upload.SpaceId!=space)throw new ArgumentException("Загрузка относится к другой статье.");
        await using(var stream=plane.OpenRead(ready.LeaseId??throw new InvalidDataException())){var header=new byte[12];var count=await stream.ReadAsync(header,ct);RichImageUploadPurpose.ValidateSignature(header.AsSpan(0,count),upload.ContentType);}
        var order=(await data.GetVersionChunkMembershipAsync(id,input.BaseVersion,ct)).Select(x=>x.ChunkId).ToList();var index=input.AfterChunkId is {} after?order.IndexOf(after)+1:order.Count;
        if(input.AfterChunkId.HasValue&&index==0)throw new ArgumentException("AfterChunkId отсутствует в базе автора.");
        var file=write.Id("image").ToString("N");var chunk=new BusinessEntityDataChunk{Id=write.Id("chunk:image"),BusinessEntityId=id,SortOrder=index,Blocks=[new(){Kind="image",ImageId=file,DisplayVariant="original",AltText=input.AltText,Width=input.Width,Height=input.Height}]};order.Insert(index,chunk.Id);
        var batch=await editor.PrepareSnapshotBatchAsync(write.OperationId,id,input.BaseVersion,input.Title??snapshot.Name,"",[chunk],order,ct);
        await data.SaveAsync(write.Attach(batch with{Files=[new(id,file,"original",upload.FileName,upload.ContentType,[],UploadId:upload.Id)]},id,space,[new("EditDraft",id,space),new("History",id,space)]),ct);
    }
    public async Task<ApiFileDownload> DownloadAsync(DownloadApiRichImageRequest request,CancellationToken ct)
    {
        var(_,space,snapshot)=await access.ReadAsync(request.DocumentId,request.Version,false,ct);
        if(!Guid.TryParse(request.FileId,out var file)||file==Guid.Empty||request.Variant!="original")throw new ArgumentException("Неверный файл/variant.");
        var response=await bus.SendAsync<OpenApiFileLeaseRequest,OpenApiFileLeaseResponse>(new(Guid.NewGuid(),request.DocumentId,file.ToString("N"),request.Variant,snapshot.Version,space,"rich-documents.images.download",[new("Read",request.DocumentId,space)]),ct);return response.Result??throw new InvalidDataException();
    }
}

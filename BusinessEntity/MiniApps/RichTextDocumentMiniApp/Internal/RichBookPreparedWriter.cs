using System.Text.Json;
using BusinessEntity.Core.Chunks;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

internal sealed class RichBookPreparedWriter(HtmlToRichTextBlocksConverter html,IApiStagingDataPlane plane,BusRequestClient bus,ApiWriteContext write,RichTextDocumentImportService importer)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)};
    public async Task<ApiPreparedChunkSet> PrepareAsync(Guid jobId,Guid entityId,ReadApiUploadLeaseResponse ready,CancellationToken ct)
    {
        var upload=ready.Upload??throw new InvalidDataException();var opened=await bus.SendAsync<OpenApiPreparedChunksRequest,OpenApiPreparedChunksResponse>(new(Guid.NewGuid(),write.Id("prepared-chunks"),entityId,upload.Id),ct);if(opened.Existing!=null)return opened.Existing;var lease=opened.WriteLeaseId??throw new InvalidDataException();var sealedSet=false;var targetChars=Math.Clamp(await importer.ResolveRichTextChunkCharLimitAsync(ct),1000,65536);
        try
        {
            var blocks=new List<BusinessEntityDataBlock>();int characters=0,count=0;long reported=0;
            await using(var input=plane.OpenRead(ready.LeaseId??throw new InvalidDataException()))
            await using(var output=plane.OpenPreparedWrite(lease))
            {
                await foreach(var fragment in RichBookFragments.ReadAsync(upload.FileName,input,ct))
                {
                    var safe=RichTextApiHtml.Sanitize(fragment);var converted=await html.ConvertHtmlAsync(safe,ct);if(converted.Files.Count>0)throw new InvalidDataException("API importer не принимает внешние файлы.");
                    foreach(var block in converted.Blocks){if(block.Html.Length>65536)throw new InvalidDataException("Блок книги превышает лимит.");if(blocks.Count>0&&(blocks.Count>=128||characters+block.Html.Length>targetChars))await FlushAsync();blocks.Add(block);characters+=block.Html.Length;}
                    if(input.Position-reported>=1024*1024){reported=input.Position;await ProgressAsync(jobId,reported,count,ct);}
                }
                if(blocks.Count>0||count==0)await FlushAsync();await output.FlushAsync(ct);await ProgressAsync(jobId,upload.Length,count,ct);
                async Task FlushAsync(){await JsonSerializer.SerializeAsync(output,new ApiStagedChunk(write.Id("chunk:book:"+count),blocks),Json,ct);await output.WriteAsync(new byte[]{10},ct);count++;blocks.Clear();characters=0;if(count>100000)throw new InvalidDataException("Слишком много чанков.");}
            }
            var result=(await bus.SendAsync<SealApiPreparedChunksRequest,SealApiPreparedChunksResponse>(new(Guid.NewGuid(),lease),ct,TimeSpan.FromMinutes(10))).Result??throw new InvalidDataException();sealedSet=true;return result;
        }
        finally{if(!sealedSet)try{await bus.SendAsync<AbandonApiPreparedChunksRequest,AbandonApiPreparedChunksResponse>(new(Guid.NewGuid(),lease),CancellationToken.None);}catch{/* Только собственный tmp, также истекает lease. */}}
    }
    private async Task ProgressAsync(Guid id,long bytes,int chunks,CancellationToken ct)
    {
        var status=(await bus.SendAsync<UpdateApiJobRequest,UpdateApiJobResponse>(new(Guid.NewGuid(),id,"Running",bytes,chunks),ct)).Result??throw new KeyNotFoundException();var job=(await bus.SendAsync<ReadApiJobRequest,ReadApiJobResponse>(new(Guid.NewGuid(),id),ct)).Result??throw new KeyNotFoundException();
        if(job.CancelRequested||status.State!="Running")throw new PublicOperationException("job.cancelled_conflict","Работа остановлена.");
    }
}

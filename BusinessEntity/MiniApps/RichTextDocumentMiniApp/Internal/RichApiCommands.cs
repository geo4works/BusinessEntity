using BusinessEntity.Core.Chunks;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Services;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Full author order/delta использует тот же assembler, что редактор; чужой latest не подмешивается.
internal sealed class RichApiCommands(BusinessEntityHelper graph,IDataProviderConnector data,RichApiAccess access,RichApiBlocks blocks,ApiWriteContext write,RichTextEditService editor)
{
    public async Task CreateAsync(Guid spaceId,ApiRichCreate input,CancellationToken ct)
    {
        Schema(input.SchemaVersion);var(parent,space)=await access.ParentAsync(spaceId,input.ParentId,ct);var title=RichTextTitleRules.NormalizeRichTextDocumentTitle(input.Title);var id=write.Id("article");
        var plan=await graph.PrepareChildAsync(parent,RichTextDocumentType.Id,title,id,write.Id("contains"),ct:ct);if(input.IsPublic.HasValue)plan.Entity.IsPublic=input.IsPublic.Value;
        var chunk=new BusinessEntityDataChunk{Id=write.Id("chunk:initial"),BusinessEntityId=id,Blocks=(await blocks.ConvertAsync(id,input.Blocks??[],ct)).ToList()};var initial=plan.Batch.Payloads.Single();
        var batch=plan.Batch with {OperationId=write.OperationId,Payloads=[initial with {ChangedChunks=[chunk],ChunkOrder=[chunk.Id]}]};await data.SaveAsync(write.Attach(batch,id,space,[new("AdminItems",parent.Id,space)]),ct);
    }
    public async Task SaveAsync(Guid id,ApiRichRevision input,CancellationToken ct)
    {
        Schema(input.SchemaVersion);var(_,space,decision)=await access.ContextAsync(id,ct);if(!decision.CanEditDraft)throw new UnauthorizedAccessException();
        if(input.BaseVersion<1||input.ChunkOrder==null||input.Changes==null||input.ChunkOrder.Length>100000||input.Changes.Length>128)throw new ArgumentException("Нужны BaseVersion, полный ChunkOrder и ограниченный Changes.");
        if(await data.GetDataVersionAsync<Contracts.Entities.RichTextDocument>(id,input.BaseVersion,ct)==null)throw new KeyNotFoundException();var baseline=(await data.GetVersionChunkMembershipAsync(id,input.BaseVersion,ct)).Select(x=>x.ChunkId).ToHashSet();
        Guid Resolve(string key){if(key!=null&&key.StartsWith("new:",StringComparison.Ordinal)&&System.Text.RegularExpressions.Regex.IsMatch(key,"^new:[A-Za-z0-9_.-]{1,64}$"))return write.Id("chunk:"+key);if(Guid.TryParse(key,out var existing)&&baseline.Contains(existing))return existing;throw new ArgumentException("Chunk ID отсутствует в базе автора или неверен new:key.");}
        var order=input.ChunkOrder.Select(Resolve).ToArray();if(order.Distinct().Count()!=order.Length)throw new ArgumentException("Повтор Chunk ID в полном порядке.");var changed=new List<BusinessEntityDataChunk>();
        foreach(var chunk in input.Changes){var key=Resolve(chunk.Id);var index=Array.IndexOf(order,key);if(index<0)throw new ArgumentException("Changes чанк отсутствует в ChunkOrder.");changed.Add(new(){Id=key,BusinessEntityId=id,SortOrder=index,Blocks=(await blocks.ConvertAsync(id,chunk.Blocks,ct)).ToList()});}
        if(changed.Select(x=>x.Id).Distinct().Count()!=changed.Count)throw new ArgumentException("Повтор Changes ID.");
        var batch=await editor.PrepareSnapshotBatchAsync(write.OperationId,id,input.BaseVersion,input.Title,input.VersionDescription??string.Empty,changed,order,ct);await data.SaveAsync(write.Attach(batch,id,space,[new("EditDraft",id,space)]),ct);
    }
    private static void Schema(int version){if(version!=1)throw new NotSupportedException("Поддерживается block schema1.");}
}

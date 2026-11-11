using System.Globalization;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Headless чтение не обращается к editor/viewport и фиксирует версию каждой следующей части.
internal sealed class RichApiReader(RichApiAccess access,RichTextDocumentReader reader,IDataProviderConnector data,ApiDomainScope scope,ApiPagination pages)
{
    public async Task<ApiRichDocument> ReadAsync(ReadApiRichRequest request,CancellationToken ct)
    {var(entity,space,snapshot)=await access.ReadAsync(request.EntityId,request.Version,request.RequireHistory,ct);return new(entity.Id,space,snapshot.Name,snapshot.Version,entity.PublishedVersion,entity.IsPublic,(await data.GetVersionChunkMembershipAsync(entity.Id,snapshot.Version,ct)).Count);}
    public async Task<ApiRichContent> ContentAsync(ReadApiRichContentRequest request,CancellationToken ct)
    {
        if(request.Limit is <1 or >100)throw new ArgumentException("limit должен быть 1–100.");var session=await scope.CurrentAsync(ct);var query="rich.content.v1:"+request.EntityId;var cursor=pages.Decode(request.Cursor,session,query);var version=request.Version;long start=0;
        if(cursor!=null){if(!int.TryParse(cursor.Snapshot,NumberStyles.None,CultureInfo.InvariantCulture,out var pinned)||!long.TryParse(cursor.After,NumberStyles.None,CultureInfo.InvariantCulture,out var last))throw new ApiFault(400,"api.cursor_invalid","Неверный cursor.");if(version.HasValue&&version!=pinned)throw new ApiFault(409,"api.cursor_conflict","Курсор закреплён за другой версией.");version=pinned;start=checked(last+1);}
        var(_,_,snapshot)=await access.ReadAsync(request.EntityId,version,false,ct);var count=await reader.GetChunkCountAsync(request.EntityId,snapshot.Version,ct);
        if(start>=count)return new(request.EntityId,snapshot.Version,[],null);
        var window=await reader.GetChunkWindowAsync(request.EntityId,start,request.Limit,snapshot.Version,ct);var chunks=window.Chunks.Select(c=>new ApiRichChunk(c.Id,c.SortOrder,c.Blocks.Select(RichApiBlocks.View).ToArray())).ToArray();
        var next=window.HasNext?pages.After(session,query,window.EndSortOrder.ToString(CultureInfo.InvariantCulture),snapshot.Version.ToString(CultureInfo.InvariantCulture)):null;return new(request.EntityId,snapshot.Version,chunks,next);
    }
    public async Task<ApiPage<ApiRichVersion>> VersionsAsync(ListApiRichVersionsRequest request,CancellationToken ct)
    {
        await access.ReadAsync(request.EntityId,null,true,ct);var versions=(await data.GetDataVersionsAsync(request.EntityId,ct)).Select(x=>new ApiRichVersion(x.Version,x.Name,x.SavedByUserId,x.CreatedDate,x.VersionDescription));return pages.Page(versions,x=>x.Version.ToString("D10",CultureInfo.InvariantCulture),await scope.CurrentAsync(ct),"rich.versions.v1:"+request.EntityId,request.Cursor,request.Limit);
    }
    public async Task<ApiRichPublication> PublicationAsync(Guid id,CancellationToken ct)
    {var(entity,_,_)=await access.ReadAsync(id,null,false,ct);return new(entity.PublishedVersion);}
}

using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

internal sealed class MediaApiReader(MediaServerService media,ApiDomainScope scope,IUserConnector users,ApiPagination pages,BusRequestClient bus)
{
    public async Task<ApiMediaVideo> ReadAsync(Guid id,CancellationToken ct)
    {var(_,space)=await scope.EntityAsync(id,MediaVideoType.Id,ct);if(!(await users.GetEntityContentAccessAsync(id,ct)).CanRead)throw new UnauthorizedAccessException();return View(await media.GetEditableVideoDataAsync(id,ct),space);}
    public async Task<ApiPage<ApiMediaVideo>> ListAsync(ListApiMediaRequest request,CancellationToken ct)
    {
        await scope.EntityAsync(request.SpaceId,SpaceType.Id,ct);if(request.Search?.Length>240)throw new ArgumentException("Поиск слишком длинный.");var result=new List<ApiMediaVideo>();
        foreach(var video in await media.GetVideosAsync(request.SpaceId,request.Search,ct))if((await users.GetEntityContentAccessAsync(video.Id,ct)).CanRead)result.Add(View(await media.GetEditableVideoDataAsync(video.Id,ct),request.SpaceId));
        return pages.Page(result,static x=>x.Id.ToString("N"),await scope.CurrentAsync(ct),$"media:{request.SpaceId:N}:{request.Search}",request.Cursor,request.Limit);
    }
    public async Task<ApiFileDownload> DownloadAsync(Guid id,CancellationToken ct)
    {var video=await ReadAsync(id,ct);var r=await bus.SendAsync<OpenApiFileLeaseRequest,OpenApiFileLeaseResponse>(new(Guid.NewGuid(),id,"video","original",null,video.SpaceId,"media-videos.download",[new("Read",id,video.SpaceId)]),ct);return r.Result??throw new InvalidDataException();}
    private static ApiMediaVideo View(MediaVideo v,Guid space)=>new(v.Id,space,v.DisplayName,v.Comment,v.FileName,v.ContentType,v.OriginalSizeBytes,v.DurationSeconds,v.UploadedByUserId,v.UploadedDate,$"/api/v1/media-videos/{v.Id:D}/file");
}

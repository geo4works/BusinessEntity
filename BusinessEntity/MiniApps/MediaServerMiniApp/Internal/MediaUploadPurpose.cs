using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

internal sealed class MediaUploadPurpose:IApiUploadPurpose
{
    public string Id=>"media.video";public string OwnerMiniAppId=>"MediaServerMiniApp";public string RequiredCapability=>"media-videos.create";public long MaxBytes=>1024L*1024*1024;public bool CreatesEntity=>true;
    public ApiPermissionCheck AuthorizeTarget(BusinessEntityTypeEnum type,Guid targetId,Guid spaceId)=>type==SpaceType.Id&&targetId==spaceId?new("AdminItems",targetId,spaceId):throw new ArgumentException("Видео загружается в явно указанное пространство.");
    public void ValidateFormat(string name,string mime)
    {if(!mime.StartsWith("video/",StringComparison.OrdinalIgnoreCase)||!new[]{".mp4",".webm",".ogv",".ogg",".mov",".m4v"}.Contains(Path.GetExtension(name).ToLowerInvariant()))throw new NotSupportedException("Неподдерживаемый формат видео.");}
}

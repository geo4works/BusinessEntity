using BusinessEntity.Core.Chunks;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Внешний формат отделён от storage/UI блоков и проверяет все ссылки до Save.
internal sealed class RichApiBlocks(IDataProviderConnector data,IUserConnector users,ApiDomainScope scope)
{
    public async Task<IReadOnlyList<BusinessEntityDataBlock>> ConvertAsync(Guid entityId,IReadOnlyList<ApiRichBlock>? input,CancellationToken ct,IReadOnlySet<string>? preparedImages=null)
    {
        if(input==null||input.Count>256||input.Sum(x=>x.Html?.Length??0)>65536)throw new ArgumentException("Чанк ограничен 256 блоками/65536 символами HTML.");
        var result=new List<BusinessEntityDataBlock>();
        foreach(var block in input)
        {
            var item=new BusinessEntityDataBlock{Kind=block.Kind};
            switch(block.Kind)
            {
                case "paragraph":case "heading":case "list":case "code":case "table":
                    if(block.Kind=="heading"&&block.Level is <1 or >6)throw new ArgumentException("Heading level должен быть 1–6.");item.Level=block.Kind=="heading"?block.Level:0;item.Html=RichTextApiHtml.Sanitize(block.Html);break;
                case "image":
                    if(!Guid.TryParse(block.AttachmentId,out var image)||image==Guid.Empty||!System.Text.RegularExpressions.Regex.IsMatch(block.Variant??"","^[a-zA-Z0-9_-]{1,32}$"))throw new ArgumentException("Неверная attachment reference.");
                    var fileId=image.ToString("N");if(preparedImages?.Contains(fileId)!=true&&await data.GetStoredFileAsync(entityId,fileId,block.Variant,ct)==null)throw new KeyNotFoundException("Файл не принадлежит этой статье.");
                    if(block.Width is <0 or >16384||block.Height is <0 or >16384)throw new ArgumentException("Неверный размер изображения.");item.ImageId=fileId;item.DisplayVariant=block.Variant;item.AltText=block.AltText??string.Empty;item.Width=block.Width;item.Height=block.Height;break;
                case "video":
                    if(!Guid.TryParse(block.VideoId,out var video)||video==Guid.Empty)throw new ArgumentException("Неверный VideoId.");await scope.EntityAsync(video,global::BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.MediaVideoType.Id,ct);if(!(await users.GetEntityContentAccessAsync(video,ct)).CanRead)throw new UnauthorizedAccessException();item.VideoId=video.ToString();break;
                default:throw new ArgumentException("Неизвестный block kind schema v1.");
            }
            result.Add(item);
        }
        return result;
    }
    // Public DTO не отдаёт физические row versions, EF, paths или HTML renderer state.
    public static ApiRichBlock View(BusinessEntityDataBlock b)=>new(b.Kind,b.Html,b.Level,string.IsNullOrEmpty(b.ImageId)?null:b.ImageId,b.DisplayVariant,string.IsNullOrEmpty(b.VideoId)?null:b.VideoId,b.AltText,b.Width,b.Height);
}

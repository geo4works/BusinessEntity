using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

internal sealed class RichImageUploadPurpose:IApiUploadPurpose
{
    public string Id=>"rich.image";public string OwnerMiniAppId=>"RichTextDocumentMiniApp";public string RequiredCapability=>"rich-documents.images.attach";public long MaxBytes=>20L*1024*1024;public bool CreatesEntity=>false;
    public ApiPermissionCheck AuthorizeTarget(BusinessEntityTypeEnum type,Guid targetId,Guid spaceId)=>type==RichTextDocumentType.Id?new("EditDraft",targetId,spaceId):throw new ArgumentException("Изображение загружается для существующей статьи.");
    public void ValidateFormat(string name,string mime)
    {
        var valid=(Path.GetExtension(name).ToLowerInvariant(),mime.ToLowerInvariant()) switch{(".png","image/png") or (".jpg" or ".jpeg","image/jpeg") or (".gif","image/gif") or (".webp","image/webp")=>true,_=>false};
        if(!valid)throw new NotSupportedException("Поддерживаются PNG/JPEG/GIF/WebP.");
    }
    internal static void ValidateSignature(ReadOnlySpan<byte> header,string mime)
    {
        var valid=mime switch{"image/png"=>header.StartsWith(new byte[]{137,80,78,71,13,10,26,10}),"image/jpeg"=>header.StartsWith(new byte[]{255,216,255}),"image/gif"=>header.StartsWith("GIF87a"u8)||header.StartsWith("GIF89a"u8),"image/webp"=>header.Length>=12&&header[..4].SequenceEqual("RIFF"u8)&&header.Slice(8,4).SequenceEqual("WEBP"u8),_=>false};
        if(!valid)throw new NotSupportedException("Сигнатура изображения не соответствует формату.");
    }
}

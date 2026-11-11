using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

internal sealed class RichBookUploadPurpose:IApiUploadPurpose
{
    public string Id=>"rich.book";public string OwnerMiniAppId=>"RichTextDocumentMiniApp";public string RequiredCapability=>"rich-documents.import";public long MaxBytes=>100L*1024*1024;public bool CreatesEntity=>true;
    public ApiPermissionCheck AuthorizeTarget(BusinessEntityTypeEnum type,Guid targetId,Guid spaceId)=>type==RichTextDocumentType.Id?new("EditDraft",targetId,spaceId):type==SpaceType.Id||type==FolderType.Id?new("AdminItems",targetId,spaceId):throw new ArgumentException("Книга импортируется в статью или разрешённого родителя.");
    public void ValidateFormat(string name,string mime)
    {if(!new[]{".txt",".md",".markdown",".html",".htm"}.Contains(Path.GetExtension(name).ToLowerInvariant())||!new[]{"text/plain","text/markdown","text/html","application/octet-stream"}.Contains(mime.ToLowerInvariant()))throw new NotSupportedException("Поддерживаются TXT/Markdown/HTML.");}
}

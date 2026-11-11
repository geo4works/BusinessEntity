using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using Document=BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Internal;

// Наружу передаётся один разрешённый снимок, а не смешение current header и старого текста.
internal sealed class DocumentApiReader(IDataProviderConnector data,IUserConnector users,ApiDomainScope scope,ApiPagination pagination)
{
    public async Task<ApiDocument> ReadAsync(Guid id,int? version,CancellationToken ct)
    {
        var(entity,space)=await scope.EntityAsync(id,DocumentType.Id,ct);var access=await users.GetEntityContentAccessAsync(id,ct);
        if(version.HasValue?!access.CanViewDraft:!access.CanRead)throw new UnauthorizedAccessException();
        if(version is <1)throw new ArgumentException("Version должен быть положительным.");
        var snapshot=version.HasValue?await data.GetDataVersionAsync<Document>(id,version.Value,ct):await data.GetDataAsync<Document>(id,ct);
        if(snapshot==null)throw new KeyNotFoundException();return new(id,space,snapshot.Name,snapshot.Text,snapshot.Version,entity.IsPublic);
    }
    public async Task<ApiPage<ApiDocumentVersion>> VersionsAsync(ListApiDocumentVersionsRequest request,CancellationToken ct)
    {
        await scope.EntityAsync(request.DocumentId,DocumentType.Id,ct);if(!(await users.GetEntityContentAccessAsync(request.DocumentId,ct)).CanViewDraft)throw new UnauthorizedAccessException();
        var versions=(await data.GetDataVersionsAsync(request.DocumentId,ct)).Select(x=>new ApiDocumentVersion(x.Version,x.Name,x.SavedByUserId,x.CreatedDate,x.VersionDescription));
        return pagination.Page(versions,x=>x.Version.ToString("D10",System.Globalization.CultureInfo.InvariantCulture),await scope.CurrentAsync(ct),"documents.versions.v1:"+request.DocumentId,request.Cursor,request.Limit);
    }
}

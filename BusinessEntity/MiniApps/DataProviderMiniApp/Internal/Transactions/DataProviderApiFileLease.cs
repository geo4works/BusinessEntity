using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;

internal sealed partial class DataProviderTransactionService
{
    public async Task<ApiFileDownload> OpenFileLeaseAsync(OpenApiFileLeaseRequest request,CancellationToken ct=default)
    {
        var actor=request.Context;RequireApiActor(actor);await _committedGraph.AuthorizeApiAsync(new(Guid.Empty,"DataProviderMiniApp",request.CapabilityId,1,""),new(Guid.Empty,"",request.EntityId,request.SpaceId,request.Checks),actor,ct);
        await using var db=new Storage.KmsBusinessEntityDbContext(_options);
        if(request.Version.HasValue&&!await db.VersionFileReferences.AsNoTracking().AnyAsync(x=>x.SourceEntityId==request.EntityId&&x.Version==request.Version&&x.FileEntityId==request.EntityId&&x.FileId==request.FileId&&x.Variant==request.Variant,ct))throw new KeyNotFoundException();
        var row=await db.EntityFiles.AsNoTracking().SingleOrDefaultAsync(x=>x.EntityId==request.EntityId&&x.FileId==request.FileId&&x.Variant==request.Variant,ct)??throw new KeyNotFoundException();
        return new(_files.ApiStaging.BeginStoredRead(_files.ResolvePath(row)),row.FileName,row.ContentType,row.Length);
    }
}

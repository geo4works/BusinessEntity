using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Последняя проверка делается при занятом DP writer: предшествующий отзыв роли/допуска уже виден в SQL.
internal sealed class UserApiCommitAuthorizer(UserApiSessions sessions,UserSecurityAuthority authority,UserAccessService access)
{
    // Вызывает собственные policy services напрямую; никаких чужих Internal или доверия к имени admin.
    public async Task AuthorizeAsync(AuthorizeApiCommitRequest request,CancellationToken ct)
    {
        var actor=request.Context;
        if(actor.ExecutionKind!=BusExecutionKind.Api||actor.IsSystem||actor.ApiSessionId is not {} id||request.Checks.Count==0)throw new UnauthorizedAccessException("Не объявлены права canonical API операции.");
        var session=await sessions.ValidateAsync(id,request.CapabilityId,ct);
        if(session.UserId!=actor.UserId||session.ClientId!=actor.ApiClientId||session.Epoch!=actor.AuthEpoch)throw new UnauthorizedAccessException();
        if(request.SpaceId.HasValue&&!session.Spaces.Allows(request.SpaceId.Value))throw new ApiFault(403,"api.space_denied","Пространство не входит в допуск.");
        foreach(var check in request.Checks)
        {
            if(check.RequireAllSpaces&&!session.Spaces.AllSpaces)throw new ApiFault(403,"api.space_denied","Операция требует допуска ко всем пространствам.");
            if(check.Permission=="Self"){if(check.EntityId.HasValue&&check.EntityId!=session.UserId)throw new UnauthorizedAccessException();continue;}
            if(check.Permission=="GlobalAdmin"){await authority.RequireGlobalAdminAsync(ct);continue;}
            var permissions=check.EntityId.HasValue?await access.GetCurrentUserPermissionsForEntityAsync(check.EntityId.Value,ct):check.SpaceId.HasValue?await access.GetCurrentUserPermissionsForSpaceAsync(check.SpaceId.Value,ct):throw new UnauthorizedAccessException();
            if(check.SpaceId.HasValue&&permissions.SpaceId!=check.SpaceId.Value)throw new ApiFault(409,"api.context_conflict","Принадлежность объекта пространству изменилась.");
            if(!session.Spaces.Allows(permissions.SpaceId))throw new ApiFault(403,"api.space_denied","Пространство объекта не входит в допуск.");
            var allowed=check.Permission switch {"AdminSpace"=>permissions.CanAdminSpace||permissions.CanGlobalAdmin,"AdminItems" or "Delete"=>permissions.CanAdminItems||permissions.CanAdminSpace||permissions.CanGlobalAdmin,_=>false};
            if(check.Permission is "Read" or "ViewDraft" or "History" or "EditDraft" or "PublishDraft" or "ChangeCommon")
            {
                if(check.EntityId is not {} entity)throw new UnauthorizedAccessException();var content=await access.GetContentAccessForEntityIdAsync(entity,ct);
                allowed=check.Permission switch {"Read"=>content.CanRead,"ViewDraft" or "History"=>content.CanViewDraft,"EditDraft"=>content.CanEditDraft,"PublishDraft"=>content.CanPublishDraft,"ChangeCommon"=>content.CanChangeCommonFlag,_=>false};
            }
            if(!allowed)throw new UnauthorizedAccessException("Предметное право отозвано или не назначено.");
        }
    }
}

using BusinessEntity.Services;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Messages;
using ReactiveUI;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Адаптер собственных адресных запросов миниаппа.
internal sealed class UserSessionMessageHandler : ScopedBusEndpoint
{
    private readonly UserGroupService UserGroupService;
    private readonly UserIdentityService UserIdentityService;

    // Использует сервис своего миниаппа и общий транспорт текущего scope.
    public UserSessionMessageHandler(UserGroupService userGroupService, UserIdentityService userIdentityService, IMessageBus bus, BusRequestScope scope, ILogger<UserSessionMessageHandler> logger)
        : base(bus, scope, logger) { UserGroupService = userGroupService; UserIdentityService = userIdentityService; }

    // Подключает предметные запросы один раз для своего адреса.
    protected override void SubscribeRequests()
    {
        Subscribe<GetCurrentUserRequest, GetCurrentUserResponse>(async request =>
        {
            var result = await UserIdentityService.GetCurrentUserAsync();
            return new GetCurrentUserResponse(request.RequestId, result);
        }, static (id, error) => new GetCurrentUserResponse(id, default, error));
        Subscribe<EnsureCurrentUserRequest, EnsureCurrentUserResponse>(async request =>
        {
            var result = await UserIdentityService.EnsureCurrentUserAsync();
            return new EnsureCurrentUserResponse(request.RequestId, result)
            { HasApplicationAdminAuthority = UserValueRules.IsAccessAdmin(await UserIdentityService.GetCurrentUserAsync()) };
        }, static (id, error) => new EnsureCurrentUserResponse(id, default, error));
        Subscribe<GetGroupsRequest, GetGroupsResponse>(async request =>
        {
            var result = await UserGroupService.GetCurrentUserGroupNamesAsync();
            return new GetGroupsResponse(request.RequestId, result);
        }, static (id, error) => new GetGroupsResponse(id, default, error));
        Subscribe<IsInGroupRequest, IsInGroupResponse>(async request =>
        {
            var result = await IsInGroupAsync(request.GroupName);
            return new IsInGroupResponse(request.RequestId, result);
        }, static (id, error) => new IsInGroupResponse(id, default, error));
    }

    // Проверяет группу по нормализованным данным текущей сессии.
    private async Task<bool> IsInGroupAsync(string groupName)
    {
        var groups = await UserGroupService.GetCurrentUserGroupNamesAsync();
        return groups.Any(group => string.Equals(group, groupName, StringComparison.OrdinalIgnoreCase));
    }
}

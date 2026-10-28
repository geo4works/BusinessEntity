using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.UserMiniApp.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.MiniApps.UserMiniApp.Facade;
using BusinessEntity.MiniApps.UserMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Repositories.EfPostgres;

namespace BusinessEntity.MiniApps.UserMiniApp.Registration
{
    // Регистрирует все публичные и внутренние компоненты user mini-app в DI.
    public static class UserMiniAppRegistration
    {
        // Подключает user mini-app, его connector, state, factory и message handler.
        public static IServiceCollection AddUserMiniApp(this IServiceCollection services)
        {
            services.AddScoped<UserMiniAppState>();
            services.AddScoped<BusinessEntityUserFactory>();
            services.AddScoped<UserIdentitySession>();
            services.AddScoped<AuthentikManagementClient>();
            services.AddScoped<IUserMiniAppRepository<UserDto>, UserDtoEfRepository>();
            services.AddScoped<IUserMiniAppRepository<UserPropertyDto>, UserPropertyDtoEfRepository>();
            services.AddScoped<IUserMiniAppRepository<UserRoleDto>, UserRoleDtoEfRepository>();
            services.AddScoped<IUserMiniAppRepository<UserGroupDto>, UserGroupDtoEfRepository>();
            services.AddScoped<IUserMiniAppRepository<UserGroupMemberDto>, UserGroupMemberDtoEfRepository>();
            services.AddScoped<IUserMiniAppRepository<UserRoleAssignmentDto>, UserRoleAssignmentDtoEfRepository>();
            services.AddScoped<UserSpaceContentAccessHelper>();
            services.AddScoped<UserMiniAppStartupAdminBootstrapper>();
            services.AddScoped<UserMiniAppService>();
            services.AddScoped<UserMiniAppMessageHandler>();
            services.AddScoped<IUserMiniApp, Facade.UserMiniApp>();
            services.AddScoped<IUserConnector, UserConnector>();

            services.AddBusEndpoint<UserMiniAppMessageHandler>(typeof(DeleteCurrentUserRequest), typeof(GetAdministrationUsersRequest), typeof(ReadAdministrationUsersFromAuthentikRequest), typeof(CreateAdministrationUserRequest), typeof(UpdateAdministrationUserRequest), typeof(DeleteAdministrationUserRequest), typeof(GetRolesRequest), typeof(CreateRoleRequest), typeof(UpdateRoleRequest), typeof(DeleteRoleRequest), typeof(GetUserGroupsRequest), typeof(CreateUserGroupRequest), typeof(UpdateUserGroupRequest), typeof(DeleteUserGroupRequest), typeof(GetUserGroupMembershipsRequest), typeof(UpdateUserGroupMembershipsRequest), typeof(GetRoleAssignmentSpacesRequest), typeof(GetRoleAssignmentsRequest), typeof(CreateRoleAssignmentRequest), typeof(DeleteRoleAssignmentRequest), typeof(GetProfileRequest), typeof(UpdateProfileRequest), typeof(GetRichDocBookmarksRequest), typeof(AddRichDocBookmarkRequest), typeof(DeleteRichDocBookmarkRequest), typeof(GetRichDocDisplayedLevelRequest), typeof(SaveRichDocDisplayedLevelRequest), typeof(GetCurrentUserPropertiesRequest), typeof(UpsertCurrentUserPropertyRequest), typeof(DeleteCurrentUserPropertyRequest), typeof(GetDocPrintPresetsRequest), typeof(SaveDocPrintPresetRequest), typeof(DeleteDocPrintPresetRequest), typeof(GetCurrentUserPermissionsForSpaceRequest), typeof(GetCurrentUserPermissionsForEntityRequest), typeof(GetCurrentUserContentAccessForEntityRequest), typeof(GetAnonymousPermissionsForSpaceRequest), typeof(GetAnonymousAccessibleSpacesRequest), typeof(GetAnonymousAccessibleDocumentsRequest));

            services.AddScoped<UserSessionMessageHandler>();

            services.AddBusEndpoint<UserSessionMessageHandler>(typeof(GetCurrentUserRequest), typeof(EnsureCurrentUserRequest), typeof(GetGroupsRequest), typeof(IsInGroupRequest));

            return services;
        }
    }
}

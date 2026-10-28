using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
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
            services.TryAddSingleton(provider => new DbContextOptionsBuilder<UserMiniAppDbContext>()
                .UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("DockerConnection")
                    ?? throw new InvalidOperationException("ConnectionStrings:DockerConnection обязателен.")).Options);
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

            services.AddMiniAppHost("UserMiniApp",
                new ModuleDefinition("UserMiniApp", "identity-bootstrap", "Identity", null, ModulePlacement.Headless, ModuleInstancePolicy.AppSingleton, Order: 10, OpenOnStartup: true),
                new ModuleDefinition("UserMiniApp", "defaults", "Пользователи", null, ModulePlacement.Headless, ModuleInstancePolicy.AppSingleton, Order: 20, OpenOnStartup: true),
            new ModuleDefinition("UserMiniApp", "User.Profile", "Профиль", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "user.profile", RequiredPermission: "Authenticated"),
            new ModuleDefinition("UserMiniApp", "User.AuthInfo", "Авторизация", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "user.authinfo", RequiredPermission: "Authenticated"),
            new ModuleDefinition("UserMiniApp", "User.Administration", "Пользователи", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "user.administration", RequiredPermission: "GlobalAdmin"),
            new ModuleDefinition("UserMiniApp", "User.Header", "Пользователь", null, ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "user.header", "TopBar", Order: 20, OpenOnStartup: true));
            services.AddModuleRuntime<IdentityStartupRuntime>("UserMiniApp:identity-bootstrap");
            services.AddModuleRuntime<UserStartupRuntime>("UserMiniApp:defaults");
            services.AddScoped<IModuleAccessPolicy, UserModuleAccessPolicy>();

        services.AddModuleComponent<global::BusinessEntity.MiniApps.UserMiniApp.Components.UserProfile>("user.profile");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.UserMiniApp.Components.UserAuthInfo>("user.authinfo");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.UserMiniApp.Components.UsersAdministration>("user.administration");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.UserMiniApp.Components.UserHeader>("user.header");
        services.AddMiniAppButtons(
            new ButtonDefinition("UserMiniApp", "nav.authinfo", "Nav.Main", "Авторизация", "oi oi-lock-locked", 10, RequiredPermission: "Authenticated", TargetRoute: "/authinfo"),
            new ButtonDefinition("UserMiniApp", "nav.profile", "Nav.Main", "Профиль", "oi oi-person", 20, RequiredPermission: "Authenticated", TargetRoute: "/profile"),
            new ButtonDefinition("UserMiniApp", "admin.users", "Administration.Navigation", "Пользователи", null, 30, RequiredPermission: "GlobalAdmin", TargetRoute: "/administration/users"));

        services.AddMiniAppButtons(
            new ButtonDefinition("UserMiniApp", "users.general", "User.Administration.Navigation", "Общая", Order: 0, RequiredPermission: "GlobalAdmin", TargetRoute: "/administration/users/general"),
            new ButtonDefinition("UserMiniApp", "users.access", "User.Administration.Navigation", "Права доступа", Order: 1, RequiredPermission: "GlobalAdmin", TargetRoute: "/administration/users/access"),
            new ButtonDefinition("UserMiniApp", "users.roles", "User.Administration.Navigation", "Роли", Order: 2, RequiredPermission: "GlobalAdmin", TargetRoute: "/administration/users/roles"),
            new ButtonDefinition("UserMiniApp", "users.groups", "User.Administration.Navigation", "Группы", Order: 3, RequiredPermission: "GlobalAdmin", TargetRoute: "/administration/users/groups"),
            new ButtonDefinition("UserMiniApp", "users.members", "User.Administration.Navigation", "Состав групп", Order: 4, RequiredPermission: "GlobalAdmin", TargetRoute: "/administration/users/members"));
        return services;
        }
        // Создаёт собственную схему User после нового DataProvider и проверяет её на последующих стартах.
        public static async Task InitializeUserStorageAsync(this IServiceProvider services, bool freshDatabase, CancellationToken ct)
        {
            await using var context = new UserMiniAppDbContext(services.GetRequiredService<DbContextOptions<UserMiniAppDbContext>>());
            await UserMiniAppStorageSchema.InitializeAsync(context, freshDatabase, ct);
        }
    }
}

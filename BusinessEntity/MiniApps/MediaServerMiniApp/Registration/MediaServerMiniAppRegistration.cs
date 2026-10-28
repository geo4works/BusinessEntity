using BusinessEntity.Infrastructure.Commands;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Internal;

namespace BusinessEntity.MiniApps.MediaServerMiniApp.Registration;

// Регистрация MediaServerMiniApp в основном DI приложения.
public static class MediaServerMiniAppRegistration
{
    public static IServiceCollection AddMediaServerMiniApp(this IServiceCollection services)
    {
        services.AddSingleton<MediaServerUploadJobRegistry>();
        services.AddSingleton<IMediaServerUploadJobTracker>(provider => provider.GetRequiredService<MediaServerUploadJobRegistry>());
        services.AddSingleton<MediaServerUploadManager>();
        services.AddSingleton<IMediaServerUploadManager>(provider => provider.GetRequiredService<MediaServerUploadManager>());
        services.AddScoped<MediaServerService>();
        services.AddScoped<IMediaServerService>(provider => provider.GetRequiredService<MediaServerService>());

        services.AddSingleton(new EntityTypeOwnership(BusinessEntityTypeEnum.MediaVideo, "MediaServerMiniApp"));
        services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(BusinessEntityTypeEnum.MediaVideo, true));
        services.AddSingleton(new EntityUrlPattern("/media-server-files/videos/"));
        services.AddMiniAppHost("MediaServerMiniApp", new ModuleDefinition("MediaServerMiniApp", "uploads", "Загрузки", null,
                ModulePlacement.Headless, ModuleInstancePolicy.AppSingleton, Order: 50, OpenOnStartup: true),
            new ModuleDefinition("MediaServerMiniApp", "MediaServer.Library", "Мультимедиа", null, ModulePlacement.Page, ModuleInstancePolicy.WindowSingleton, "media.library", RequiredPermission: "Authenticated"),
            new ModuleDefinition("MediaServerMiniApp", "MediaServer.Uploads", "Загрузки", null, ModulePlacement.ShellModuleHostArea, ModuleInstancePolicy.WindowSingleton, "media.uploads", "RightPanel", Order: 10, OpenOnStartup: true, RequiredPermission: "Authenticated"));
            services.AddSingleton(new ModuleRuntimeRegistration("MediaServerMiniApp:uploads", provider =>
                new HostedModuleRuntime(provider.GetRequiredService<MediaServerUploadManager>())));

        services.AddModuleComponent<global::BusinessEntity.MiniApps.MediaServerMiniApp.Components.MediaLibrary>("media.library");
        services.AddModuleComponent<global::BusinessEntity.MiniApps.MediaServerMiniApp.Components.MediaServerUploadWidget>("media.uploads");
        services.AddMiniAppButtons(
            new ButtonDefinition("MediaServerMiniApp", "nav.media", "Nav.Main", "Мультимедиа", "oi oi-video", 50, RequiredPermission: "Authenticated", TargetRoute: "/multimedia"));

        return services;
    }
}

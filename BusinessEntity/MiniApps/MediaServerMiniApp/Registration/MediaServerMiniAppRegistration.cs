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
        services.AddHostedService(provider => provider.GetRequiredService<MediaServerUploadManager>());
        services.AddScoped<MediaServerService>();
        services.AddScoped<IMediaServerService>(provider => provider.GetRequiredService<MediaServerService>());

        services.AddSingleton(new EntityTypeOwnership(BusinessEntityTypeEnum.MediaVideo, "MediaServerMiniApp"));
        services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(BusinessEntityTypeEnum.MediaVideo, true));
        services.AddSingleton(new EntityUrlPattern("/media-server-files/videos/"));
        return services;
    }
}

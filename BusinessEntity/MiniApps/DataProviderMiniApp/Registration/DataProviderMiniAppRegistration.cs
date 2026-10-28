using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Caching;
using BusinessEntity.Core.Saving;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.DataProviderMiniApp.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Registration
{
    /// <summary>
    /// Регистрирует все публичные и внутренние компоненты data-provider mini-app в DI.
    /// </summary>
    public static class DataProviderMiniAppRegistration
    {
        /// <summary>
        /// Подключает mini-app хранения данных, его connector, репозитории и message handler.
        /// </summary>
        // Регистрирует все зависимости mini-app в контейнере DI.
        public static IServiceCollection AddDataProviderMiniApp(this IServiceCollection services)
        {
            services.AddSingleton<StorageCommandMetrics>();
            services.AddSingleton<BoundedPayloadCache>();
            services.AddSingleton<DataProviderGraphCache>();
            services.TryAddSingleton(provider => new DbContextOptionsBuilder<KmsBusinessEntityDbContext>()
                .UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("DockerConnection")
                    ?? throw new InvalidOperationException("ConnectionStrings:DockerConnection обязателен.")).AddInterceptors(provider.GetRequiredService<StorageCommandMetrics>()).Options);
            services.TryAddSingleton<EntityUrlRewriter>();
            services.TryAddSingleton<GraphArchiveRemapper>();
            services.AddSingleton<ExportFileLeases>();
            // Регистрируем поэкземплярные payload-конвертеры для всех поддерживаемых typed business-data объектов.
            services.AddSingleton<IEntityDataStorageConverterFactory, EntityDataStorageConverterFactory>();
            services.AddSingleton<EntityDataStorageCodec>();
            services.AddSingleton<DataProviderMessageHandler>();
            services.AddSingleton<IStorageFaults, NoStorageFaults>();
            services.AddSingleton<ImmutableEntityFileStore>();
            services.AddSingleton<DataProviderSchema>();
            services.AddSingleton<DataProviderReadService>();
            services.AddSingleton<DataProviderCommitPublisher>();
            services.AddSingleton<EntityPersistencePolicies>();
            services.AddSingleton<CommittedGraphCoordinator>();
            services.AddSingleton<DataProviderTransactionService>();
            services.AddSingleton<DataProviderTransactionHandler>();
            services.AddBusEndpoint<DataProviderTransactionHandler>(typeof(ExportSubgraphRequest), typeof(ReleaseExportRequest), typeof(SaveEntityBatchRequest), typeof(GetOperationOutcomeRequest), typeof(GetStoredCommentsRequest), typeof(GetStoredPropertiesRequest), typeof(GetVersionChunkWindowRequest), typeof(GetStoredFileRequest), typeof(GetVersionChunkMembershipRequest));
            services.AddScoped<IDataProviderConnector, DataProviderConnector>();

            services.AddBusEndpoint<DataProviderMessageHandler>(typeof(QueryGraphRequest), typeof(GetPublishedEntityNamesRequest), typeof(GetDataProviderCacheStatisticsRequest), typeof(GetBusinessEntitiesRequest), typeof(GetBusinessEntityByIdRequest), typeof(GetBusinessEntityDataRequest), typeof(GetBusinessEntityDataVersionRequest), typeof(GetBusinessEntityDataVersionsRequest), typeof(GetAllRelationsRequest), typeof(GetRelationsRequest), typeof(GetRelationByIdRequest), typeof(GetRichTextEmbeddedFileRequest));

            services.AddMiniAppHost("DataProviderMiniApp", new ModuleDefinition("DataProviderMiniApp", "storage", "Хранилище", null,
                ModulePlacement.Headless, ModuleInstancePolicy.AppSingleton, Order: 0, OpenOnStartup: true));
            services.AddModuleRuntime<DataProviderStartupRuntime>("DataProviderMiniApp:storage");
            return services;
        }
        // Композиционный корень вызывает публичную инициализацию, не зная EF и таблиц владельца.
        public static async Task<bool> InitializeDataProviderStorageAsync(this IServiceProvider services, CancellationToken ct)
        {
            var fresh = await services.GetRequiredService<DataProviderSchema>().InitializeAsync(ct);
            await services.GetRequiredService<ImmutableEntityFileStore>().RecoverAsync(
                services.GetRequiredService<DbContextOptions<KmsBusinessEntityDbContext>>(), ct);
            services.GetRequiredService<ExportFileLeases>().RecoverAfterRestart();
            await services.GetRequiredService<DataProviderGraphCache>().InitializeAsync(ct);
            return fresh;
        }
    }
}

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
            services.TryAddSingleton(provider => new DbContextOptionsBuilder<KmsBusinessEntityDbContext>()
                .UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("DockerConnection")
                    ?? throw new InvalidOperationException("ConnectionStrings:DockerConnection обязателен.")).Options);
            services.TryAddSingleton<EntityUrlRewriter>();
            services.TryAddSingleton<GraphArchiveRemapper>();
            services.AddSingleton<ExportFileLeases>();
            // Подключаем Postgres-репозитории для всех DTO-хранилищ mini-app.
            // Регистрируем поэкземплярные payload-конвертеры для всех поддерживаемых typed business-data объектов.
            services.AddSingleton<IEntityDataStorageConverter, DocumentEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, FolderEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, MediaVideoEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, RichTextDocumentEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, SpaceEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, SysParametersEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverterFactory, EntityDataStorageConverterFactory>();
            services.AddSingleton<EntityDataStorageCodec>();
            services.AddSingleton<DataProviderMessageHandler>();
            services.AddSingleton<IStorageFaults, NoStorageFaults>();
            services.AddSingleton<ImmutableEntityFileStore>();
            services.AddSingleton<DataProviderSchema>();
            services.AddSingleton<DataProviderReadService>();
            services.AddSingleton<DataProviderCommitPublisher>();
            services.AddSingleton<DataProviderTransactionService>();
            services.AddSingleton<DataProviderTransactionHandler>();
            services.AddBusEndpoint<DataProviderTransactionHandler>(typeof(ExportSubgraphRequest), typeof(ReleaseExportRequest), typeof(SaveEntityBatchRequest), typeof(GetOperationOutcomeRequest), typeof(GetStoredCommentsRequest), typeof(GetStoredPropertiesRequest), typeof(GetVersionChunkWindowRequest), typeof(GetStoredFileRequest), typeof(GetVersionChunkMembershipRequest));
            services.AddScoped<IDataProviderConnector, DataProviderConnector>();

            services.AddBusEndpoint<DataProviderMessageHandler>(typeof(GetBusinessEntitiesRequest), typeof(GetBusinessEntityByIdRequest), typeof(GetBusinessEntityDataRequest), typeof(GetBusinessEntityDataVersionRequest), typeof(GetBusinessEntityDataVersionsRequest), typeof(GetAllRelationsRequest), typeof(GetRelationsRequest), typeof(GetRelationByIdRequest), typeof(GetRichTextEmbeddedFileRequest));

            return services;
        }
        // Композиционный корень вызывает публичную инициализацию, не зная EF и таблиц владельца.
        public static async Task<bool> InitializeDataProviderStorageAsync(this IServiceProvider services, CancellationToken ct)
        {
            var fresh = await services.GetRequiredService<DataProviderSchema>().InitializeAsync(ct);
            await services.GetRequiredService<ImmutableEntityFileStore>().RecoverAsync(
                services.GetRequiredService<DbContextOptions<KmsBusinessEntityDbContext>>(), ct);
            services.GetRequiredService<ExportFileLeases>().RecoverAfterRestart();
            return fresh;
        }
    }
}

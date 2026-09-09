using BusinessEntity.MiniApps.DataProviderMiniApp.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;
using BusinessEntity.MiniApps.DataProviderMiniApp.Repositories.EfPostgres;

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
            // Подключаем Postgres-репозитории для всех DTO-хранилищ mini-app.
            services.AddSingleton<IAsyncRepository<BusinessEntityDto>, BusinessEntityDtoEfPostgresRepository>();
            services.AddSingleton<IAsyncRepository<BusinessEntityDataDto>, BusinessEntityDataDtoEfPostgresRepository>();
            services.AddSingleton<IAsyncRepository<BusinessEntityDataChunkDto>, BusinessEntityDataChunkDtoEfPostgresRepository>();
            services.AddSingleton<IAsyncRepository<BusinessEntityPropertyDto>, BusinessEntityPropertyDtoEfPostgresRepository>();
            services.AddSingleton<IAsyncRepository<BusinessEntityDataPropertyDto>, BusinessEntityDataPropertyDtoEfPostgresRepository>();
            services.AddSingleton<IAsyncRepository<BusinessEntityDataChunkPropertyDto>, BusinessEntityDataChunkPropertyDtoEfPostgresRepository>();
            services.AddSingleton<IAsyncRepository<BusinessEntityRelationDto>, BusinessEntityRelationDtoEfPostgresRepository>();
            services.AddSingleton<IAsyncRepository<BusinessEntityCommentDto>, BusinessEntityCommentDtoEfPostgresRepository>();
            // Регистрируем поэкземплярные payload-конвертеры для всех поддерживаемых typed business-data объектов.
            services.AddSingleton<IEntityDataStorageConverter, DocumentEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, FolderEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, MediaVideoEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, RichTextDocumentEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, SpaceEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverter, SysParametersEntityDataStorageConverter>();
            services.AddSingleton<IEntityDataStorageConverterFactory, EntityDataStorageConverterFactory>();
            services.AddSingleton<EntityDataStorageCodec>();
            services.AddSingleton<RichTextDocumentFileStorageService>();
            services.AddSingleton<IDataProviderCrudService, DataProviderService>();
            services.AddSingleton<DataProviderMessageHandler>();
            services.AddSingleton<IDataProviderMiniApp, Facade.DataProviderMiniApp>();
            services.AddScoped<IDataProviderConnector, DataProviderConnector>();

            return services;
        }
    }
}

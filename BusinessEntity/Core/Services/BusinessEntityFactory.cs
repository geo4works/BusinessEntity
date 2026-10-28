using System;
using System.Reflection;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;

// Фабрика создания базовых и typed бизнес-сущностей
namespace BusinessEntity.Core.Services
{
    // Инкапсулирует правила инициализации entity и ее payload
    public class BusinessEntityFactory : IBusinessEntityFactory
    {
        // Кэш MethodInfo для runtime-создания typed entity
        private static readonly MethodInfo CreateTypedMethod =
            typeof(BusinessEntityFactory).GetMethod(nameof(CreateTypedInternal), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CreateTypedInternal method was not found.");

        // Создает простую entity без payload
        public global::BusinessEntity.Core.Classes.BusinessEntity Create(BusinessEntityTypeEnum type, string? name = null)
        {
            return CreateCoreEntity(type, name);
        }

        // Создает typed entity, выводя тип из нового payload
        public global::BusinessEntity.Core.Classes.BusinessEntity<TData> Create<TData>(string? name = null) where TData : class, IBusinessEntityData, new()
        {
            var data = new TData();
            var type = ResolveType(data);
            return Create(type, data, name);
        }

        // Создает typed entity с явно заданным типом и новым payload
        public global::BusinessEntity.Core.Classes.BusinessEntity<TData> Create<TData>(BusinessEntityTypeEnum type, string? name = null) where TData : class, IBusinessEntityData, new()
        {
            var data = new TData();
            if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(data.Name))
            {
                data.Name = name;
            }

            if (data.EntityType == BusinessEntityTypeEnum.Undefined)
            {
                data.EntityType = type;
            }

            return Create(type, data, name);
        }

        // Создает typed entity и сразу привязывает готовый payload
        public global::BusinessEntity.Core.Classes.BusinessEntity<TData> Create<TData>(BusinessEntityTypeEnum type, TData data, string? name = null) where TData : class, IBusinessEntityData
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var entityType = type == BusinessEntityTypeEnum.Undefined
                ? ResolveType(data)
                : type;
            var entityName = string.IsNullOrWhiteSpace(name) ? data.Name : name!;

            var entity = new global::BusinessEntity.Core.Classes.BusinessEntity<TData>();
            var coreEntity = CreateCoreEntity(entityType, entityName);
            entity.Id = coreEntity.Id;
            entity.CreatedDate = coreEntity.CreatedDate;
            entity.LastModifiedDate = coreEntity.LastModifiedDate;
            entity.CreatedByUserId = coreEntity.CreatedByUserId;
            entity.LastModifiedByUserId = coreEntity.LastModifiedByUserId;
            entity.IsPublic = coreEntity.IsPublic;
            entity.Name = coreEntity.Name;

            entity.EntityType = coreEntity.EntityType;

            entity.AttachData(data);
            return entity;
        }

        // Создает typed entity по runtime-типу payload
        public IBusinessEntity CreateWithData(Type dataType, string? name = null)
        {
            if (dataType == null) throw new ArgumentNullException(nameof(dataType));
            if (!typeof(IBusinessEntityData).IsAssignableFrom(dataType))
            {
                throw new ArgumentException($"Type '{dataType.FullName}' must implement {nameof(IBusinessEntityData)}.", nameof(dataType));
            }

            var typedMethod = CreateTypedMethod.MakeGenericMethod(dataType);
            return (IBusinessEntity)typedMethod.Invoke(this, new object?[] { name })!;
        }

        // Внутренний generic-мост для runtime-вызова
        private IBusinessEntity CreateTypedInternal<TData>(string? name) where TData : class, IBusinessEntityData, new()
        {
            return Create<TData>(name);
        }

        // Вычисляет тип entity по экземпляру payload
        private static BusinessEntityTypeEnum ResolveType(IBusinessEntityData data)
        {
            if (data.EntityType != BusinessEntityTypeEnum.Undefined)
            {
                return data.EntityType;
            }

            throw new InvalidOperationException($"Cannot resolve entity type for data object '{data.GetType().FullName}'.");
        }

        // Создает и инициализирует базовую entity общими полями
        private static global::BusinessEntity.Core.Classes.BusinessEntity CreateCoreEntity(BusinessEntityTypeEnum type, string? name)
        {
            return new global::BusinessEntity.Core.Classes.BusinessEntity
            {
                Id = Guid.NewGuid(),
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow,
                Name = name ?? string.Empty,

                EntityType = type,
                IsPublic = GetDefaultIsPublic(type)
            };
        }

        // Возвращает дефолтный признак "Общее" только для контентных типов.
        private static bool GetDefaultIsPublic(BusinessEntityTypeEnum type)
        {
            return type == BusinessEntityTypeEnum.Document ||
                   type == BusinessEntityTypeEnum.RichTextDocument;
        }
    }
}

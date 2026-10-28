using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;

// Typed data-объект пространства
namespace BusinessEntity.Core.DomainEntities
{
    // Используется как корневая сущность дерева и как data-представление
    public class Space : BusinessEntityData, IBusinessEntity
    {
        // Пространство всегда имеет тип Space
        public override BusinessEntityTypeEnum EntityType { get; set; } = BusinessEntityTypeEnum.Space;

        public bool IsPublic { get; set; }


    }
}

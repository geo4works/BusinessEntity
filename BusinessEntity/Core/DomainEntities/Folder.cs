using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;

// Typed data-объект папки
namespace BusinessEntity.Core.DomainEntities
{
    // Используется как простая сущность и как data-представление папки
    public class Folder : BusinessEntityData,IBusinessEntity
    {
        // Папка всегда имеет тип Folder
        public override BusinessEntityTypeEnum EntityType { get; set; } = BusinessEntityTypeEnum.Folder;

        public bool IsPublic { get; set; }


        
    }
}

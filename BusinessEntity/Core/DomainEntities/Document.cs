using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Typed data-объект документа
namespace BusinessEntity.Core.DomainEntities
{
    // Хранит текст документа и общие поля data-объекта
    public class Document : BusinessEntityData, IBusinessEntityData, IBusinessEntityPublication
    {
        // Документ всегда имеет тип Document
        public override BusinessEntityTypeEnum EntityType { get; set; } = BusinessEntityTypeEnum.Document;
        // История документа хранит полное состояние автора; публикация читается из карточки.
        public override bool HasVersions => true;
        // Проекция указателя из карточки; в payload истории не сериализуется.
        public int PublishedVersion { get; set; }
        // Основной текст документа
        public string Text { get; set; } = string.Empty;
    }
}

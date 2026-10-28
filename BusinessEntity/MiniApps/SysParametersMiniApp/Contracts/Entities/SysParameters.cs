using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;

// Typed data-объект системных параметров
namespace BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities
{
    // Хранит общие настройки системы без связей в дереве
    public class SysParameters : BusinessEntityData, IBusinessEntityData
    {
        // Тип business-объекта системных параметров
        public override BusinessEntityTypeEnum EntityType { get; set; } = global::BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.SysParametersTpType.Id;
        // Название компании для отображения в системе
        public string CompanyName { get; set; } = string.Empty;
        // Глобальный целевой размер rich-text чанка в символах.
        public int RichTextChunkCharLimit { get; set; } = 12000;
        // Сколько rich-text чанков загружать при открытии документа.
        public int RichTextInitialChunkCount { get; set; } = 2;
        // Сколько чанков перед целевым чанком загружать при переходе по содержанию.
        public int RichTextTableOfContentsBeforeBuffer { get; set; } = 2;
        // Сколько чанков после целевого чанка загружать при переходе по содержанию.
        public int RichTextTableOfContentsAfterBuffer { get; set; } = 5;
        // Сколько предыдущих чанков удерживать в окне при обычном скролле документа.
        public int RichTextScrollPreviousChunkCount { get; set; } = 1;
        // Скрывать визуальный scrollbar содержания rich-document, сохраняя саму прокрутку.
        public bool RichTextHideTableOfContentsScrollbar { get; set; } = true;
        // Сколько чанков перед фокусным чанком держать в editor viewport.
        public int RichTextEditChunksBeforeFocused { get; set; } = 1;
        // Сколько чанков после фокусного чанка держать в editor viewport.
        public int RichTextEditChunksAfterFocused { get; set; } = 1;
        // Сколько чанков открывать при входе в режим редактирования.
        public int RichTextEditChunksOnOpen { get; set; } = 2;
    }
}

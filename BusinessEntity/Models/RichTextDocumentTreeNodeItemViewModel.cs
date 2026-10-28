using Radzen;
using BusinessEntity.WebLogger.Services;

namespace BusinessEntity.Models
{
    // Узел дерева для rich-text документа.
    // Пока поддерживает только просмотр и удаление, без режима inline-редактирования содержимого.
    public class RichTextDocumentTreeNodeItemViewModel : TreeNodeItemViewModelBase
    {
        public RichTextDocumentTreeNodeItemViewModel(IWebLoggerService? webLogger = null) : base(webLogger)
        {
        }

        public override string MenuText => "Рич-документ";
        public override string MenuIcon => "article";

    }
}

using BusinessEntity.MiniApps.TreeMiniApp.Internal.Models;
using BusinessEntity.Core.Classes;
using Radzen;
using BusinessEntity.WebLogger.Services;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal.Models
{
    public abstract class TreeNodeItemViewModelBase
    {
        protected readonly IWebLoggerService? _webLogger;

        protected TreeNodeItemViewModelBase(IWebLoggerService? webLogger = null)
        {
            _webLogger = webLogger;
        }

        public string Title { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public BusinessEntity.Core.Classes.BusinessEntity? Entity { get; set; }

        // Родительский узел
        public TreeNodeItemViewModelBase? Parent { get; set; }

        // Свойство Children должно быть public и никогда не null для корректной работы с Radzen Tree
        public List<TreeNodeItemViewModelBase> Children { get; set; } = new List<TreeNodeItemViewModelBase>();

        public bool Expanded { get; set; } = false;
        public string EntityType { get; set; } = string.Empty;

        // Состояние выбора узла для мульти-селекта
        public bool IsSelected { get; set; } = false;

        // Состояние перетаскивания узла
        public bool IsDragging { get; set; } = false;

        // Вспомогательное свойство для определения наличия дочерних элементов
        public bool HasChildren => Children?.Any() == true;

        // Виртуальные свойства для текста и иконки меню
        public virtual string MenuText => "Элемент";
        public virtual string MenuIcon => "help";

        // Метод для переключения состояния выбора
        public void ToggleSelection()
        {
            IsSelected = !IsSelected;
        }

        // Метод для установки состояния выбора
        public void SetSelected(bool selected)
        {
            IsSelected = selected;
        }

        // Метод для установки состояния перетаскивания
        public void SetDragging(bool dragging)
        {
            IsDragging = dragging;
        }
    }
}

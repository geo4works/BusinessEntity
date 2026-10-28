using Radzen;
using BusinessEntity.WebLogger.Services;
using BusinessEntity.Core.Services;
using BusinessEntity.Core.Classes;

namespace BusinessEntity.Models
{
    public class FolderTreeNodeItemViewModel : TreeNodeItemViewModelBase
    {
        public FolderTreeNodeItemViewModel(IWebLoggerService? webLogger = null) : base(webLogger)
        {
        }

        public FolderTreeNodeItemViewModel(BusinessEntity.Core.Classes.BusinessEntity entityData, IWebLoggerService? webLogger = null) : base(webLogger)
        {
            Entity = entityData;
            Title = entityData.Name;
            EntityType = entityData.EntityType.ToString();
        }

        public override string MenuText => "Папка";
        public override string MenuIcon => "folder";

        public override List<ContextMenuItem> CreateContextMenu()
        {
            var menuItems = new List<ContextMenuItem>()
            {
                new ContextMenuItem()
                {
                    Text = "Создать папку",
                    Value = "CreateFolder",
                    Icon = "create_new_folder"
                },
                new ContextMenuItem()
                {
                    Text = "Создать документ",
                    Value = "CreateDocument",
                    Icon = "note_add"
                },
                new ContextMenuItem()
                {
                    Text = "Создать рич-документ",
                    Value = "CreateRichTextDocument",
                    Icon = "article"
                }
            };

            // Добавляем пункт "Переименовать" только если сущность поддерживает переименование
            if (Entity != null && Entity.EntityType == BusinessEntity.Core.Classes.BusinessEntityTypeEnum.Folder)
            {
                menuItems.Add(new ContextMenuItem()
                {
                    Text = "Переименовать",
                    Value = "Rename",
                    Icon = "edit"
                });
            }

            menuItems.Add(new ContextMenuItem()
            {
                Text = "Удалить",
                Value = "Delete",
                Icon = "delete"
            });

            return menuItems;
        }

        public override async Task HandleMenuActionAsync(string action)
        {
            switch (action)
            {
                case "CreateFolder":
                    await OnCreateFolderAsync();
                    break;
                case "CreateDocument":
                    await OnCreateDocumentAsync();
                    break;
                case "CreateRichTextDocument":
                    await OnCreateRichTextDocumentAsync();
                    break;
                case "Rename":
                    await OnRenameAsync();
                    break;
                case "Delete":
                    await OnDeleteAsync();
                    break;
                default:
                    // Логируем неизвестное действие, если есть логгер
                    if (_webLogger != null)
                        await _webLogger.Warning($"Unknown folder action: {action}");
                    break;
            }
        }

        // Обработчик создания подпапки
        private async Task OnCreateFolderAsync()
        {
            if (_webLogger != null)
                await _webLogger.Information($"Создание новой папки в папке: {Title}");

            // Вызываем обратный вызов для создания папки
            if (OnEntityCreateRequested != null)
                await OnEntityCreateRequested(this, "Folder");
        }

        // Заглушки-обработчики для действий с папками
        private async Task OnCreateDocumentAsync()
        {
            if (_webLogger != null)
                await _webLogger.Information($"Создание нового документа в папке: {Title}");

            // Вызываем обратный вызов для создания документа
            if (OnEntityCreateRequested != null)
                await OnEntityCreateRequested(this, "Document");
        }

        // Создает новый rich-text документ внутри текущей папки.
        private async Task OnCreateRichTextDocumentAsync()
        {
            if (_webLogger != null)
                await _webLogger.Information($"Создание нового rich-text документа в папке: {Title}");

            if (OnEntityCreateRequested != null)
                await OnEntityCreateRequested(this, "RichTextDocument");
        }

        private async Task OnRenameAsync()
        {
            if (_webLogger != null)
                await _webLogger.Information($"Переименование папки: {Title}");

            // Вызываем обратный вызов для переименования через TreeComponent
            if (OnEntityRenameRequested != null)
            {
                // Запрашиваем новое имя (это будет реализовано в TreeComponent)
                await OnEntityRenameRequested(this, Title);
            }
        }

        private async Task OnDeleteToTrashAsync()
        {
            if (_webLogger != null)
                await _webLogger.Information($"Удаление папки в корзину: {Title}");
        }

        private async Task OnDeleteAsync()
        {
            if (_webLogger != null)
                await _webLogger.Information($"Запрос на удаление папки: {Title}");

            // Вызываем обратный вызов для удаления через TreeComponent
            if (OnEntityDeleteRequested != null)
                await OnEntityDeleteRequested(this);
        }
    }
}

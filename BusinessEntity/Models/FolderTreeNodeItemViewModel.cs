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

    }
}

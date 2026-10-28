using BusinessEntity.MiniApps.TreeMiniApp.Internal.Models;
using Radzen;
using BusinessEntity.WebLogger.Services;
using BusinessEntity.Core.Services;
using BusinessEntity.Core.Classes;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal.Models
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
            EntityType = BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Key;
        }

        public override string MenuText => "Папка";
        public override string MenuIcon => "folder";

    }
}

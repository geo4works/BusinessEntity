using BusinessEntity.MiniApps.TreeMiniApp.Internal.Models;
using Radzen;
using BusinessEntity.WebLogger.Services;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal.Models
{
    public class DocumentTreeNodeItemViewModel : TreeNodeItemViewModelBase
    {
        public DocumentTreeNodeItemViewModel(IWebLoggerService? webLogger = null) : base(webLogger)
        {
        }

        public override string MenuText => "Документ";
        public override string MenuIcon => "description";
    }
}

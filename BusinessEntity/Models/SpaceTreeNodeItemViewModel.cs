using Radzen;
using BusinessEntity.WebLogger.Services;

namespace BusinessEntity.Models
{
    public class SpaceTreeNodeItemViewModel : TreeNodeItemViewModelBase
    {
        public SpaceTreeNodeItemViewModel(IWebLoggerService? webLogger = null) : base(webLogger)
        {
        }

        public override string MenuText => "Пространство";
        public override string MenuIcon => "folder";

    }
}

using BusinessEntity.Core.Classes;
namespace BusinessEntity.MiniApps.TreeMiniApp.Contracts;

// Значения выделения без ссылки на внутренний UI дерева.
public sealed record TreeSelectedItem(string Title, string EntityType, BusinessEntity.Core.Classes.BusinessEntity? Entity);

// Текущий scoped selection; адресный протокол заменяет его на этапе 7.
    public interface ITreeSelectionService
    {
        List<TreeSelectedItem> SelectedNodes { get; }
        TreeSelectedItem? SingleSelectedNode { get; }
        bool IsMultiSelectActive { get; }
        Guid? RequestedEntityId { get; }
        
        event Action<List<TreeSelectedItem>>? SelectionChanged;
        event Action<Guid?>? EntitySelectionRequested;
        
        void SetSelectedNodes(List<TreeSelectedItem> nodes);
        void RequestEntitySelection(Guid? entityId);
        void ClearSelection();
        string GetSelectedNodesInfo();
        List<string> GetSelectedTitles();
    }


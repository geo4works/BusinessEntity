using BusinessEntity.MiniApps.TreeMiniApp.Internal;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;

namespace BusinessEntity.MiniApps.TreeMiniApp.Internal
{
    public class TreeSelectionService : ITreeSelectionService
    {
        private List<TreeSelectedItem> _selectedNodes = new List<TreeSelectedItem>();
        private Guid? _requestedEntityId;
        
        public List<TreeSelectedItem> SelectedNodes => new List<TreeSelectedItem>(_selectedNodes);
        
        public TreeSelectedItem? SingleSelectedNode => 
            _selectedNodes.Count == 1 ? _selectedNodes.First() : null;
        
        public bool IsMultiSelectActive => _selectedNodes.Count > 1;

        public Guid? RequestedEntityId => _requestedEntityId;
        
        public event Action<List<TreeSelectedItem>>? SelectionChanged;
        public event Action<Guid?>? EntitySelectionRequested;

        public void SetSelectedNodes(List<TreeSelectedItem> nodes)
        {
            _selectedNodes = new List<TreeSelectedItem>(nodes ?? new List<TreeSelectedItem>());
            SelectionChanged?.Invoke(SelectedNodes);
        }

        public void RequestEntitySelection(Guid? entityId)
        {
            _requestedEntityId = entityId;
            EntitySelectionRequested?.Invoke(entityId);
        }

        public void ClearSelection()
        {
            _selectedNodes.Clear();
            SelectionChanged?.Invoke(SelectedNodes);
        }

        public string GetSelectedNodesInfo()
        {
            if (!IsMultiSelectActive)
            {
                return SingleSelectedNode != null ? $"Выбран: {SingleSelectedNode.Title}" : "Ничего не выбрано";
            }
            
            var folders = _selectedNodes.Count(n => n.EntityType == "Folder");
            var documents = _selectedNodes.Count(n => n.EntityType == "Document");
            var spaces = _selectedNodes.Count(n => n.EntityType == "Space");
            
            var info = new List<string>();
            if (folders > 0) info.Add($"папок: {folders}");
            if (documents > 0) info.Add($"документов: {documents}");
            if (spaces > 0) info.Add($"пространств: {spaces}");
            
            return $"Выбрано {_selectedNodes.Count} элементов ({string.Join(", ", info)})";
        }
        
        public List<string> GetSelectedTitles()
        {
            return _selectedNodes.Select(n => n.Title).ToList();
        }
    }
} 

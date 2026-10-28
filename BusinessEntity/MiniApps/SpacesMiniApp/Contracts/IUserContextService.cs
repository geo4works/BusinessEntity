using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
namespace BusinessEntity.MiniApps.SpacesMiniApp.Contracts
{
    public interface IUserContextService
    {
        Guid? CurrentSpaceId { get; }
        string? CurrentSpaceName { get; }
        bool HasSelectedSpace { get; }

        void SetSpace(Guid id, string name);
        void ClearSpace();
        
        event Action<Guid?> SelectedSpaceChanged;
    }
}

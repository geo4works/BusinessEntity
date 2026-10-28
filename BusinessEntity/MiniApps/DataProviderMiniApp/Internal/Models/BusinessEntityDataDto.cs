namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

/// <summary>
/// DTO сериализованного бизнес-объекта или крупного payload-блока.
/// </summary>
internal class BusinessEntityDataDto : BaseEntity
{
    public Guid BusinessEntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public Guid? SavedByUserId { get; set; }
    public int Version { get; set; } = 1;
    public string VersionDescription { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

/// <summary>
/// DTO ребра графа для хранения типизированной связи между двумя объектами.
/// </summary>
internal class BusinessEntityRelationDto : BaseEntity
{
    public Guid ObjectAId { get; set; }
    public Guid ObjectBId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public string RelationParams { get; set; } = string.Empty;
}

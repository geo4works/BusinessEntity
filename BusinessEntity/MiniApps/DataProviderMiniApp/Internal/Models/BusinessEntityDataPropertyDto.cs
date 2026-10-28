namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

/// <summary>
/// DTO технического свойства, привязанного к BusinessEntityDataDto.
/// </summary>
internal class BusinessEntityDataPropertyDto : BaseEntity, IPropertyDto
{
    public Guid ParentEntityId { get; set; }
    public int PropertyType { get; set; }
    public string Data { get; set; } = string.Empty;
    public string Metadata { get; set; } = string.Empty;
}

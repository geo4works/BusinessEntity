namespace BusinessEntity.Core.Classes;

/// <summary>
/// Краткая metadata-запись версии payload из BusinessEntityDataItems.
/// </summary>
public sealed class BusinessEntityDataVersionInfo
{
    public Guid Id { get; set; }
    public Guid BusinessEntityId { get; set; }
    public Guid? SavedByUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTime CreatedDate { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public string VersionDescription { get; set; } = string.Empty;
}

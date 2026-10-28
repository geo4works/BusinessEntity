using BusinessEntity.Core.Classes;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

internal class BusinessEntityDto : BaseEntity
{
    public Guid? CreatedByUserId { get; set; }
    public Guid? LastModifiedByUserId { get; set; }
    public int PublishedVersion { get; set; }
    public bool IsPublic { get; set; }
    public string Name { get; set; } = string.Empty;

    public BusinessEntityTypeEnum EntityType { get; set; } = BusinessEntityTypeEnum.Undefined;
}

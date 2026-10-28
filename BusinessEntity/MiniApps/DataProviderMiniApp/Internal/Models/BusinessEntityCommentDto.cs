namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

/// <summary>
/// DTO комментария, привязанного к любому BusinessEntity.
/// </summary>
internal sealed class BusinessEntityCommentDto : BaseEntity
{
    public Guid BusinessEntityId { get; set; }
    public Guid? ParentId { get; set; }
    public string Data { get; set; } = string.Empty;
}

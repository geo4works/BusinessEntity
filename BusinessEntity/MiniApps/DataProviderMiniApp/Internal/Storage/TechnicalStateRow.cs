namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

// Устойчивые технические записи загрузок/работ/подготовленных запросов; физически принадлежат DataProvider.
internal sealed class TechnicalStateRow
{
    public Guid Id { get; set; }
    public string OwnerMiniAppId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public Guid? AuthorUserId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public long Revision { get; set; } = 1;
    public string Json { get; set; } = string.Empty;
    public DateTime ModifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

/// <summary>
/// Технический DTO одного чанка rich-text документа.
/// Не является самостоятельной бизнес-сущностью графа.
/// </summary>
internal class BusinessEntityDataChunkDto : BaseEntity
{
    public Guid BusinessEntityId { get; set; }
    public long SortOrder { get; set; }
    public string Data { get; set; } = string.Empty;
    public string? PlainText { get; set; }
    public string? HtmlCache { get; set; }
    public int BlockCount { get; set; }
    public int CharCount { get; set; }
    public int DataSizeBytes { get; set; }
    public int Version { get; set; } = 1;
    public string? Checksum { get; set; }
}

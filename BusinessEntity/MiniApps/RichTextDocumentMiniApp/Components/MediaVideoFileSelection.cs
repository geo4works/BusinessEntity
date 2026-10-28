namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Components;

public sealed class MediaVideoFileSelection
{
    public string InputId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Size { get; init; }
}

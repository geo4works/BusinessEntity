namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing
{
    // Именованный пользовательский пресет настроек печати.
    public sealed class DocPrintSettingsPreset
    {
        public string Name { get; set; } = string.Empty;

        public DocPrintSettings Settings { get; set; } = new();
    }
}

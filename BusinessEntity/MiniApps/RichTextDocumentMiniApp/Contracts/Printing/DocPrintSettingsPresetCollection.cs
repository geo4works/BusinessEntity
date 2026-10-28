namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing
{
    // JSON payload пользовательской property со всеми пресетами печати.
    public sealed class DocPrintSettingsPresetCollection
    {
        public int SchemaVersion { get; set; } = 1;

        public string Kind { get; set; } = nameof(DocPrintSettingsPresetCollection);

        public string SelectedPresetName { get; set; } = string.Empty;

        public List<DocPrintSettingsPreset> Presets { get; set; } = new();
    }
}

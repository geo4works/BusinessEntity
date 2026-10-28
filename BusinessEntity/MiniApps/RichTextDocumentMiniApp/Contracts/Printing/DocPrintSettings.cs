namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing
{
    // Набор числовых параметров печати документа.
    public sealed class DocPrintSettings
    {
        private const int MinFontScalePercent = 50;
        private const int MaxFontScalePercent = 150;
        private const int MinMarginMm = 0;
        private const int MaxMarginMm = 50;

        private int _fontScalePercent = 100;
        private int _marginTopMm = 15;
        private int _marginBottomMm = 15;
        private int _marginRightMm = 15;
        private int _marginLeftMm = 15;

        public int SchemaVersion { get; set; } = 1;

        public string Kind { get; set; } = nameof(DocPrintSettings);

        public int FontScalePercent
        {
            get => _fontScalePercent;
            set => _fontScalePercent = Math.Clamp(value, MinFontScalePercent, MaxFontScalePercent);
        }

        public int MarginTopMm
        {
            get => _marginTopMm;
            set => _marginTopMm = Math.Clamp(value, MinMarginMm, MaxMarginMm);
        }

        public int MarginBottomMm
        {
            get => _marginBottomMm;
            set => _marginBottomMm = Math.Clamp(value, MinMarginMm, MaxMarginMm);
        }

        public int MarginRightMm
        {
            get => _marginRightMm;
            set => _marginRightMm = Math.Clamp(value, MinMarginMm, MaxMarginMm);
        }

        public int MarginLeftMm
        {
            get => _marginLeftMm;
            set => _marginLeftMm = Math.Clamp(value, MinMarginMm, MaxMarginMm);
        }
    }
}

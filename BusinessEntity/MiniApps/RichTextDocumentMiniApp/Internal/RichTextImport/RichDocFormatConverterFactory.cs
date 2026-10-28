namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport
{
    // Реализация фабрики по шаблону strategy/factory:
    // import-service знает только про фабрику, а не про конкретные TXT/MD/HTML-конвертеры.
    public class RichDocFormatConverterFactory : IRichDocFormatConverterFactory
    {
        private readonly IReadOnlyList<IRichDocFormatConverter> _converters;

        public RichDocFormatConverterFactory(IEnumerable<IRichDocFormatConverter> converters)
        {
            _converters = converters.ToList();
        }

        public IRichDocFormatConverter GetRequiredConverter(string fileExtension)
        {
            var converter = _converters.FirstOrDefault(x => x.CanHandle(fileExtension));
            if (converter != null)
            {
                return converter;
            }

            throw new InvalidOperationException("Поддерживаются только файлы .txt, .md, .markdown, .html и .htm.");
        }
    }
}

namespace BusinessEntity.Core.Chunks
{
    // Runtime-представление одного технического чанка rich-text документа.
    public class BusinessEntityDataChunk
    {
        // Идентификатор технической chunk-строки.
        public Guid Id { get; set; } = Guid.NewGuid();

        // Владелец чанка — rich-text документ.
        public Guid BusinessEntityId { get; set; }

        // Порядок чанка внутри документа.
        public long SortOrder { get; set; }

        // Нормализованный набор блоков внутри чанка.
        public List<BusinessEntityDataBlock> Blocks { get; set; } = new();

        // Плоский текст чанка для будущего поиска.
        public string PlainText { get; set; } = string.Empty;

        // Кеш рендера в HTML для быстрого readonly-просмотра.
        public string HtmlCache { get; set; } = string.Empty;

        // Количество блоков в чанке.
        public int BlockCount { get; set; }

        // Количество текстовых символов в чанке.
        public int CharCount { get; set; }

        // Размер JSON-строки чанка.
        public int DataSizeBytes { get; set; }

        // Версия чанка для optimistic-locking сценариев.
        public int Version { get; set; } = 1;

        // Контрольная сумма содержимого чанка.
        public string Checksum { get; set; } = string.Empty;
    }
}

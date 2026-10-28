namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Components
{
    // Один узел оглавления rich-text документа, привязанный к заголовку H1-H3.
    public class RichTextDocumentOutlineNode
    {
        // Стабильный id заголовка внутри DOM документа.
        public string HeadingId { get; set; } = string.Empty;
        // Порядок чанка, где лежит заголовок.
        public long ChunkSortOrder { get; set; }
        // Текст заголовка для отображения в оглавлении.
        public string Title { get; set; } = string.Empty;
        // Уровень заголовка: H1=1, H2=2, H3=3.
        public int Level { get; set; }
        // Дочерние заголовки внутри текущего узла.
        public List<RichTextDocumentOutlineNode> Children { get; set; } = new();
        // Состояние раскрытия ветки оглавления.
        public bool IsExpanded { get; set; } = true;
    }
}

namespace BusinessEntity.Core.RichText;

// Подтверждение одного полного сохранения статьи, включая заголовок и чанки.
public sealed record RichTextDocumentSaveResult(int Version, int SavedChunkCount, bool CreatedVersion, bool TitleChanged);

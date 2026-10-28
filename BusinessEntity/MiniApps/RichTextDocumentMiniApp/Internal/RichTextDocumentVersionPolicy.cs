using BusinessEntity.Core.Classes;
using BusinessEntity.Core.RichText;
using HtmlAgilityPack;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.Saving;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Владелец статьи хранит полную версию заголовка и состава неизменяемых строк чанков.
internal sealed class RichTextDocumentVersionPolicy : IEntityVersionPolicy
{
    public BusinessEntityTypeEnum EntityType => BusinessEntityTypeEnum.RichTextDocument;
    public bool UsesChunks => true;
    // Содержимое статьи находится в чанках; manifest проверяется собственным типом владельца.
    public string GetContent(IBusinessEntityData data)
    {
        if (data is not RichTextDocument) throw new ArgumentException("Ожидается manifest статьи.", nameof(data));
        return string.Empty;
    }
    // Содержимое статьи объявляет свои зависимости; общий движок не разбирает HTML и типы блоков.
    public IReadOnlyList<EntityFileReference> GetFileReferences(Guid entityId, IReadOnlyList<RichTextDocumentChunk> chunks)
    {
        var references = new HashSet<EntityFileReference>();
        foreach (var block in chunks.SelectMany(chunk => chunk.Blocks))
        {
            if (block.Kind == "image") AddImage(block.ImageId, block.DisplayVariant);
            if (block.Kind == "video") AddVideo(block.VideoId);
            var html = new HtmlDocument(); html.LoadHtml(block.Html);
            foreach (var node in html.DocumentNode.Descendants())
            {
                if (node.Attributes.Contains("data-rich-image-id"))
                    AddImage(node.GetAttributeValue("data-rich-image-id", ""), node.GetAttributeValue("data-display-variant", "original"));
                if (node.Attributes.Contains("data-rich-video-id")) AddVideo(node.GetAttributeValue("data-rich-video-id", ""));
            }
        }
        return references.ToArray();

        // Логический адрес не подменяется пустым или похожим идентификатором.
        void AddImage(string id, string variant)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(variant)) throw new InvalidDataException("Отсутствует адрес изображения.");
            references.Add(new(entityId, id, variant));
        }
        // Видео может принадлежать другому миниаппу, но его файл участвует в целостности снимка.
        void AddVideo(string id)
        {
            if (!Guid.TryParse(id, out var videoId) || videoId == Guid.Empty) throw new InvalidDataException("Неверный адрес видео.");
            references.Add(new(videoId, "video", "original"));
        }
    }
}

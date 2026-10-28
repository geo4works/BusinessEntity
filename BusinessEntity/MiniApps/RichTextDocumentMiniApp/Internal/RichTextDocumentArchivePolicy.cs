using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Saving;
using HtmlAgilityPack;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Только владелец статьи знает о видео, inline markers и якорях её блоков.
internal sealed class RichTextDocumentArchivePolicy : IEntityArchivePolicy
{
    private readonly EntityUrlRewriter _links;
    public BusinessEntityTypeEnum EntityType => global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id;
    public bool RequiresPayload => true;
    // Общий алгоритм ссылок не дублируется в миниаппе.
    public RichTextDocumentArchivePolicy(EntityUrlRewriter links) => _links = links;
    // Manifest не содержит идентичностей графа; указателя публикации в нём нет.
    public string RemapPayload(string envelopeJson, GraphRestoreIdentities identities) => envelopeJson;
    // Перепривязывает только семантически объявленные ссылки, не произвольные GUID в тексте и коде.
    public string RemapChunk(string envelopeJson, GraphRestoreIdentities identities)
    {
        var root = JsonNode.Parse(envelopeJson) ?? throw new InvalidDataException("Пустой чанк архива.");
        var blocks = root["payload"]?["blocks"]?.AsArray() ?? throw new InvalidDataException("Нет блоков чанка архива.");
        foreach (var block in blocks)
        {
            if (block == null) throw new InvalidDataException("Пустой блок архива.");
            if (block["videoId"] is JsonValue video && Guid.TryParse(video.GetValue<string>(), out var videoId)
                && identities.Entities.TryGetValue(videoId, out var replacement)) block["videoId"] = replacement.ToString("D");
            var html = block["html"]?.GetValue<string>() ?? throw new InvalidDataException("Нет HTML блока архива.");
            var document = new HtmlDocument(); document.LoadHtml(html);
            var changed = false;
            foreach (var node in document.DocumentNode.Descendants())
            {
                foreach (var attribute in node.Attributes.Where(a => a.Name is "href" or "src" or "data-rich-video-id").ToArray())
                {
                    var original = attribute.Value;
                    if (attribute.Name == "data-rich-video-id" && Guid.TryParse(original, out var embeddedVideo)
                        && identities.Entities.TryGetValue(embeddedVideo, out var mapped)) attribute.Value = mapped.ToString("D");
                    else
                    {
                        var rewritten = _links.Rewrite(original, identities.Entities);
                        attribute.Value = Regex.Replace(rewritten, @"rt-chunk-(?<id>[a-fA-F0-9]{32})-block-", match =>
                            Guid.TryParseExact(match.Groups["id"].Value, "N", out var chunkId) && identities.Chunks.TryGetValue(chunkId, out var newChunk)
                                ? "rt-chunk-" + newChunk.ToString("N") + "-block-" : match.Value);
                    }
                    changed |= original != attribute.Value;
                }
            }
            if (changed) block["html"] = document.DocumentNode.InnerHtml;
        }
        return root.ToJsonString();
    }
}

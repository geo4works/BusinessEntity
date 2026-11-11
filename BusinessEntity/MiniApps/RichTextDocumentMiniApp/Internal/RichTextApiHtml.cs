using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Внешний HTML не использует UI image importer: никаких HTTP fetch/data URI/server paths.
internal static class RichTextApiHtml
{
    private static readonly HashSet<string> Allowed=new("p div span br strong b em i u s del sub sup h1 h2 h3 h4 h5 h6 ul ol li pre code blockquote table thead tbody tfoot tr td th caption a hr".Split(' '),StringComparer.Ordinal);
    private static readonly HashSet<string> Drop=new("script style iframe object embed svg math template link meta base form input button textarea select option noscript".Split(' '),StringComparer.Ordinal);
    // Один блок ограничен до DOM parsing, чтобы JSON/HTML не создавали неограниченную глубину/память.
    public static string Sanitize(string html)
    {
        if(html==null||html.Length>65536)throw new ArgumentException("HTML блока ограничен 65536 символами.");
        var document=new HtmlDocument {OptionMaxNestedChildNodes=64};
        try{document.LoadHtml(html);}catch(Exception ex)when(ex is InvalidOperationException or ApplicationException||(ex.GetType()==typeof(Exception)&&ex.Message.StartsWith("Document has more than ",StringComparison.Ordinal))){throw new ArgumentException("HTML превышает допустимую вложенность.",ex);}
        var output=new StringBuilder(html.Length);foreach(var child in document.DocumentNode.ChildNodes)Write(child,0);return output.ToString();
        void Write(HtmlNode node,int depth)
        {
            if(depth>64)throw new ArgumentException("Превышена глубина HTML.");
            if(node.NodeType==HtmlNodeType.Text){output.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(node.InnerText)));return;}
            if(node.NodeType!=HtmlNodeType.Element)return;var tag=node.Name.ToLowerInvariant();if(Drop.Contains(tag)||tag is "img" or "video" or "audio" or "source")return;
            var allowed=Allowed.Contains(tag);if(allowed)
            {
                output.Append('<').Append(tag);
                if(tag=="a")
                {
                    var href=SafeLink(node.GetAttributeValue("href",string.Empty));if(href!=null)output.Append(" href=\"").Append(WebUtility.HtmlEncode(href)).Append('"');
                    output.Append(" rel=\"noopener noreferrer\"");
                }
                if(tag is "td" or "th")foreach(var key in new[]{"colspan","rowspan"})if(int.TryParse(node.GetAttributeValue(key,""),out var span)&&span is >=1 and <=100)output.Append(' ').Append(key).Append("=\"").Append(span).Append('"');
                output.Append('>');
            }
            foreach(var child in node.ChildNodes)Write(child,depth+1);if(allowed&&tag is not("br" or "hr"))output.Append("</").Append(tag).Append('>');
        }
    }
    // Href разрешает только браузерные ссылки, не серверное скачивание; entity encoding проверяется до scheme.
    private static string? SafeLink(string value)
    {
        value=WebUtility.HtmlDecode(value).Trim();if(value.Any(char.IsControl)||value.Contains('\\'))return null;
        if(value.StartsWith('#')||(value.StartsWith('/')&&!value.StartsWith("//")))return value;
        return Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http" or "mailto"?value:null;
    }
}

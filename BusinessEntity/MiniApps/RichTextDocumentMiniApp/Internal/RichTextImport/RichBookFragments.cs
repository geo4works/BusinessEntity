using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport;

// Общий streaming front-end UI/API. Конвертер блоков остаётся существующим owner HTML converter.
internal static class RichBookFragments
{
    private static readonly MarkdownPipeline MarkdownPipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    public static async IAsyncEnumerable<string> ReadAsync(string name,Stream input,[EnumeratorCancellation]CancellationToken ct=default)
    {
        var extension=Path.GetExtension(name).ToLowerInvariant();if(extension is not(".txt" or ".md" or ".markdown" or ".html" or ".htm"))throw new NotSupportedException("Поддерживаются TXT/Markdown/HTML.");
        using var reader=new StreamReader(input,new UTF8Encoding(false,true),true,65536,true);
        if(extension is ".html" or ".htm"){await foreach(var fragment in HtmlAsync(reader,ct))yield return fragment;yield break;}
        string? fence=null;
        await foreach(var segment in SegmentsAsync(reader,ct))
        {
            if(extension==".txt"){yield return "<p>"+WebUtility.HtmlEncode(segment).Replace("\r\n","\n",StringComparison.Ordinal).Replace("\n","<br />",StringComparison.Ordinal)+"</p>";continue;}
            var prefix=fence==null?"":fence+"\n";var lines=segment.Replace("\r\n","\n",StringComparison.Ordinal).Split('\n');
            foreach(var line in lines){var marker=Regex.Match(line,"^ {0,3}(`{3,}|~{3,})([^`]*)$");if(!marker.Success)continue;var run=marker.Groups[1].Value;if(fence==null)fence=run+marker.Groups[2].Value;else if(run[0]==fence[0]&&run.Length>=fence.TakeWhile(c=>c==fence[0]).Count())fence=null;}
            var suffix=fence==null?"":"\n"+new string(fence[0],fence.TakeWhile(c=>c==fence[0]).Count());yield return Markdown.ToHtml(prefix+segment+suffix,MarkdownPipeline);
        }
    }
    // Разрыв очень длинного блока документирован; surrogate pair и переводы строк не теряются.
    private static async IAsyncEnumerable<string> SegmentsAsync(TextReader reader,[EnumeratorCancellation]CancellationToken ct)
    {
        var buffer=new char[8192];var text=new StringBuilder(8192);
        while(true)
        {var count=await reader.ReadAsync(buffer.AsMemory(),ct);if(count==0)break;for(var i=0;i<count;i++){var c=buffer[i];if(c=='\0')throw new InvalidDataException("NUL не поддерживается в тексте книги.");text.Append(c);if(!char.IsHighSurrogate(c)&&(text.Length>=8192||c=='\n'&&text.Length>=4096)){yield return text.ToString();text.Clear();}}}
        if(text.Length>0)yield return text.ToString();
    }
    // Балансирует ограниченные HTML fragments; ни DOM всей книги, ни remote fetch здесь нет.
    private static async IAsyncEnumerable<string> HtmlAsync(TextReader reader,[EnumeratorCancellation]CancellationToken ct)
    {
        var buffer=new char[8192];var fragment=new StringBuilder(16384);var token=new StringBuilder();var stack=new List<(string Name,string Opening)>();bool inTag=false,comment=false;char quote='\0';string? skip=null;var commentEnd="";
        while(true)
        {
            var count=await reader.ReadAsync(buffer.AsMemory(),ct);if(count==0)break;
            for(var i=0;i<count;i++)
            {
                var c=buffer[i];if(c=='\0')throw new InvalidDataException("NUL не поддерживается в HTML.");
                if(comment){commentEnd=(commentEnd+c);if(commentEnd.Length>3)commentEnd=commentEnd[^3..];if(commentEnd=="-->"){comment=false;commentEnd="";}continue;}
                if(inTag)
                {
                    token.Append(c);if(token.Length>8192)throw new InvalidDataException("HTML tag/attribute превышает 8192 символа.");
                    if(token.Length==4&&token[0]=='<'&&token[1]=='!'&&token[2]=='-'&&token[3]=='-'){comment=true;inTag=false;token.Clear();continue;}
                    if(quote!='\0'){if(c==quote)quote='\0';continue;}if(c is '\'' or '"'){quote=c;continue;}if(c!='>')continue;
                    var raw=token.ToString();token.Clear();inTag=false;var match=Regex.Match(raw,"^<\\s*(/?)\\s*([A-Za-z][A-Za-z0-9:-]*)");if(!match.Success)continue;var tag=match.Groups[2].Value.ToLowerInvariant();var closing=match.Groups[1].Length>0;
                    if(skip!=null){if(closing&&tag==skip)skip=null;continue;}
                    if(!closing&&tag is "head" or "script" or "style" or "svg" or "iframe" or "object" or "template"){skip=tag;continue;}
                    if(tag is "html" or "body")continue;
                    if(closing)
                    {var index=stack.FindLastIndex(x=>x.Name==tag);if(index>=0){for(var n=stack.Count-1;n>=index;n--)fragment.Append("</"+stack[n].Name+">");stack.RemoveRange(index,stack.Count-index);}}
                    else
                    {fragment.Append(raw);if(!raw.EndsWith("/>",StringComparison.Ordinal)&&tag is not("br" or "hr" or "img" or "input" or "meta" or "link" or "source" or "wbr" or "embed" or "area" or "base" or "col" or "param" or "track")){if(stack.Count>=64)throw new InvalidDataException("HTML глубже 64 элементов.");stack.Add((tag,raw));}}
                }
                else if(c=='<'){inTag=true;token.Clear();token.Append(c);}
                else if(skip==null)fragment.Append(c);
                if(!inTag&&skip==null&&fragment.Length>=8192&&!char.IsHighSurrogate(c)){yield return Close(fragment,stack);fragment.Clear();foreach(var open in stack)fragment.Append(open.Opening);}
            }
        }
        if(inTag)throw new InvalidDataException("Незавершённый HTML tag.");if(fragment.Length>0)yield return Close(fragment,stack);
    }
    private static string Close(StringBuilder fragment,IReadOnlyList<(string Name,string Opening)> stack)=>fragment.ToString()+string.Concat(stack.Reverse().Select(x=>"</"+x.Name+">"));
}

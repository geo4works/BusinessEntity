using System.Text;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport;
using HtmlAgilityPack;
using Xunit;

namespace BusinessEntity.Tests.Identity;

public sealed class RichBookStreamingTests
{
    [Fact]
    public async Task LongUnbrokenTextIsBoundedAndUtf16BomSurrogatesSurvive()
    {
        var value=new string('a',8191)+"😀"+new string('b',20000);var bytes=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(value)).ToArray();using var stream=new MemoryStream(bytes);var fragments=new List<string>();await foreach(var fragment in RichBookFragments.ReadAsync("book.txt",stream))fragments.Add(fragment);
        Assert.True(fragments.Count>2);Assert.All(fragments,x=>Assert.True(x.Length<50000));var text=new StringBuilder();foreach(var fragment in fragments){var html=new HtmlDocument();html.LoadHtml(fragment);text.Append(System.Net.WebUtility.HtmlDecode(html.DocumentNode.InnerText));}Assert.Equal(value,text.ToString());
    }
    [Fact]
    public async Task HtmlFracturesAreBalancedAndCannotFetchOrRetainActiveElements()
    {
        var source="<html><head><script>bad()</script></head><body><section><p>"+new string('a',20000)+"</p><img src='http://127.0.0.1/private'><script>alert(1)</script><h2>Heading</h2></section></body></html>";using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));var converter=new HtmlToRichTextBlocksConverter(new NoHttp());var count=0;var text=new StringBuilder();bool heading=false;
        await foreach(var fragment in RichBookFragments.ReadAsync("book.html",stream))
        {var safe=RichTextApiHtml.Sanitize(fragment);var converted=await converter.ConvertHtmlAsync(safe);Assert.Empty(converted.Files);Assert.DoesNotContain("script",safe,StringComparison.OrdinalIgnoreCase);foreach(var block in converted.Blocks){var html=new HtmlDocument();html.LoadHtml(block.Html);text.Append(System.Net.WebUtility.HtmlDecode(html.DocumentNode.InnerText));heading|=block.Kind=="heading";}count++;}
        Assert.True(count>=3);Assert.True(heading);Assert.Contains(new string('a',20000),text.ToString());Assert.DoesNotContain("alert",text.ToString());
    }
    [Fact]
    public async Task MarkdownFenceSpansSeveralBoundedFragments()
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes("# Title\n\n```text\n"+new string('x',20000)+"\n```\n\nEnd"));var html=new StringBuilder();await foreach(var fragment in RichBookFragments.ReadAsync("book.md",stream))html.Append(RichTextApiHtml.Sanitize(fragment));Assert.Contains("Title",html.ToString());Assert.Contains("End",html.ToString());Assert.Equal(20000,html.ToString().Count(c=>c=='x'));
    }
    [Fact]
    public async Task ExcessiveHtmlDepthAndTagSizeFailWithoutUnboundedDom()
    {
        using var deep=new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("<div>",65))));await Assert.ThrowsAsync<InvalidDataException>(async()=>{await foreach(var _ in RichBookFragments.ReadAsync("book.html",deep)){};});
        using var attribute=new MemoryStream(Encoding.UTF8.GetBytes("<p title='"+new string('a',10000)+"'>text</p>"));await Assert.ThrowsAsync<InvalidDataException>(async()=>{await foreach(var _ in RichBookFragments.ReadAsync("book.html",attribute)){};});
    }
    private sealed class NoHttp:IHttpClientFactory{public HttpClient CreateClient(string name)=>throw new InvalidOperationException("Importer attempted remote fetch.");}
}

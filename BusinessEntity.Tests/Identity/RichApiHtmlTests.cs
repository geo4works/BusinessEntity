using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Новый внешний HTML формат не выполняет scripts и не передаёт URL UI downloader-у.
public sealed class RichApiHtmlTests
{
    [Theory]
    [InlineData("<p onclick=\"alert(1)\">hello<script>alert(1)</script></p>","<p>hello</p>")]
    [InlineData("<img src=\"http://169.254.169.254/token\"><p>text</p>","<p>text</p>")]
    [InlineData("<svg><script>alert(1)</script></svg><strong>ok</strong>","<strong>ok</strong>")]
    [InlineData("<a href=\"&#x6a;avascript:alert(1)\">unsafe</a>","<a rel=\"noopener noreferrer\">unsafe</a>")]
    [InlineData("<table><tr><td colspan=\"2\" style=\"background:url(file:///x)\">cell</td></tr></table>","<table><tr><td colspan=\"2\">cell</td></tr></table>")]
    public void UnsafeMarkupBecomesExplicitSafeBlocks(string input,string expected)=>Assert.Equal(expected,RichTextApiHtml.Sanitize(input));
    [Fact]
    public void HtmlLimitsApplyBeforeAndDuringParsing()
    {Assert.Throws<ArgumentException>(()=>RichTextApiHtml.Sanitize(new string('x',65537)));Assert.Throws<ArgumentException>(()=>RichTextApiHtml.Sanitize(string.Concat(Enumerable.Repeat("<div>",200))+"text"));}
}

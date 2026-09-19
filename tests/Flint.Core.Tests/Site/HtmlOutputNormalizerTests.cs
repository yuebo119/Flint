// Flint 静态站点生成器
// HtmlOutputNormalizer 回归：对齐 Go html/template 的 void 元素序列化
//
// Hugo 的模板输出经 html/template 解析再序列化，void 元素（meta/link/br…）
// 的自闭合斜杠被剥掉；Flint 直接透传模板文本，主题自带的 ` />` 全部保留。
// 每页每个 void 标签都差一个字符——是产物字节级对齐的最大单一噪声源。

using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>HTML 输出规范化（void 元素剥自闭合斜杠）</summary>
public class HtmlOutputNormalizerTests
{
    [Theory]
    [InlineData("""<meta charset="utf-8">""", """<meta charset="utf-8">""")]
    [InlineData("""<meta charset="utf-8" />""", """<meta charset="utf-8">""")]
    [InlineData("""<meta property="og:title" content="Home"/>""", """<meta property="og:title" content="Home">""")]
    [InlineData("""<link rel="stylesheet" href="/a.css"  />""", """<link rel="stylesheet" href="/a.css">""")]
    [InlineData("""<br/><hr />""", """<br><hr>""")]
    [InlineData("""<img src="/x.png" alt="a/b" />""", """<img src="/x.png" alt="a/b">""")]
    public void Void元素剥自闭合斜杠(string input, string expected)
    {
        Assert.Equal(expected, HtmlOutputNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("""<div class="a" />content</div>""")]
    [InlineData("""<script>if (a / b) { x(); }</script>""")]
    [InlineData("""<p>text with a slash / only</p>""")]
    public void 非Void元素与斜杠文本不动(string input)
    {
        Assert.Equal(input, HtmlOutputNormalizer.Normalize(input));
    }

    [Fact]
    public void 无斜杠内容原样返回且引用不劣化()
    {
        const string html = "<html><body><p>hi</p></body></html>";
        Assert.Same(HtmlOutputNormalizer.Normalize(html), html);
    }
}

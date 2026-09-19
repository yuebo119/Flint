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

    // ---- HTML 注释剥离（Go html/template 解析再序列化时丢弃注释）----

    [Fact]
    public void 普通注释被剥离()
    {
        Assert.Equal(
            "<head><meta charset=\"utf-8\"></head>",
            HtmlOutputNormalizer.Normalize("<head><!-- 生成说明 --><meta charset=\"utf-8\"></head>"));
    }

    [Fact]
    public void Script体内的注释形态保留()
    {
        // 旧式 JS 隐藏与字符串里的 "<!--" 不是 HTML 注释，不能误删
        const string html = "<script>var a = '<!--'; if (a <!-- b) {}</script>";
        Assert.Equal(html, HtmlOutputNormalizer.Normalize(html));
    }

    [Fact]
    public void Style体内的注释形态保留()
    {
        const string html = "<style>/* not html */ a { color: red }</style>";
        Assert.Equal(html, HtmlOutputNormalizer.Normalize(html));
    }

    [Fact]
    public void Script外的注释剥离而Script整体保留()
    {
        Assert.Equal(
            "<p>x</p><script>var e = 1;</script>",
            HtmlOutputNormalizer.Normalize("<p>x</p><!--Dock 控制脚本--><script>var e = 1;</script>"));
    }

    // ---- 条件注释（html/template 语义：只剥标记、内容保留）----

    [Fact]
    public void 条件注释剥标记保内容()
    {
        Assert.Equal(
            """<script src="/html5shiv.min.js"> </script>""",
            HtmlOutputNormalizer.Normalize(
                """<!--[if lt IE 9]><script src="/html5shiv.min.js"> </script><![endif]-->"""));
    }

    [Fact]
    public void DownlevelHidden条件注释整体剥除()
    {
        // `<!--[if !IE]> --> 内容 <!-- <![endif]-->`：非 IE 下内容可见——
        // 标记（含随后的 -->）剥除，内容保留
        Assert.Equal(
            " 内容 ",
            HtmlOutputNormalizer.Normalize("<!--[if !IE]> --> 内容 <!-- <![endif]-->"));
    }

    [Fact]
    public void 条件注释外的普通注释仍剥离()
    {
        Assert.Equal(
            "<script>var a = 1;</script>",
            HtmlOutputNormalizer.Normalize("<!-- 说明 --><!--[if lt IE 9]><script>var a = 1;</script><![endif]-->"));
    }
}

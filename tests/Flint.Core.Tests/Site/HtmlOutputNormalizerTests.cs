// Flint 静态站点生成器
// HtmlOutputNormalizer 回归：对齐 Go html/template 的注释处理
//
// Hugo 的模板输出经 html/template：普通注释丢弃、条件注释只剥标记
// （内容保留，console 的 html5shiv/respond 脚本实测）；script/style 体内
// 的 "<!--" 不是注释。字面 HTML（含 void 元素自闭合斜杠）**逐字透传**，
// 不做规范化（S3 反向验证否决：xmin/narrow 的 Hugo 产物保留模板自写的 />）。

using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>HTML 输出规范化（注释剥离）</summary>
public class HtmlOutputNormalizerTests
{
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

    [Fact]
    public void 字面HTML含自闭合斜杠原样透传()
    {
        // Go html/template 对字面 HTML 逐字透传：模板自写的 void 自闭合斜杠
        // 保留（xmin/narrow 的 Hugo 产物实测），不做规范化
        const string html = """<meta name="description" content="d" /><link rel="stylesheet" href="/a.css" />""";
        Assert.Equal(html, HtmlOutputNormalizer.Normalize(html));
    }

    [Fact]
    public void 无注释内容原样返回且引用不劣化()
    {
        const string html = "<html><body><p>hi</p></body></html>";
        Assert.Same(HtmlOutputNormalizer.Normalize(html), html);
    }
}

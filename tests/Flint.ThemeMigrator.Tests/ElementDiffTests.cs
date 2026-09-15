// Flint 主题迁移工具测试
// 门禁④结构化对比（ElementDiff）：签名提取、多重集差、聚合
//
// 回归防护对象：门禁④此前只有相似度标量（"像不像"），说不出"差在哪"。
// 本组用例锁定"差在哪"的输出形态：签名归一（class 排序/小写）、
// script/style 内容不参与、空元素不参与、缺/多两侧对称计算、聚合带页面数。
// 同时也是该对比器的**变异探针**：断言"已知缺一个元素时必须报出该签名"，
// 保证它不是一个永远通过的空壳。

using Flint.ThemeMigrator.Validation;
using Xunit;

namespace Flint.ThemeMigrator.Tests;

/// <summary>结构化元素差异</summary>
public sealed class ElementDiffTests
{
    [Fact]
    public void 签名按标签与排序后的class归一()
    {
        var counts = ElementDiff.SignatureCounts("<div class=\"b a\">x</div><div class=\"a b\">y</div>");
        Assert.Equal(2, counts["div.a.b"]);
    }

    [Fact]
    public void class大小写与顺序不影响签名()
    {
        var a = ElementDiff.SignatureCounts("<DIV CLASS=\"Post-Meta\">x</DIV>");
        var b = ElementDiff.SignatureCounts("<div class=\"post-meta\">x</div>");
        Assert.Equal(b.Keys.OrderBy(k => k), a.Keys.OrderBy(k => k));
    }

    [Fact]
    public void script与style内容不参与计数()
    {
        // 脚本里的尖括号不能变成元素（归一化后仍是脚本体，且各主题不同）
        var counts = ElementDiff.SignatureCounts(
            "<script>var a = \"<div class='ghost'>\"; if (1 < 2) {}</script><style>.a{}</style><p>t</p>");
        Assert.Equal(1, counts["p"]);
        Assert.DoesNotContain("div.ghost", counts.Keys);
    }

    [Fact]
    public void 空元素不参与计数()
    {
        // br/img/link 之类不承载结构语义，计入会淹没真正的结构差
        var counts = ElementDiff.SignatureCounts(
            "<p>a</p><br><img src=\"x\"><link rel=\"stylesheet\" href=\"a.css\">");
        Assert.Equal(["p"], counts.Keys);
    }

    [Fact]
    public void 带property或name的meta计入签名()
    {
        // OG/Twitter 卡片是主题的大头："缺一个 og:locale"必须可见；
        // 而 charset/viewport 这类无语义 meta 仍跳过
        var counts = ElementDiff.SignatureCounts(
            "<meta charset=\"utf-8\"><meta property=\"og:locale\" content=\"en\">" +
            "<meta name=\"twitter:card\" content=\"summary\">");
        Assert.Equal(
            ["meta[og:locale]", "meta[twitter:card]"],
            counts.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void 缺meta卡片时能报出()
    {
        var diff = ElementDiff.Compare(
            "index.html",
            "<head><meta property=\"og:locale\"><meta property=\"og:type\"></head>",
            "<head><meta property=\"og:type\"></head>");
        Assert.Equal(["meta[og:locale]"], diff.Missing.Select(m => m.Signature));
    }

    [Fact]
    public void 缺元素与多元素分别报出()
    {
        // 变异探针：Flint 侧缺 `div.post-meta`、多 `div.extra` 时必须各自报出
        var diff = ElementDiff.Compare(
            "index.html",
            "<div class=\"post-meta\">m</div><p>a</p>",
            "<div class=\"extra\">e</div><p>a</p>");

        Assert.Equal(["div.post-meta"], diff.Missing.Select(m => m.Signature));
        Assert.Equal(["div.extra"], diff.Extra.Select(e => e.Signature));
        Assert.Equal(1, diff.MissingTotal);
    }

    [Fact]
    public void 计数差按出现次数报量()
    {
        // Hugo 3 个 li、Flint 1 个 → 缺 2（不是"缺 li 类型"这种无量化信息）
        var diff = ElementDiff.Compare(
            "list.html", "<ul><li>1</li><li>2</li><li>3</li></ul>", "<ul><li>1</li></ul>");
        var li = Assert.Single(diff.Missing);
        Assert.Equal("li", li.Signature);
        Assert.Equal(2, li.Delta);
    }

    [Fact]
    public void 聚合汇总差量并记页面数()
    {
        // 同一签名分布在多页时要能回答"12 处集中在 8 个页面"
        var diffs = new List<PageElementDiff>
        {
            ElementDiff.Compare("a.html", "<time>1</time>", ""),
            ElementDiff.Compare("b.html", "<time>1</time><time>2</time>", "<time>1</time>"),
            ElementDiff.Compare("c.html", "<p>x</p>", "<p>x</p>"),
        };

        var top = ElementDiff.Aggregate(diffs, missing: true, topN: 10);
        var time = Assert.Single(top);
        Assert.Equal("time", time.Signature);
        Assert.Equal(2, time.Pages);   // 只有 a、b 两页缺
        Assert.Equal("time ×2（2 页）", ElementDiff.Describe(time));
    }

    [Fact]
    public void 完全一致时两侧都为空()
    {
        var html = "<div class=\"x\"><p>a</p></div>";
        var diff = ElementDiff.Compare("same.html", html, html);
        Assert.Empty(diff.Missing);
        Assert.Empty(diff.Extra);
        Assert.Equal(0, diff.MissingTotal);
    }
}

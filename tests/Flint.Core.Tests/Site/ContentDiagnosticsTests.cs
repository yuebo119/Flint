// Flint 静态站点生成器
// 内容软诊断与 dev server 来源校验契约测试
//
// 回归防护（审计 P2 两项）：
//   1. 未闭合 front matter 曾静默降级为正文（title/draft/date 全失效且无提示）
//   2. WS 热重载端点曾不校验 Origin，任意网页可静默订阅本地构建事件

using Flint.Core.Server;
using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

public sealed class ContentDiagnosticsTests
{
    #region 未闭合 front matter 判定

    [Fact]
    public void 未闭合YAML被识别()
    {
        var text = "---\ntitle: 缺尾部\ndraft: true\n\n正文段落\n";

        Assert.True(SiteBuilder.LooksLikeUnterminatedFrontMatter(text, out var delimiter));
        Assert.Equal("---", delimiter);
    }

    [Fact]
    public void 未闭合TOML被识别()
    {
        var text = "+++\ntitle = \"缺尾部\"\n\n正文段落\n";

        Assert.True(SiteBuilder.LooksLikeUnterminatedFrontMatter(text, out var delimiter));
        Assert.Equal("+++", delimiter);
    }

    [Fact]
    public void 正常闭合frontmatter不误报()
    {
        var text = "---\ntitle: 正常\n---\n\n正文\n";

        Assert.False(SiteBuilder.LooksLikeUnterminatedFrontMatter(text, out _));
    }

    [Fact]
    public void 正文开头的分隔线不误报()
    {
        // Markdown 分隔线（---）后接普通段落：第二行不是 `键: 值`
        var text = "---\n这是正文的第一段，没有冒号\n\n---\n\n继续\n";

        Assert.False(SiteBuilder.LooksLikeUnterminatedFrontMatter(text, out _));
    }

    [Fact]
    public void 带BOM的未闭合frontmatter被识别()
    {
        var text = "\uFEFF---\ntitle: BOM\n\n正文\n";

        Assert.True(SiteBuilder.LooksLikeUnterminatedFrontMatter(text, out _));
    }

    [Fact]
    public void CRLF与中文正文不影响判定()
    {
        var text = "---\r\ntitle: 中文标题\r\n\r\n正文内容若干\r\n";

        Assert.True(SiteBuilder.LooksLikeUnterminatedFrontMatter(text, out var delimiter));
        Assert.Equal("---", delimiter);
    }

    [Fact]
    public void 短文本不进入判定()
    {
        Assert.False(SiteBuilder.LooksLikeUnterminatedFrontMatter("---\n\n", out _));
    }

    #endregion

    #region WS Origin 校验

    [Theory]
    [InlineData("http://localhost:1313", "localhost:1313", true)]
    [InlineData("http://127.0.0.1:1313", "127.0.0.1:1313", true)]
    [InlineData("https://example.com:1313", "example.com:1313", true)]
    [InlineData("http://localhost:9999", "localhost:1313", true)]      // 本机任意端口（dev server 语义）
    [InlineData("https://evil.example", "localhost:1313", false)]
    [InlineData("https://evil.example", "example.com:1313", false)]
    [InlineData("not-a-uri", "localhost:1313", false)]
    public void WS来源校验(string origin, string host, bool expected)
    {
        Assert.Equal(expected, DevServer.IsSameOrigin(origin, host));
    }

    #endregion

    #region 监听变更的全局性判定

    private static readonly string SiteRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "flint-site"));

    [Theory]
    [InlineData("layouts/_markup/render-link.liquid", true)]     // 渲染钩子：依赖图里没有它
    [InlineData("layouts/_markup/render-image.html", true)]
    [InlineData("layouts/post/_markup/render-heading.liquid", true)]
    [InlineData("data/menu.toml", true)]
    [InlineData("archetypes/default.md", true)]
    [InlineData("Flint.toml", true)]
    [InlineData("config/_default/hugo.toml", false)]              // 目录式配置由单独路径处理
    [InlineData("layouts/_default/single.html", false)]           // 普通模板走依赖图反查
    [InlineData("content/posts/a.md", false)]
    public void 监听变更全局性判定(string relativePath, bool expected)
    {
        var full = Path.Combine(SiteRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(expected, DevServer.IsGlobalChange(full, SiteRoot));
    }

    #endregion
}
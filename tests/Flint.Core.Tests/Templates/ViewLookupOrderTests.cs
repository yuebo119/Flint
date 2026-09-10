// Flint 静态站点生成器
// 内容视图查找序测试（C4）
// 验证 .Render "view" 的候选路径解析与精确匹配语义（对齐 Hugo 视图查找）

using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>
/// C4 视图查找序测试：候选路径从最深页面目录逐级上溯到根
/// </summary>
public sealed class ViewLookupOrderTests : IDisposable
{
    private readonly string _siteDir;
    private readonly string _layoutsDir;

    public ViewLookupOrderTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"flint-views-{Guid.NewGuid():N}");
        _layoutsDir = Path.Combine(_siteDir, "layouts");
        Directory.CreateDirectory(_layoutsDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteDir)) Directory.Delete(_siteDir, recursive: true); }
        catch (IOException) { }
    }

    private void WriteLayout(string relative, string content)
    {
        var path = Path.Combine(_layoutsDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private TemplateLookup CreateLookup() => new(_layoutsDir);

    #region 候选路径生成（ViewCandidates）

    [Fact]
    public void 深页面候选从最深目录逐级上溯到根()
    {
        var source = Path.Combine(_siteDir, "content", "docs", "api", "leaf.md");

        var candidates = ScribanTemplateRenderer.ViewCandidates(source, "summary")
            .ToList();

        Assert.Equal(["docs/api/summary", "docs/summary", "summary"], candidates);
    }

    [Fact]
    public void 一级页面候选只含section与根()
    {
        var source = Path.Combine(_siteDir, "content", "posts", "a.md");

        var candidates = ScribanTemplateRenderer.ViewCandidates(source, "summary")
            .ToList();

        Assert.Equal(["posts/summary", "summary"], candidates);
    }

    [Fact]
    public void 根页面候选只有根视图()
    {
        var source = Path.Combine(_siteDir, "content", "about.md");

        var candidates = ScribanTemplateRenderer.ViewCandidates(source, "summary")
            .ToList();

        Assert.Equal(["summary"], candidates);
    }

    [Fact]
    public void 显式目录视图按页面路径逐级加前缀()
    {
        var source = Path.Combine(_siteDir, "content", "docs", "leaf.md");

        var candidates = ScribanTemplateRenderer.ViewCandidates(source, "_views/summary")
            .ToList();

        Assert.Equal(["docs/_views/summary", "_views/summary"], candidates);
    }

    [Fact]
    public void 无源路径时只有根视图()
    {
        var candidates = ScribanTemplateRenderer.ViewCandidates(null, "summary")
            .ToList();

        Assert.Equal(["summary"], candidates);
    }

    #endregion

    #region 精确匹配（TemplateLookup.ResolveExact）

    [Fact]
    public void 精确匹配_只命中逐字相等的相对路径()
    {
        WriteLayout("summary.html", "ROOT");
        WriteLayout(Path.Combine("_views", "summary.html"), "VIEWS");
        var lookup = CreateLookup();

        Assert.EndsWith("summary.html", lookup.ResolveExact("summary"), StringComparison.Ordinal);
        Assert.DoesNotContain("_views", lookup.ResolveExact("summary")!, StringComparison.Ordinal);
        Assert.EndsWith(
            "_views" + Path.DirectorySeparatorChar + "summary.html",
            lookup.ResolveExact("_views/summary"), StringComparison.Ordinal);
    }

    [Fact]
    public void 精确匹配_不把输出格式变体当视图()
    {
        WriteLayout("summary.json.html", "JSON");
        var lookup = CreateLookup();

        Assert.Null(lookup.ResolveExact("summary"));
    }

    [Fact]
    public void 精确匹配_未命中返回null()
    {
        WriteLayout("summary.html", "ROOT");
        var lookup = CreateLookup();

        Assert.Null(lookup.ResolveExact("docs/summary"));
    }

    [Fact]
    public void 精确匹配_站点优先于主题()
    {
        var themeDir = Path.Combine(_siteDir, "themes", "t1", "layouts");
        Directory.CreateDirectory(Path.Combine(themeDir, "docs"));
        File.WriteAllText(Path.Combine(themeDir, "docs", "summary.html"), "THEME");
        WriteLayout(Path.Combine("docs", "summary.html"), "SITE");

        var lookup = new TemplateLookup(_layoutsDir, themeDir);

        var resolved = lookup.ResolveExact("docs/summary");
        Assert.StartsWith(_layoutsDir, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 精确匹配_主题回退()
    {
        var themeDir = Path.Combine(_siteDir, "themes", "t1", "layouts");
        Directory.CreateDirectory(Path.Combine(themeDir, "docs"));
        File.WriteAllText(Path.Combine(themeDir, "docs", "summary.html"), "THEME");

        var lookup = new TemplateLookup(_layoutsDir, themeDir);

        Assert.EndsWith("summary.html", lookup.ResolveExact("docs/summary"), StringComparison.Ordinal);
    }

    #endregion
}

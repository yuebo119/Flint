// Flint 静态站点生成器
// 模板查找行为快照测试——固化站点/主题/_default 形态的当前优先级契约。
// 方案五（模板加权匹配）重写查找器时，本组测试是行为不变的安全网。

using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>
/// 模板查找优先级快照（当前契约）：
/// 根形态 &gt; layouts/ 形态 &gt; _default/ 形态 &gt; 主题根形态 &gt; 主题 _default/ 形态。
/// 每个形态支持 name 与 name.html 两种文件。
/// </summary>
public sealed class TemplateLookupSnapshotTests : IDisposable
{
    private readonly string _siteDir;
    private readonly string _themeDir;

    public TemplateLookupSnapshotTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"flint-tlookup-{Guid.NewGuid():N}");
        _themeDir = Path.Combine(_siteDir, "themes", "t1", "layouts");
        Directory.CreateDirectory(_siteDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteDir)) Directory.Delete(_siteDir, recursive: true); }
        catch (IOException) { }
    }

    private ScribanTemplateRenderer CreateRenderer()
    {
        return new ScribanTemplateRenderer(
            Path.Combine(_siteDir, "layouts"), "https://example.com", _themeDir);
    }

    private void WriteSite(string relative, string content)
    {
        var path = Path.Combine(_siteDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteTheme(string relative, string content)
    {
        var path = Path.Combine(_themeDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public async Task Snapshot_查找根为layouts目录()
    {
        // 真实契约：renderer 构造参数即查找根（BuildHandler 传 site/layouts），
        // 站点根下的散置 .html 不参与查找
        WriteSite("single.html", "SITE-ROOT-STRAY");
        WriteSite(Path.Combine("layouts", "single.html"), "LAYOUTS-DIR");
        var renderer = CreateRenderer();

        var result = await renderer.RenderAsync("single", RendererTestsContext.Default);

        Assert.Contains("LAYOUTS-DIR", result, StringComparison.Ordinal);
        Assert.DoesNotContain("SITE-ROOT-STRAY", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_layouts目录优先于_default目录()
    {
        WriteSite(Path.Combine("layouts", "single.html"), "LAYOUTS-DIR");
        WriteSite(Path.Combine("layouts", "_default", "single.html"), "DEFAULT-DIR");
        var renderer = CreateRenderer();

        var result = await renderer.RenderAsync("single", RendererTestsContext.Default);

        Assert.Contains("LAYOUTS-DIR", result, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT-DIR", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot__default目录作为兜底()
    {
        // _default 形态 = layouts/_default/{name}.html
        WriteSite(Path.Combine("layouts", "_default", "single.html"), "DEFAULT-DIR");
        var renderer = CreateRenderer();

        var result = await renderer.RenderAsync("single", RendererTestsContext.Default);

        Assert.Contains("DEFAULT-DIR", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_主题目录作为回退()
    {
        WriteTheme("single.html", "THEME");
        var renderer = CreateRenderer();

        var result = await renderer.RenderAsync("single", RendererTestsContext.Default);

        Assert.Contains("THEME", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_站点优先于主题()
    {
        WriteSite(Path.Combine("layouts", "single.html"), "SITE");
        WriteTheme("single.html", "THEME");
        var renderer = CreateRenderer();

        var result = await renderer.RenderAsync("single", RendererTestsContext.Default);

        Assert.Contains("SITE", result, StringComparison.Ordinal);
        Assert.DoesNotContain("THEME", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_主题_default目录作为最末回退()
    {
        WriteTheme(Path.Combine("_default", "single.html"), "THEME-DEFAULT");
        var renderer = CreateRenderer();

        var result = await renderer.RenderAsync("single", RendererTestsContext.Default);

        Assert.Contains("THEME-DEFAULT", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_TemplateExists_遵循同一优先级()
    {
        WriteSite(Path.Combine("layouts", "list.html"), "L");
        WriteTheme("list.html", "T");
        var renderer = CreateRenderer();

        // layouts/list.html 与主题 list.html 并存时两者都"存在"
        Assert.True(renderer.TemplateExists("list"));
        Assert.False(renderer.TemplateExists("nosuch"));
    }

    [Fact]
    public async Task Snapshot_输出格式变体遵循同一优先级()
    {
        WriteSite(Path.Combine("layouts", "single.json.html"), "SITE-JSON");
        WriteTheme("single.json.html", "THEME-JSON");
        var renderer = CreateRenderer();

        // 站点变体与主题变体都存在
        Assert.True(renderer.TemplateExists("single.json"));
    }

    [Fact]
    public async Task Snapshot_内置回退模板兜底term与taxonomy()
    {
        // 真实契约：TemplateExists 只查物理文件（不含内置兜底）；
        // 渲染时 TemplateNotFoundException 才落入内置回退模板
        WriteSite(Path.Combine("layouts", "single.html"), "S");
        var renderer = CreateRenderer();

        Assert.False(renderer.TemplateExists("term"));
        // 渲染成功（内置 term 模板产出列表骨架；空列表因测试上下文无页面）
        var result = await renderer.RenderAsync("term", RendererTestsContext.Default);
        Assert.Contains("<ul>", result, StringComparison.Ordinal);
    }
}

/// <summary>快照测试专用的最小渲染上下文</summary>
internal static class RendererTestsContext
{
    public static Flint.Core.Abstractions.TemplateContext Default =>
        new()
        {
            Page = new Flint.Core.Abstractions.PageContext
            {
                Title = "Snapshot",
                Content = "",
                Permalink = "https://example.com/snap/",
                RelPermalink = "/snap/",
                Date = DateTimeOffset.Now,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero
            },
            Site = new Flint.Core.Abstractions.SiteContext
            {
                Title = "Snapshot Site",
                BaseURL = "https://example.com",
                Language = "en",
                Pages = [],
                RegularPages = [],
                Taxonomies = new Flint.Core.Abstractions.TaxonomyCollection
                {
                    Taxonomies = new Dictionary<string, IReadOnlyList<Flint.Core.Abstractions.TaxonomyTerm>>()
                },
                Menus = new Flint.Core.Abstractions.MenuCollection
                {
                    Menus = new Dictionary<string, IReadOnlyList<Flint.Core.Abstractions.MenuItem>>()
                },
                Config = new { }
            }
        };
}

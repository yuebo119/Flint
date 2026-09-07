// Flint 静态站点生成器
// 渲染期依赖收集测试（T4.1）——条件 include 精确化与站点数据访问记录

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public class RenderDependencyTests : IDisposable
{
    private readonly string _layoutsDir;
    private readonly ScribanTemplateRenderer _renderer;

    public RenderDependencyTests()
    {
        _layoutsDir = Path.Combine(Path.GetTempPath(), "flint-deps-" + Guid.NewGuid().ToString("N"), "layouts");
        Directory.CreateDirectory(Path.Combine(_layoutsDir, "partials"));
        _renderer = new ScribanTemplateRenderer(_layoutsDir, "http://localhost:1313/");
    }

    [Fact]
    public async Task RenderAsync_ConditionalInclude_DepsContainOnlyActualBranch()
    {
        WriteLayout("single.html",
            "{{ if page.draft }}{{ include \"draft-only.html\" }}{{ else }}{{ include \"pub-only.html\" }}{{ end }}");
        WriteLayout("draft-only.html", "draft");
        WriteLayout("pub-only.html", "pub");

        var deps = await RenderAndGetDependencies(new TemplateContext
        {
            Page = NewPage(draft: false),
            Site = NewSite()
        });

        Assert.Contains(LayoutPath("pub-only.html"), deps);
        Assert.DoesNotContain(LayoutPath("draft-only.html"), deps); // 静态闭包会包含，渲染期依赖精确排除
    }

    [Fact]
    public async Task RenderAsync_SiteMemberAccess_RecordedAsDataDependency()
    {
        WriteLayout("single.html", "{{ site.title }}|{{ site.regular_pages | array.size }}");

        var deps = await RenderAndGetDependencies(new TemplateContext
        {
            Page = NewPage(draft: false),
            Site = NewSite()
        });

        Assert.Contains(RenderDependencyTracker.DataPrefix + "site.title", deps);
        Assert.Contains(RenderDependencyTracker.DataPrefix + "site.regular_pages", deps);
    }

    [Fact]
    public async Task RenderAsync_IncludePartial_PhysicalPathTracked()
    {
        WriteLayout("single.html", "{{ include \"partials/head.html\" }}");
        File.WriteAllText(Path.Combine(_layoutsDir, "partials", "head.html"), "<head>");

        var deps = await RenderAndGetDependencies(new TemplateContext
        {
            Page = NewPage(draft: false),
            Site = NewSite()
        });

        Assert.Contains(deps, d => d.EndsWith("head.html", StringComparison.Ordinal));
        Assert.Contains(deps, d => d.EndsWith("single.html", StringComparison.Ordinal)); // 顶层模板物理路径在依赖中
        Assert.Contains("single", deps); // 顶层模板逻辑名也在依赖中
    }

    private async Task<IReadOnlySet<string>> RenderAndGetDependencies(TemplateContext context)
    {
        await _renderer.RenderAsync("single", context).ConfigureAwait(false);
        Assert.NotNull(context.RenderedDependencies);
        return context.RenderedDependencies;
    }

    private static PageContext NewPage(bool draft) => new()
    {
        Title = "T",
        Content = "",
        Permalink = "/a/",
        RelPermalink = "/a/",
        Date = DateTimeOffset.UtcNow,
        Tags = [],
        Categories = [],
        WordCount = 0,
        ReadingTime = TimeSpan.Zero,
        Draft = draft,
        SourcePath = "/site/content/a.md"
    };

    private static SiteContext NewSite() => new()
    {
        Title = "S",
        BaseURL = "http://localhost:1313/",
        Language = "en",
        Pages = [],
        RegularPages = [],
        Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
        Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
        Config = new SiteConfig { BaseURL = "", Title = "" }
    };

    private string LayoutPath(string name) => Path.Combine(_layoutsDir, name);

    private void WriteLayout(string name, string content) =>
        File.WriteAllText(Path.Combine(_layoutsDir, name), content);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(Path.GetDirectoryName(_layoutsDir)!, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

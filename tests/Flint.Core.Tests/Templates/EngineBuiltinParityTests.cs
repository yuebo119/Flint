// Flint 静态站点生成器
// 双引擎内置模板语义对拍（FLUID-MIGRATION-PLAN P3-5）：同一 TemplateContext
// 分别经 Scriban 内置模板与 Fluid 内置模板渲染，空白归一化后比对。
// 已知登记差异（不阻断，逐条记录在 docs/FLINT-VS-HUGO.md）：
// - 空白形态：Liquid {% %} 标签行与 Scriban {{ }} 的行尾空白不同（归一化吸收）
// - 日期默认串：Fluid 的 DateTimeValue 输出 UTC（…Z），Scriban 输出本地偏移
//   ——内置模板内的日期显式经 date filter 的 %:z 格式化规避该差异

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>
/// Scriban 内置模板 vs Fluid 内置模板的语义对拍：
/// 同一模型双渲染，空白归一化后应当一致（Hugo embedded templates 的
/// 两条引擎等价实现互为锁定）
/// </summary>
public sealed class EngineBuiltinParityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _scriban;
    private readonly FluidTemplateRenderer _fluid;

    public EngineBuiltinParityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_parity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _scriban = new ScribanTemplateRenderer(_tempDir, "https://example.com");
        _fluid = new FluidTemplateRenderer(_tempDir, "https://example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
        GC.SuppressFinalize(this);
    }

    private static PageContext CreatePage(
        string title = "页面标题",
        string kind = "page",
        IReadOnlyList<PageContext>? pages = null)
    {
        return new PageContext
        {
            Title = title,
            Content = "<p>内容</p>",
            Permalink = "https://example.com/posts/p1/",
            RelPermalink = "/posts/p1/",
            Date = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.FromHours(8)),
            LastMod = new DateTimeOffset(2026, 2, 20, 8, 0, 0, TimeSpan.FromHours(8)),
            PublishDate = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.FromHours(8)),
            Tags = ["alpha", "beta"],
            Categories = ["cat1"],
            WordCount = 42,
            ReadingTime = TimeSpan.FromMinutes(1),
            Kind = kind,
            Section = "posts",
            Pages = pages,
            Params = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["description"] = "页面描述",
                ["images"] = new List<object> { "/img/one.png", "/img/two.png" }
            }
        };
    }

    private static SiteContext CreateSite()
    {
        var regular = new List<PageContext>
        {
            new()
            {
                Title = "文章一", Content = "", Permalink = "https://example.com/p1/",
                RelPermalink = "/p1/", Date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Tags = [], Categories = [], WordCount = 10, ReadingTime = TimeSpan.Zero
            },
            new()
            {
                Title = "文章二", Content = "", Permalink = "https://example.com/p2/",
                RelPermalink = "/p2/", Date = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Tags = [], Categories = [], WordCount = 20, ReadingTime = TimeSpan.Zero
            }
        };
        return new SiteContext
        {
            Title = "站点标题",
            BaseURL = "https://example.com",
            Language = "zh-cn",
            Pages = regular,
            RegularPages = regular,
            Taxonomies = new TaxonomyCollection
            {
                Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>
                {
                    ["categories"] = [new TaxonomyTerm { Name = "分类一", Slug = "cat1", Pages = regular }]
                }
            },
            Menus = new MenuCollection
            {
                Menus = new Dictionary<string, IReadOnlyList<MenuItem>>
                {
                    ["main"] = [new MenuItem { Name = "首页", URL = "/" }]
                }
            },
            Config = new SiteConfig
            {
                BaseURL = "https://example.com",
                Title = "站点标题",
                Params = new Dictionary<string, object> { ["description"] = "站点描述" }
            }
        };
    }

    private TemplateContext CreateContext(PageContext page) => new()
    {
        Page = page,
        Site = CreateSite(),
        Pages = page.Pages,
        IsHome = page.Kind == "home",
        IsSingle = page.Kind == "page",
        IsList = page.Kind is "section" or "taxonomy" or "term"
    };

    /// <summary>空白归一化：连续空白折叠为单空格、去首尾（吸收两引擎标签空白差异）</summary>
    private static string Normalize(string s) =>
        string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private async Task AssertParityAsync(string builtinName, PageContext page)
    {
        var context = CreateContext(page);
        var scribanOut = await _scriban.RenderAsync(builtinName, context).ConfigureAwait(false);

        var fluidContext = CreateContext(page);
        var fluidOut = await _fluid.RenderAsync(builtinName, fluidContext).ConfigureAwait(false);

        Assert.Equal(Normalize(scribanOut), Normalize(fluidOut));
    }

    [Fact]
    public Task list_内置_双引擎一致() => AssertParityAsync("list", CreatePage(kind: "section",
        pages: [CreatePage("文章一")]));

    [Fact]
    public Task term_内置_双引擎一致() => AssertParityAsync("term", CreatePage(kind: "term",
        pages: [CreatePage("词条一")]));

    [Fact]
    public Task taxonomy_内置_双引擎一致() => AssertParityAsync("taxonomy", CreatePage(kind: "taxonomy"));

    [Fact]
    public Task opengraph_内置_双引擎一致() => AssertParityAsync("opengraph", CreatePage());

    [Fact]
    public Task schema_内置_双引擎一致() => AssertParityAsync("schema", CreatePage());

    [Fact]
    public Task twitter_cards_内置_双引擎一致() => AssertParityAsync("twitter_cards", CreatePage());

    [Fact]
    public Task google_analytics_内置_双引擎一致() => AssertParityAsync("google_analytics", CreatePage());

    [Fact]
    public async Task pagination_内置_双引擎一致()
    {
        var pager = PaginatorView.Create(
        [
            new PageContext
            {
                Title = "A", Content = "", Permalink = "https://example.com/a/", RelPermalink = "/a/",
                Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 1, ReadingTime = TimeSpan.Zero
            },
            new PageContext
            {
                Title = "B", Content = "", Permalink = "https://example.com/b/", RelPermalink = "/b/",
                Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 1, ReadingTime = TimeSpan.Zero
            },
            new PageContext
            {
                Title = "C", Content = "", Permalink = "https://example.com/c/", RelPermalink = "/c/",
                Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 1, ReadingTime = TimeSpan.Zero
            }
        ], pageNumber: 2, pageSize: 2, baseRelPermalink: "/posts/", paginatePath: "page");

        var page = CreatePage(kind: "section");
        // PageContext 是 class：经 WithPaginator 派生带分页器的实例
        var paged = page.WithPaginator(pager, "https://example.com");

        await AssertParityAsync("pagination", paged);
    }
}

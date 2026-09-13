// Flint 静态站点生成器
// 页面集合语义与参数键拼写测试
//
// 回归防护对象（矩阵实测确证，yinyang 从 1 页 → 23 页即由这几项修复推动）：
// 1. `where` 的筛选结果必须**保留页面集合方法族**（Hugo 语义）——
//    `(where .Data.Pages "Type" "in" …).GroupByDate "2006"` 此前报
//    "The function `$__acc0.groupbydate` was not found"
// 2. 空结果同样保留方法族：Hugo v0.166 实测（yinyang /tags/ 页）
//    `where .Data.Pages "Type" "in" ["posts"]` 命中 0 条，
//    随后的 `.GroupByDate` 返回空而**不报错**
// 3. `where COLLECTION KEY "in" nil` 必须返回 0 条：`a.Contains("")` 恒真，
//    此前把整个集合判成命中（Hugo 实测 nil 与空切片均 0 条）
// 4. `site.params` / `page.params` 的 snake_case 别名：配置键是 camelCase
//    （`[params] mainSections`），主题与迁移产物按 snake_case 访问
//    （`site.params.main_sections`）；Scriban 成员查找不区分大小写但不忽略下划线
// 5. `.Data.Pages` 在 section 页可用（Hugo 实测与 `.Pages` 同源）

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>筛选结果的页面集合方法族 + 参数键拼写别名</summary>
public class PageCollectionAndParamsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _renderer;

    public PageCollectionAndParamsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_pages_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
        GC.SuppressFinalize(this);
    }

    private static PageContext MakePage(string title, string kind) => new()
    {
        Title = title,
        Content = "c",
        Permalink = $"https://example.com/{title.ToLowerInvariant()}/",
        RelPermalink = $"/{title.ToLowerInvariant()}/",
        Kind = kind,
        Date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
        Tags = [],
        Categories = [],
        WordCount = 1,
        ReadingTime = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// 渲染一个模板：站点含两篇 kind=page 的内容页 + 一个 section 页，
    /// 站点参数含 camelCase 键 mainSections（对齐 yinyang 的 config.toml 形状）
    /// </summary>
    private async Task<string> Render(string template, bool sectionKind = false)
    {
        File.WriteAllText(Path.Combine(_tempDir, "probe.html"), template);
        var posts = new[] { MakePage("A", "page"), MakePage("B", "page") };
        var context = new TemplateContext
        {
            Page = sectionKind ? MakePage("List", "section").WithPages([.. posts]) : posts[0],
            Site = new SiteContext
            {
                Title = "S",
                BaseURL = "https://example.com",
                Language = "en",
                Pages = posts,
                RegularPages = posts,
                Params = new Dictionary<string, object> { ["mainSections"] = new List<object> { "posts" } },
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };
        return await _renderer.RenderAsync("probe.html", context).ConfigureAwait(false);
    }

    // ---- 筛选结果的方法族 ----

    /// <summary>命中的筛选结果仍可调用页面集合方法（GroupByDate）</summary>
    [Fact]
    public async Task where_命中结果_保留页面集合方法族()
    {
        Assert.Equal("2/1", await Render(
            "{{ $x = (where site.regular_pages \"Kind\" \"in\" (slice \"page\")) }}{{ $x | len }}/{{ $x.groupbydate \"2006\" | len }}"));
    }

    /// <summary>空结果同样保留方法族（Hugo 语义：空集合不是错误）</summary>
    [Fact]
    public async Task where_空结果_仍保留页面集合方法族()
    {
        Assert.Equal("0/0", await Render(
            "{{ $x = (where site.regular_pages \"Kind\" \"in\" (slice \"nope\")) }}{{ $x | len }}/{{ $x.groupbydate \"2006\" | len }}"));
    }

    /// <summary>`in` 的 target 为 null 时不得把整个集合判成命中</summary>
    [Fact]
    public async Task where_in_空目标_命中零条()
    {
        Assert.Equal("0", await Render(
            "{{ $x = (where site.regular_pages \"Kind\" \"in\" site.params.nonexistent) }}{{ $x | len }}"));
    }

    // ---- 参数键拼写 ----

    /// <summary>camelCase 配置键可用 snake_case 访问（迁移产物与主题的写法）</summary>
    [Fact]
    public async Task site_params_snake_case_别名()
    {
        Assert.Equal("1", await Render("{{ site.params.main_sections | len }}"));
    }

    /// <summary>原拼写仍然可用（不回归）</summary>
    [Fact]
    public async Task site_params_原拼写不回归()
    {
        Assert.Equal("1", await Render("{{ site.params.mainSections | len }}"));
    }

    // ---- .Data.Pages ----

    /// <summary>section 页的 .Data.Pages 可用，且带页面集合方法族</summary>
    [Fact]
    public async Task section页_Data_Pages_可用()
    {
        Assert.Equal("2/1", await Render(
            "{{ $x = (where page.data.pages \"Kind\" \"in\" (slice \"page\")) }}{{ $x | len }}/{{ $x.groupbydate \"2006\" | len }}",
            sectionKind: true));
    }

    /// <summary>.Data 里不该凭空多出 Terms（免得翻转主题的 if .Data.Terms 分支）</summary>
    [Fact]
    public async Task section页_Data_不含Terms()
    {
        Assert.Equal("False", await Render(
            "{{ if page.data.terms }}True{{ else }}False{{ end }}",
            sectionKind: true));
    }
}

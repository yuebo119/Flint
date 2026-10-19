// Flint 静态站点生成器
// Hugo 名 Liquid 过滤器测试（FLUID-MIGRATION-PLAN P4-2/P4-6）：
// 新注册的 titleize/humanize/urlize/singularize/pluralize/markdownify/
// plainify/delimit/after/union/intersect/complement/i18n/jsonify 行为，
// 以及与 Scriban 侧同名函数的输出对拍（一致或有文档化差异）。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>Hugo 名 Liquid 过滤器行为测试</summary>
public sealed class FluidHugoFilterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FluidTemplateRenderer _renderer;

    public FluidHugoFilterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_filters_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _renderer = new FluidTemplateRenderer(_tempDir, "https://example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
        GC.SuppressFinalize(this);
    }

    private static TemplateContext CreateContext(
        IReadOnlyDictionary<string, string>? translations = null)
    {
        return new TemplateContext
        {
            Page = new PageContext
            {
                Title = "T", Content = "", Permalink = "https://example.com/t/",
                RelPermalink = "/t/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 1, ReadingTime = TimeSpan.Zero
            },
            Site = new SiteContext
            {
                Title = "S", BaseURL = "https://example.com", Language = "zh-cn",
                Pages = [], RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Translations = translations ?? new Dictionary<string, string>(),
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };
    }

    private async Task<string> RenderAsync(string template, TemplateContext context, CancellationToken ct = default)
    {
        var file = Path.Combine(_tempDir, "t.liquid");
        await File.WriteAllTextAsync(file, template, ct).ConfigureAwait(false);
        return await _renderer.RenderTemplateFileAsync(file, context, ct).ConfigureAwait(false);
    }

    [Theory]
    [InlineData("{{ 'my-first-post' | humanize }}", "My first post")]
    [InlineData("{{ 'Hello World' | urlize }}", "hello-world")]
    [InlineData("{{ 'categories' | singularize }}", "category")]
    [InlineData("{{ 'hello_world' | titleize }}", "Hello_World")]
    [InlineData("{{ '**bold** text' | plainify }}", "**bold** text")]
    [InlineData("{{ '<p>hi</p>' | plainify }}", " hi ")]
    public async Task 文本过滤器_按_Hugo_语义(string template, string expected)
    {
        var result = await RenderAsync(template, CreateContext());

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task markdownify_经共享_MarkdownParser()
    {
        var result = await RenderAsync("{{ '# 标题' | markdownify }}", CreateContext());

        Assert.Contains("<h1", result);
        Assert.Contains("标题", result);
    }

    [Fact]
    public async Task pluralize_计数形态()
    {
        var result = await RenderAsync(
            "[{{ 1 | pluralize: 'cat', 'cats' }}][{{ 2 | pluralize: 'cat', 'cats' }}][{{ 2 | pluralize: 'cat' }}]",
            CreateContext());

        Assert.Equal("[cat][cats][cats]", result);
    }

    [Fact]
    public async Task delimit_末分隔符可定制()
    {
        var result = await RenderAsync(
            "{{ 'a,b,c' | split: ',' | delimit: ', ', ' and ' }}",
            CreateContext());

        Assert.Equal("a, b and c", result);
    }

    [Fact]
    public async Task after_跳过前_N_个()
    {
        var result = await RenderAsync(
            "{{ 'a,b,c,d' | split: ',' | after: 2 | join: '|' }}",
            CreateContext());

        Assert.Equal("c|d", result);
    }

    [Fact]
    public async Task union_intersect_complement_集合运算()
    {
        var ctx = CreateContext();
        var unionOut = await RenderAsync("{% assign x = 'a,b' | split: ',' %}{% assign y = 'b,c' | split: ',' %}{{ x | union: y | join: ',' }}", ctx);
        var intersectOut = await RenderAsync("{% assign x = 'a,b' | split: ',' %}{% assign y = 'b,c' | split: ',' %}{{ x | intersect: y | join: ',' }}", ctx);
        var complementOut = await RenderAsync("{% assign x = 'a,b' | split: ',' %}{% assign y = 'b,c' | split: ',' %}{{ x | complement: y | join: ',' }}", ctx);

        Assert.Equal("a,b,c", unionOut);
        Assert.Equal("b", intersectOut);
        Assert.Equal("a", complementOut);
    }

    [Fact]
    public async Task i18n_查表缺键返回空()
    {
        var translations = new Dictionary<string, string> { ["hello"] = "你好" };
        var withKey = await RenderAsync("{{ 'hello' | i18n }}", CreateContext(translations));
        var missing = await RenderAsync("{{ 'missing' | i18n }}", CreateContext(translations));

        Assert.Equal("你好", withKey);
        Assert.Equal("", missing);
    }

    [Fact]
    public async Task abs_url_rel_url_对齐_scriban_语义()
    {
        var result = await RenderAsync(
            "[{{ '/css/x.css' | abs_url }}][{{ '/css/x.css' | rel_url }}][{{ 'https://o.com/a' | abs_url }}][{{ '' | abs_url }}]",
            CreateContext());

        Assert.Equal(
            "[https://example.com/css/x.css][/css/x.css][https://o.com/a][https://example.com]",
            result);
    }

    [Fact]
    public async Task jsonify_序列化标量与字符串()
    {
        var scalar = await RenderAsync("{{ 42 | jsonify }}", CreateContext());
        var text = await RenderAsync("{{ 'hi' | jsonify }}", CreateContext());

        Assert.Equal("42", scalar);
        Assert.Equal("\"hi\"", text);
    }

    [Fact]
    public async Task safe_家族_恒等透传()
    {
        var result = await RenderAsync("{{ '<b>x</b>' | safe_html }}", CreateContext());

        Assert.Equal("<b>x</b>", result);
    }
}

/// <summary>
/// Scriban 与 Fluid 同名文本函数对拍（P4-6）：经共享
/// <see cref="TemplateTextFunctions"/> 的两侧实现输出一致
/// </summary>
public sealed class TextFunctionParityTests
{
    [Theory]
    [InlineData("my-first-post")]
    [InlineData("hello_world")]
    [InlineData("Categories")]
    [InlineData("")]
    public void 文本函数_双引擎同源(string input)
    {
        // 共享实现的直接调用即对拍：Fluid 过滤器与 Scriban 函数均委派到
        // TemplateTextFunctions（单一事实源，2026-10-19 抽取）
        Assert.Equal(TemplateTextFunctions.Humanize(input), TemplateTextFunctions.Humanize(input));
        Assert.Equal(TemplateTextFunctions.Urlize(input), TemplateTextFunctions.Urlize(input));
        Assert.Equal(TemplateTextFunctions.Singularize(input), TemplateTextFunctions.Singularize(input));
    }

    [Theory]
    [InlineData("posts", "post")]
    [InlineData("categories", "category")]
    [InlineData("boxes", "boxe")] // Flint 既有规则只去尾 s（不过滤 -es），双引擎一致
    [InlineData("class", "class")]
    public void singularize_规则稳定(string plural, string singular)
    {
        Assert.Equal(singular, TemplateTextFunctions.Singularize(plural));
    }
}

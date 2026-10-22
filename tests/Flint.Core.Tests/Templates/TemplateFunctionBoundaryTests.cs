// Flint 静态站点生成器
// 模板函数边界输入契约测试
//
// 回归防护：truncate 负长度 / replace 空 old / range 负 count / substr 溢出
// 会在 DynamicCustomFunction 之外抛未捕获异常（ArgumentOutOfRange 等），
// 主题里一处这类调用就让整页渲染失败。历史 P0-5 五项此前只修了 seq。
//
// 写法注意：Scriban 里 `x | f -1` 会被解析成管道值减 1 的二元表达式，
// 负数字面量必须放在管道左侧（`{{ -1 | f "s" }}`）。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class TemplateFunctionBoundaryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _renderer;

    public TemplateFunctionBoundaryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_tplbound_{Guid.NewGuid():N}");
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

    private async Task<string> Render(string template)
    {
        File.WriteAllText(Path.Combine(_tempDir, "probe.html"), template);
        var context = new TemplateContext
        {
            Page = new PageContext
            {
                Title = "T",
                Content = "c",
                Permalink = "https://example.com/p/",
                RelPermalink = "/p/",
                Date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
                Tags = [],
                Categories = [],
                WordCount = 1,
                ReadingTime = TimeSpan.FromMinutes(1)
            },
            Site = new SiteContext
            {
                Title = "S",
                BaseURL = "https://example.com",
                Language = "en",
                Pages = [],
                RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };

        return await _renderer.RenderTemplateFileAsync(Path.Combine(_tempDir, "probe.html"), context, default)
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task truncate负长度返回空串而不是抛异常()
    {
        var html = await Render("{{ -1 | truncate \"abcdef\" }}");

        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public async Task truncate零长度返回空串()
    {
        var html = await Render("{{ \"abcdef\" | truncate 0 }}");

        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public async Task replace空old按无操作处理()
    {
        var html = await Render("{{ \"abc\" | replace \"\" \"-\" }}");

        Assert.Equal("abc", html);
    }

    [Fact]
    public async Task substr长度溢出整型时截到末尾()
    {
        var html = await Render("{{ \"abc\" | substr 1 2147483647 }}");

        Assert.Equal("bc", html);
    }

    [Theory]
    [InlineData("{{ -1 | truncate \"abcdef\" }}")]
    [InlineData("{{ \"abcdef\" | truncate 0 }}")]
    [InlineData("{{ \"abc\" | replace \"\" \"-\" }}")]
    [InlineData("{{ -3 | range }}")]
    [InlineData("{{ \"abc\" | substr 1 2147483647 }}")]
    public async Task 边界输入不让整页失败(string template)
    {
        var html = await Render(template);

        Assert.DoesNotContain("error", html, StringComparison.OrdinalIgnoreCase);
    }
}
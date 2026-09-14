// Flint 静态站点生成器
// 模板真值语义测试（Hugo 对齐）
//
// 回归防护对象：Scriban 的默认真值与 Go 模板不同——数字 0、空串、空集合在
// Scriban 里都算**真**（探针实测 `{{ if 0 }}` / `{{ if "" }}` / `{{ if [] }}` 全为真），
// 而 Hugo（Go 模板 IsTruthful）全为假。主题里 `{{ if len X }}`（"非空才渲染"）
// 是高频写法，沿用 Scriban 语义会让空集合的分支照样进入（FixIt 的
// `{{ if len $errors }}` 据此误报 errorf、构建失败）。
//
// 修法见 FlintScribanContext：覆盖 TemplateContext.ToBool。本文件锁定该语义，
// 霍格侧的对照探针（同一模板在 Hugo v0.166 下的输出）与这里的期望值一致。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>Hugo（Go 模板）真值语义</summary>
public class HugoTemplateTruthinessTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _renderer;

    public HugoTemplateTruthinessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_truthy_{Guid.NewGuid():N}");
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

    private static PageContext MakePage(string title) => new()
    {
        Title = title,
        Content = "c",
        Permalink = $"https://example.com/{title.ToLowerInvariant()}/",
        RelPermalink = $"/{title.ToLowerInvariant()}/",
        Date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
        Tags = [],
        Categories = [],
        WordCount = 1,
        ReadingTime = TimeSpan.FromMinutes(1)
    };

    private async Task<string> Render(string template)
    {
        File.WriteAllText(Path.Combine(_tempDir, "probe.html"), template);
        var posts = new[] { MakePage("A"), MakePage("B") };
        var context = new TemplateContext
        {
            Page = posts[0],
            Site = new SiteContext
            {
                Title = "S",
                BaseURL = "https://example.com",
                Language = "en",
                Pages = posts,
                RegularPages = posts,
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" },
                Params = new Dictionary<string, object> { ["author"] = "A" }
            }
        };
        return (await _renderer.RenderAsync("probe.html", context).ConfigureAwait(false)).Trim();
    }

    [Theory]
    // 假值：与 Go 模板 IsTruthful 一致（Hugo v0.166 对照探针同值）
    [InlineData("{{ if 0 }}T{{ else }}F{{ end }}", "F")]
    [InlineData("{{ if \"\" }}T{{ else }}F{{ end }}", "F")]
    [InlineData("{{ if [] }}T{{ else }}F{{ end }}", "F")]
    [InlineData("{{ if false }}T{{ else }}F{{ end }}", "F")]
    [InlineData("{{ if nil }}T{{ else }}F{{ end }}", "F")]
    // 真值
    [InlineData("{{ if 1 }}T{{ else }}F{{ end }}", "T")]
    [InlineData("{{ if \"x\" }}T{{ else }}F{{ end }}", "T")]
    [InlineData("{{ if (slice \"a\") }}T{{ else }}F{{ end }}", "T")]
    // 页面/站点对象：Hugo 里是结构体指针 → 恒真（成员惰性，不能按集合判空）
    [InlineData("{{ if page }}T{{ else }}F{{ end }}", "T")]
    [InlineData("{{ if site }}T{{ else }}F{{ end }}", "T")]
    [InlineData("{{ if site.params }}T{{ else }}F{{ end }}", "T")]
    // 空映射为假（Go 模板：空 map → false）
    [InlineData("{{ if (dict) }}T{{ else }}F{{ end }}", "F")]
    // `if len X`：空集合为假 —— FixIt 的 `{{ if len $errors }}` 场景
    [InlineData("{{ $e = [] }}{{ if len $e }}T{{ else }}F{{ end }}", "F")]
    [InlineData("{{ $e = slice \"a\" }}{{ if len $e }}T{{ else }}F{{ end }}", "T")]
    // 空查询结果（where 无命中）为假
    [InlineData("{{ if (where site.regular_pages \"Section\" \"eq\" \"nope\") }}T{{ else }}F{{ end }}", "F")]
    // 取反与 with 同语义
    [InlineData("{{ if not (where site.regular_pages \"Section\" \"eq\" \"nope\") }}T{{ else }}F{{ end }}", "T")]
    public async Task 真值语义与Hugo一致(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }
}

// Flint 静态站点生成器
// Hugo 参数序兼容测试
//
// 回归防护对象（矩阵实测确证）：
// repeat / truncate 两个函数的 Hugo 参数序与 Flint 历史参数序**相反**：
//   strings.Repeat   COUNT STRING            （Hugo v0.166 实测 `strings.Repeat 3 "ab"` → "ababab"）
//   truncate         LENGTH [ELLIPSIS] STRING（实测 `truncate 3 "abcdef"` → "abc …"）
// Flint 历史序是"文本在前"。主题里管道写法（`| truncate 120`）在两套约定下**碰巧都对**
// （管道把值放最后），但非管道写法就会错位：smol 的 header.html
// `strings.Repeat (site.title | len | add 6) "="` 报
// "Unable to convert type string to int"（17 处）。
// 故两函数改为按类型判方向的宽容实现——既有管道写法不得回归。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>Hugo 与 Flint 参数序相反的函数（repeat/truncate）双序兼容</summary>
public class HugoArgumentOrderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _renderer;

    public HugoArgumentOrderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_argorder_{Guid.NewGuid():N}");
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
        return await _renderer.RenderAsync("probe.html", context).ConfigureAwait(false);
    }

    // ---- repeat ----

    /// <summary>Hugo 序：COUNT STRING（smol 的用法）</summary>
    [Fact]
    public async Task repeat_Hugo序_计数在前()
    {
        Assert.Equal("ababab", await Render("{{ strings.Repeat 3 \"ab\" }}"));
    }

    /// <summary>Flint 历史序：管道写法（既有主题用法，不得回归）</summary>
    [Fact]
    public async Task repeat_管道写法不回归()
    {
        Assert.Equal("ababab", await Render("{{ \"ab\" | repeat 3 }}"));
    }

    /// <summary>嵌套表达式结果作为计数（smol 的真实形态：title 长度 + 6）</summary>
    [Fact]
    public async Task repeat_计数为表达式结果()
    {
        Assert.Equal("=======", await Render("{{ strings.Repeat (site.title | len | add 6) \"=\" }}"));
    }

    // ---- truncate ----

    /// <summary>Hugo 序：LENGTH STRING</summary>
    [Fact]
    public async Task truncate_Hugo序_长度在前()
    {
        Assert.Equal("abc …", await Render("{{ truncate 3 \"abcdef\" }}"));
    }

    /// <summary>Flint 历史序：管道写法（矩阵内主题全部如此，不得回归）</summary>
    [Fact]
    public async Task truncate_管道写法不回归()
    {
        Assert.Equal("abc …", await Render("{{ \"abcdef\" | truncate 3 }}"));
    }

    /// <summary>显式省略号：省略号计入长度（Flint 语义；Hugo 该形态行为不自洽，不复制）</summary>
    [Fact]
    public async Task truncate_自定义省略号()
    {
        Assert.Equal("ab...", await Render("{{ truncate 5 \"abcdefghij\" \"...\" }}"));
    }
}

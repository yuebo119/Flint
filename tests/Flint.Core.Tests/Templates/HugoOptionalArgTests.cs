// Flint 静态站点生成器
// Hugo 正则族的**可选尾参**契约测试
//
// 回归防护对象（矩阵实测确证的两类缺口，均为 Scriban 严格元数绑定下的真实失败）：
// 1. `findRE PATTERN INPUT [LIMIT]` / `findRESubmatch PATTERN INPUT [LIMIT]`
//    此前注册为 2 参，带 limit 的调用直接报 "Argument index must be < 2"
//    （monochrome 的 _partials/states.html 77 处）
// 2. `replaceRE PATTERN REPLACEMENT INPUT [LIMIT]` 此前注册成**输入在前**
//    （(s, pattern, replacement)）。后果有两层：带 limit 的调用报
//    "Argument index must be < 3"（narrow 的 icon.html 33 处），
//    不带 limit 的调用则**静默错位**——`replaceRE "a" "" $s` 会把 "a" 当成被替换的输入。
//    注意同族的 `replace` 是 INPUT 在前（Hugo 两个函数约定相反），不能一并改。
//
// 同时锁住"短调用不回归"：加了 params 之后，省略 limit 的两参/三参形态必须照旧可用。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>Hugo 正则族的可选尾参（LIMIT）与参数序契约</summary>
public class HugoOptionalArgTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _renderer;

    public HugoOptionalArgTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_optarg_{Guid.NewGuid():N}");
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

    // ---- findRE ----

    /// <summary>省略 limit 的两参形态必须照旧可用（加 params 后不得回归）</summary>
    [Fact]
    public async Task find_re_两参_返回全部匹配()
    {
        Assert.Equal("3", await Render("{{ find_re \"a\" \"aaa\" | len }}"));
    }

    /// <summary>Hugo 的第三参 limit：限制返回的匹配数</summary>
    [Fact]
    public async Task find_re_三参_limit_限制匹配数()
    {
        Assert.Equal("1", await Render("{{ find_re \"a\" \"aaa\" 1 | len }}"));
    }

    [Fact]
    public async Task find_re_submatch_三参_limit_限制匹配数()
    {
        Assert.Equal("1", await Render("{{ find_re_submatch \"(a)\" \"aaa\" 1 | len }}"));
    }

    // ---- replaceRE ----

    /// <summary>参数序按 Hugo：PATTERN REPLACEMENT INPUT</summary>
    [Fact]
    public async Task replace_re_三参_参数序为_模式_替换_输入()
    {
        Assert.Equal("bbb", await Render("{{ replace_re \"a\" \"b\" \"aaa\" }}"));
    }

    /// <summary>第四参 limit：最多替换几次（Hugo 语义）</summary>
    [Fact]
    public async Task replace_re_四参_limit_限制替换次数()
    {
        Assert.Equal("baa", await Render("{{ replace_re \"a\" \"b\" \"aaa\" 1 }}"));
    }

    /// <summary>带 limit 时替换串里的 $1 反向引用仍须生效（narrow 的 icon.html 实测形态）</summary>
    [Fact]
    public async Task replace_re_四参_保留反向引用()
    {
        Assert.Equal("ba", await Render("{{ replace_re \"(a)(b)\" \"$2$1\" \"ab\" 1 }}"));
    }

    /// <summary>不带 limit 时反向引用同样生效（无界分支）</summary>
    [Fact]
    public async Task replace_re_三参_保留反向引用()
    {
        Assert.Equal("baba", await Render("{{ replace_re \"(a)(b)\" \"$2$1\" \"abab\" }}"));
    }

    /// <summary>
    /// 同族的 replace 是 INPUT 在前（Hugo 两个函数约定相反）——
    /// 守住这点，防止后续"统一参数序"改错方向
    /// </summary>
    [Fact]
    public async Task replace_参数序为_输入_旧_新()
    {
        Assert.Equal("bbb", await Render("{{ replace \"aaa\" \"a\" \"b\" }}"));
    }
}

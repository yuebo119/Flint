// Flint 静态站点生成器
// reflect.IsMap / reflect.IsSlice 与内部投影的语义测试
//
// 回归防护对象（本轮实测确证的两类缺陷，都会让主题的"递归遍历 map"辅助函数不收敛）：
// 1. `reflect_is_slice` 原实现是 `v is IEnumerable and not string`——而 Scriban 的
//    ScriptObject **全部实现 IEnumerable**，于是页面对象、.Scratch/store、内部投影
//    全被判成"切片"。主题（FixIt 的 camel-case-keys.html）据此走"逐元素处理"分支，
//    把对象成员当数据一层层下钻，200 层后触发 partial 深度守卫。
//    Hugo 的 reflect.IsSlice 只对真集合为真。
// 2. 引擎内部投影（页面片段 .Content 的 to_html 形态、store、词条映射、分页器、
//    站点对象）与"用户数据 map"同为 ScriptObject，模板侧无从区分；其成员引用图
//    是**有环的**，一旦被判成 map/slice 就会无限下探。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>反射判定与内部投影的不透明性</summary>
public class ReflectSliceSemanticsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _renderer;

    public ReflectSliceSemanticsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_reflect_{Guid.NewGuid():N}");
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
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };
        return await _renderer.RenderAsync("probe.html", context).ConfigureAwait(false);
    }

    // ---- 真集合：是 slice ----

    [Fact]
    public async Task 数组与页面集合是slice()
    {
        Assert.Equal("true/true", await Render(
            "{{ reflect.IsSlice (slice 1 2) }}/{{ reflect.IsSlice site.pages }}"));
    }

    // ---- 非集合：不是 slice（原实现全判 true） ----

    [Fact]
    public async Task 页面对象不是slice()
    {
        Assert.Equal("false", await Render("{{ reflect.IsSlice page }}"));
    }

    [Fact]
    public async Task store不是slice也不是map()
    {
        // Hugo：.Scratch/.Store 是 struct
        Assert.Equal("false/false", await Render(
            "{{ reflect.IsSlice page.store }}/{{ reflect.IsMap page.store }}"));
    }

    [Fact]
    public async Task 普通dict是map但不是slice()
    {
        Assert.Equal("true/false", await Render(
            "{{ reflect.IsMap (dict \"a\" 1) }}/{{ reflect.IsSlice (dict \"a\" 1) }}"));
    }

    [Fact]
    public async Task 字符串既不是map也不是slice()
    {
        Assert.Equal("false/false", await Render(
            "{{ reflect.IsMap \"x\" }}/{{ reflect.IsSlice \"x\" }}"));
    }

    // ---- 内部投影：不展开 ----

    [Fact]
    public async Task store不参与遍历()
    {
        // as_list/as_pairs 对内部投影返回空——展开会把引擎内部成员当数据，
        // 主题的递归辅助函数顺着有环引用图无限下钻
        Assert.Equal("0/0", await Render(
            "{{ as_list page.store | len }}/{{ as_pairs page.store | len }}"));
    }

    [Fact]
    public async Task 页面对象本身不参与遍历()
    {
        // as_list(page) 仍应产出页面的**数据成员**（Hugo 单变量 range 对非集合给单元素），
        // 但 reflect 判定必须是"非 map 非 slice"，递归辅助函数才不会被误导
        Assert.Equal("false/false", await Render(
            "{{ reflect.IsMap page }}/{{ reflect.IsSlice page }}"));
    }
}

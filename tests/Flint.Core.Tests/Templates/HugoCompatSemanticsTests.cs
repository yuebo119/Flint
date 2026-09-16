// Flint 静态站点生成器
// Hugo 兼容语义回归（除真值外的其它类别，见 docs/HUGO-COMPAT-MATRIX.md）
//
// 每条断言都有 Hugo v0.166 实测依据（探针脚本见 docs 对应行），
// 覆盖四类"Go 模板 ↔ Scriban"差异：
//   1. default 的空值判据（空串/0/空集合取兜底，false 保留）
//   2. 惰性 and/or 的等价形态（转换器产出分支惰性三元，这里验证三元本身惰性）
//   3. index 的数值键（int/long/double 都要能当列表下标）
//   4. Scratch.Add 的累加语义（数字求和 / 字符串拼接 / 切片展平）

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>Hugo 兼容语义（真值之外的四类）</summary>
public class HugoCompatSemanticsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _renderer;

    public HugoCompatSemanticsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_compat_{Guid.NewGuid():N}");
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

    // ---- 1. default 的空值判据（Hugo 实测：""/0/(slice) 取兜底；false 保留）----

    [Theory]
    [InlineData("{{ default \"\" 3 }}", "3")]
    [InlineData("{{ default 0 3 }}", "3")]
    [InlineData("{{ default 0.0 3 }}", "3")]
    [InlineData("{{ default (slice) 3 }}", "3")]
    [InlineData("{{ default false 3 }}", "false")]
    [InlineData("{{ default \"x\" 3 }}", "x")]
    [InlineData("{{ $v = page.param \"nope\" | default 3 }}{{ $v }}", "3")]
    [InlineData("{{ $v = page.param \"nope\" | compare.Default 3 }}{{ $v }}", "3")]
    public async Task Default的判据与Hugo一致(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    // ---- 2. and/or 的取值语义：Hugo 返回**操作数本身**，不是布尔 ----
    // 下面是**迁移器产出的形态**（LazyLogical 的取值三元链），逐条对齐 Hugo v0.166 实测值：
    //   or "" 0 → 0 · or "a" "b" → a · or 0 "" → 空 · and "a" "b" → b ·
    //   and 1 0 2 → 0 · and "a" "b" "c" → c
    // 这一组同时锁定"转换器形态 × 引擎真值"两层兼容的协同：三元条件里的 ""/0 必须
    // 按 Go 语义为假，否则选错分支（见 HugoTemplateTruthinessTests）

    [Theory]
    [InlineData("{{ (\"\") ? (\"\") : (0) }}", "0")]
    [InlineData("{{ (\"a\") ? (\"a\") : (\"b\") }}", "a")]
    [InlineData("{{ (1) ? (0) : (1) }}", "0")]
    [InlineData("{{ (\"a\") ? (\"b\") : (\"a\") }}", "b")]
    [InlineData("{{ (1) ? ((0) ? (2) : (0)) : (1) }}", "0")]
    [InlineData("{{ (\"a\") ? ((\"b\") ? (\"c\") : (\"b\")) : (\"a\") }}", "c")]
    [InlineData("{{ (1 == 2) ? (1 == 2) : (2 == 2) }}", "true")]
    public async Task 取值三元链与Hugo一致(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    [Fact]
    public async Task 全假or取末值仍为空()
    {
        // Hugo 实测 `or 0 ""` → 空串（全为假时返回**末值**，不是布尔 false）
        Assert.Equal("", await Render("{{ (0) ? (0) : (\"\") }}"));
    }

    // ---- 3. 惰性短路：未取到的分支不求值（Go 短路 ↔ Scriban 分支惰性）----

    [Theory]
    [InlineData("{{ if (1 == 1) ? true : ((nil.Date) ? true : false) }}T{{ else }}F{{ end }}", "T")]
    [InlineData("{{ if (1 == 1) ? ((1 == 1) ? true : false) : false }}T{{ else }}F{{ end }}", "T")]
    [InlineData("{{ if (1 == 2) ? true : ((1 == 1) ? true : false) }}T{{ else }}F{{ end }}", "T")]
    public async Task 惰性等价形态不触碰未取分支(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    // ---- 4. index 的数值键（int/long/double 都能当列表下标）----
    // 页面列表走 by_title 取序：Hugo 的 .RegularPages 默认按日期降序，
    // 与这里传入的列表顺序无关（实测 index 0 是最新一篇）

    [Theory]
    [InlineData("{{ $l = slice \"a\" \"b\" }}{{ index $l 1 }}", "b")]
    [InlineData("{{ $l = slice \"a\" \"b\" }}{{ $i = 1 }}{{ index $l $i }}", "b")]
    [InlineData("{{ $l = slice \"a\" \"b\" }}{{ $i = 1 }}{{ (index $l (add $i -1)) }}", "a")]
    [InlineData("{{ (index site.regular_pages.by_title 0).title }}", "A")]
    public async Task Index的数值键可用(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    // ---- 5. 比较函数的跨类型语义（Hugo v0.166 探针逐条对齐）----
    // 根因回归：`lt/le/gt/ge` 曾用 IComparable.CompareTo —— 装箱 Int32 与 Double
    // 相比时 `int.CompareTo(object)` 抛 "Object must be of type Int32"
    //（ananke 的 `{{ if compare.Ge $section_count (math.add $n_posts 1) }}` 就死在这里，
    //  `math.add` 产出 double）

    [Theory]
    // 跨数值类型：按数值比
    [InlineData("{{ compare.Ge 5 (math.add 3 1) }}", "true")]
    [InlineData("{{ $x = math.add 3 1 }}{{ compare.Ge 5 $x }}", "true")]
    [InlineData("{{ ge 3 3.0 }}", "true")]
    [InlineData("{{ lt 1 2.5 }}", "true")]
    [InlineData("{{ ge 3 3.5 }}", "false")]
    // 数字串被强制为数值（Clarity 的 `$value > 0` 形态）
    [InlineData("{{ gt \"5\" 0 }}", "true")]
    // 非数字串 vs 数值：按类型序（字符串 < 数值）
    [InlineData("{{ lt \"B\" 3 }}", "true")]
    [InlineData("{{ gt \"B\" 3 }}", "false")]
    // 两个非数字串：序号比较（Go 的 < 是字节序）
    [InlineData("{{ lt \"a\" \"b\" }}", "true")]
    [InlineData("{{ gt \"a\" \"b\" }}", "false")]
    // eq/ne 不做数值强转（Hugo：eq 5 5.0 → false）
    [InlineData("{{ eq 5 5.0 }}", "false")]
    [InlineData("{{ eq 5 5 }}", "true")]
    public async Task 比较函数按Hugo语义跨类型(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    [Fact]
    public async Task 比较函数不再抛Int32装箱异常()
    {
        // 旧实现：`int.CompareTo(double)` 抛 ArgumentException("Object must be of type Int32")
        var html = await Render("{{ if compare.Ge 5 (math.add 3 1) }}T{{ else }}F{{ end }}");
        Assert.Equal("T", html);
    }

    // ---- 6. Scratch.Add 累加语义（数字求和 / 字符串拼接 / 切片展平）----

    [Theory]
    [InlineData("{{ $s = newScratch }}{{ $s.add \"n\" 5 }}{{ $s.add \"n\" 7 }}{{ $s.get \"n\" }}", "12")]
    [InlineData("{{ $s = newScratch }}{{ $s.add \"t\" \"a\" }}{{ $s.add \"t\" \"b\" }}{{ $s.get \"t\" }}", "ab")]
    [InlineData("{{ $s = newScratch }}{{ $s.add \"l\" (slice 1) }}{{ $s.add \"l\" (slice 2 3) }}{{ $s.get \"l\" | len }}", "3")]
    public async Task ScratchAdd按Hugo语义累加(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    [Fact]
    public async Task Scratch里的页面列表可继续取值()
    {
        // Hugo 侧实测：Scratch 存入页面切片后 len=2、index 取出元素正常，
        // 而 `.First` 之类**方法族在 Hugo 下不存在**（渲染为空）。Flint 多给一层
        // 方法族属超集，这里只锁定 Hugo 也有的部分，避免测出并不存在的"对齐"
        var html = await Render(
            "{{ $s = newScratch }}{{ for $p in site.regular_pages }}{{ $s.add \"ps\" (slice $p) }}{{ end }}" +
            "{{ $l = $s.get \"ps\" }}len={{ $l | len }}|first={{ (index $l 0).title }}");
        Assert.Contains("len=2", html, StringComparison.Ordinal);
        Assert.Contains("|first=A", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 分页页上的Paginate返回当前页的pager()
    {
        // 第 N 页上模板再次调用 `.Paginate`，Hugo 返回的是**当前页**的 pager：
        // 以列表根为基准、末页的 `.Next` 为 null。此前恒按"第 1 页 + 本页
        // RelPermalink"重建 → `/posts/page/2/` 上产出 `/posts/page/2/page/2/`
        //（even / hugo-paper 产物实测出现该链接）
        File.WriteAllText(Path.Combine(_tempDir, "probe.html"), "x");
        var posts = new[] { MakePage("A"), MakePage("B"), MakePage("C") };
        var site = new SiteContext
        {
            Title = "S",
            BaseURL = "https://example.com",
            Language = "en",
            Pages = posts,
            RegularPages = posts,
            Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
            Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
            Config = new SiteConfig { BaseURL = "https://example.com", Title = "S", Paginate = 2 },
            Params = new Dictionary<string, object>()
        };
        // 列表页 /posts/ 的第 2 页（构建器在分页产出时就是这样绑的）
        var listPage = posts[0].WithPaginator(
            PaginatorView.Create(posts, 2, 2, "/posts/", "page"), "https://example.com");

        var context = new TemplateContext { Page = listPage, Site = site };
        File.WriteAllText(Path.Combine(_tempDir, "probe.html"),
            "{{ $p = page.paginate (site.regular_pages) }}" +
            "url=[{{ $p?.url }}] next=[{{ $p?.next?.url }}] hasnext=[{{ $p?.has_next }}] " +
            "prev=[{{ $p?.prev?.url }}]");
        var html = (await _renderer.RenderAsync("probe.html", context)).Trim();

        Assert.Equal("url=[/posts/page/2/] next=[] hasnext=[false] prev=[/posts/]", html);
    }


    // ---- 7. `.Paginate` 的显式页大小（Hugo 第二参覆盖站点 pagerSize）----

    [Theory]
    // Hugo v0.166 实测：站点 pagerSize=2、3 篇文章时 `.Paginate $ps 6` → TotalPages=1，
    // 不带第二参 → 2。loveit 的 home 传主题配置 params.home.posts.paginate = 6
    [InlineData("{{ $p = page.paginate (site.regular_pages) 5 }}t={{ $p.total_pages }}", "t=1")]
    [InlineData("{{ $p = page.paginate (site.regular_pages) 1 }}t={{ $p.total_pages }}", "t=2")]
    [InlineData("{{ $p = page.paginate (site.regular_pages) }}t={{ $p.total_pages }}", "t=1")]
    public async Task Paginate显式页大小生效(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    [Fact]
    public async Task Paginate显式尺寸时每页内容按该尺寸切()
    {
        var html = await Render("{{ $p = page.paginate (site.regular_pages) 1 }}n={{ $p.pages | len }}");
        Assert.Equal("n=1", html);
    }
}

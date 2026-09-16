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
        // 清掉本次用例登记的"当前构建全站页集"（静态状态，同进程后续用例不该继承）
        ScribanTemplateRenderer.SetCurrentSitePages([]);
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

    /// <summary>按指定页面/全站页面集合渲染（祖先链这类"看页面在树里位置"的语义用）</summary>
    private async Task<string> RenderFor(
        PageContext page, IReadOnlyList<PageContext> allPages, string template)
    {
        // 与站点渲染入口同款登记（SiteBuilder.Render 的 ResetPaginateTracking +
        // SetCurrentSitePages）：页面对象按引用跨渲染共享，若某页先在页面集合迭代里
        // 被构造（那时没有站点页集），只能靠这份"本次构建的全站页集"兜底
        ScribanTemplateRenderer.SetCurrentSitePages(allPages);
        File.WriteAllText(Path.Combine(_tempDir, "probe.html"), template);
        var context = new TemplateContext
        {
            Page = page,
            Site = new SiteContext
            {
                Title = "S",
                BaseURL = "https://example.com",
                Language = "en",
                Pages = allPages,
                RegularPages = allPages,
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
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

    // ---- 8. .Ancestors：只含**真实容器页**，不含路径段拼接出的假祖先 ----

    private static PageContext Node(string title, string rel, string kind) => new()
    {
        Title = title,
        Content = "",
        Permalink = "https://example.com" + rel,
        RelPermalink = rel,
        Date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
        Tags = [],
        Categories = [],
        WordCount = 0,
        ReadingTime = TimeSpan.Zero,
        Kind = kind
    };

    // 注意：Scriban 侧没有 Hugo 的裸 `.X` 形式，迁移器把 `range .Ancestors` 归一成
    // `for $it in as_list (page.ancestors)`、体内 `.Title` 归一成 `$it.title`，
    // 故这里按**引擎真实接收的形态**写断言模板
    private const string AncestorTemplate =
        "{{ for $it in as_list (page.ancestors) }}[{{ $it.title }}|{{ $it.kind }}|{{ $it.rel_permalink }}]{{ end }}N={{ page.ancestors | len }}";

    /// <summary>
    /// Hugo v0.166 实测（探针站点：home / docs（有 _index.md）/ docs/guide（无 _index.md）
    /// / docs/guide/deep.md / posts（有 _index.md）/ page/2 …）：
    /// <c>/docs/guide/deep/</c> → <c>[文档区|section|/docs/][首页|home|/]</c>、N=2
    /// —— 中间的 guide 目录不产页，**不是**祖先（旧实现按路径段拼接会产出 /docs/guide/，
    /// narrow 的面包屑因此有 2 条死链）
    /// </summary>
    [Fact]
    public async Task 祖先链跳过不产页的合成目录()
    {
        var home = Node("首页", "/", "home");
        var docs = Node("文档区", "/docs/", "section");
        var deep = Node("深页", "/docs/guide/deep/", "page");
        var html = await RenderFor(deep, [home, docs, deep], AncestorTemplate);
        Assert.Equal("[文档区|section|/docs/][首页|home|/]N=2", html);
    }

    /// <summary>pager 段（<c>/posts/page/2/</c>）不是页面、更不是祖先：Hugo 只产出
    /// <c>/posts/page/N/</c>，不产出 <c>/posts/page/</c></summary>
    [Fact]
    public async Task 祖先链跳过分页段()
    {
        var home = Node("首页", "/", "home");
        var posts = Node("帖子区", "/posts/", "section");
        var pager2 = Node("第2页", "/posts/page/2/", "page");
        var html = await RenderFor(pager2, [home, posts, pager2], AncestorTemplate);
        Assert.Equal("[帖子区|section|/posts/][首页|home|/]N=2", html);
    }

    /// <summary>home 页自身没有祖先（Hugo 实测 N=0）</summary>
    [Fact]
    public async Task home页祖先链为空()
    {
        var home = Node("首页", "/", "home");
        var docs = Node("文档区", "/docs/", "section");
        var html = await RenderFor(home, [home, docs], AncestorTemplate);
        Assert.Equal("N=0", html);
    }

    /// <summary>term 页的祖先是 taxonomy 列表页再是 home（Hugo 实测：
    /// <c>/tags/x/</c> → <c>[Tags|taxonomy|/tags/][首页|home|/]</c>）；section 页只有 home</summary>
    [Fact]
    public async Task 词条页祖先含分类列表页()
    {
        var home = Node("首页", "/", "home");
        var tax = Node("Tags", "/tags/", "taxonomy");
        var term = Node("x", "/tags/x/", "term");
        var html = await RenderFor(term, [home, tax, term], AncestorTemplate);
        Assert.Equal("[Tags|taxonomy|/tags/][首页|home|/]N=2", html);

        var sectionHtml = await RenderFor(tax, [home, tax, term], AncestorTemplate);
        Assert.Equal("[首页|home|/]N=1", sectionHtml);
    }

    /// <summary>
    /// .Parent / .CurrentSection / .FirstSection：同一棵树上的三种投影，**探针值逐行锁定**
    /// （Hugo v0.166，站点 = home + /posts/（有 _index.md）+ /docs/ + /docs/guide/（有）
    /// + /docs/noindex/（**无** _index.md）+ /tags/ + /tags/x/ + 根级页 /p/）：
    /// <code>
    /// 页面                     .Parent        .CurrentSection  .FirstSection
    /// /                        nil            自身(home)        自身(home)
    /// /docs/                   home           自身(docs)        自身(docs)
    /// /docs/guide/             /docs/         自身(guide)       /docs/
    /// /docs/guide/deep/        /docs/guide/   /docs/guide/      /docs/
    /// /docs/noindex/deep2/     /docs/         /docs/            /docs/
    /// /tags/                   home           自身(Tags)        自身(Tags)
    /// /tags/x/                 /tags/         自身(term)        /tags/
    /// /posts/a/                /posts/        /posts/           /posts/
    /// /p/                      home           home              home
    /// </code>
    /// 关键点：① 不产页的合成目录（docs/noindex）不入链，其下页面的父级是**上一层真实
    /// section**；② 容器页（section/taxonomy/term/home）的 CurrentSection 是**自己**；
    /// ③ FirstSection 是**最外层** section（/docs/guide/ 的是 /docs/ 而非自己），
    /// term 页的最外层容器是分类列表页。
    /// </summary>
    // ---- 9. Hugo 的日期格式：time.Format（布局在前）与运行期 Go 布局 ----

    /// <summary>
    /// 迁移产物（Hugo <c>time.Format</c>）在管道形态下的参数序：
    /// Scriban 的管道把左值注入**首参**，Flint 的 <c>date.to_string</c> 又是 (值, 布局)
    /// → 只传布局串即可。此处锁定"管道左值 = 值、实参 = 布局"，并覆盖**运行期布局**
    /// （<c>site.Params.dateFormat</c> 这类表达式在迁移期看不到，只能由引擎按 Go 布局解析；
    /// 不解析会渲染出 <c>Januar26 2, 2006</c> 这类乱码——6 个主题的 <time> 实测）
    /// </summary>
    [Theory]
    [InlineData("{{ page.date | date.to_string \"2006-01-02\" }}", "2024-01-15")]
    [InlineData("{{ page.date | date.to_string (default \"\" \"2 Jan 2006\") }}", "15 Jan 2024")]
    [InlineData("{{ page.date | date.to_string (default \":date_long\" \"\") }}", "January 15, 2024")]
    public async Task 管道形态的日期格式按值在前布局在后(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    /// <summary><c>date.to_string</c>（迁移器为 <c>.Date.Format</c> 产出）同样要吃下运行期
    /// Go 布局：<c>.Site.Params.date_format</c> 常是 <c>2006-01-02</c> 这类 Go 串</summary>
    [Theory]
    [InlineData("{{ date.to_string page.date \"2006-01-02\" }}", "2024-01-15")]
    [InlineData("{{ date.to_string page.date \"January 2, 2006\" }}", "January 15, 2024")]
    [InlineData("{{ date.to_string page.date \":date_medium\" }}", "Jan 15, 2024")]
    [InlineData("{{ date.to_string page.date \"yyyy-MM-dd\" }}", "2024-01-15")]
    public async Task dateToString兼容Go布局与NET格式(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    /// <summary>
    /// 无日期页的 <c>.Date</c> 是**零值时间**（Hugo 探针：bearblog 直接渲染
    /// <c>01 Jan, 0001</c>、<c>datetime='0001-01-01'</c>；ananke/stack 用
    /// <c>.Date.IsZero</c> 守卫因而**不渲染**日期）。
    /// 迁移产物形态：<c>page.date.is_zero</c> → <c>date.is_zero page.date</c>
    /// </summary>
    [Fact]
    public async Task 零值日期的IsZero与渲染()
    {
        var undated = new PageContext
        {
            Title = "U",
            Content = "",
            Permalink = "https://example.com/u/",
            RelPermalink = "/u/",
            Date = DateTimeOffset.MinValue,
            Tags = [],
            Categories = [],
            WordCount = 0,
            ReadingTime = TimeSpan.Zero,
            Kind = "page"
        };
        var html = await RenderFor(
            undated,
            [undated],
            "{{ $z = date.is_zero page.date }}zero={{ $z }} fmt={{ page.date | date.to_string \"2006-01-02\" }}");
        Assert.Equal("zero=true fmt=0001-01-01", html);
    }

    /// <summary><c>date.is_zero</c> 对正常日期为 false；<c>date.unix</c> 给秒级时间戳</summary>
    [Theory]
    [InlineData("{{ date.is_zero page.date }}", "false")]
    [InlineData("{{ date.unix page.date }}", "1705314600")]
    public async Task 日期值方法的取值(string template, string expected)
    {
        Assert.Equal(expected, await Render(template));
    }

    [Fact]
    public async Task 父级与所属顶级section按Hugo语义()
    {
        var home = Node("首页", "/", "home");
        var posts = Node("帖子区", "/posts/", "section");
        var post = Node("帖子A", "/posts/a/", "page");
        var docs = Node("文档区", "/docs/", "section");
        var guide = Node("指南区", "/docs/guide/", "section");
        var deep = Node("深页", "/docs/guide/deep/", "page");
        var deep2 = Node("深页2", "/docs/noindex/deep2/", "page");
        var tax = Node("Tags", "/tags/", "taxonomy");
        var term = Node("X", "/tags/x/", "term");
        var root = Node("根页", "/p/", "page");
        var all = new[] { home, posts, post, docs, guide, deep, deep2, tax, term, root };

        // 与迁移产物同形的写法：`with .X` 归一为 `$v = page.x; if $v`（Scriban 无裸 `.x`）
        const string Tmpl =
            "{{ $p = page.parent; if $p }}P:[{{ $p.title }}|{{ $p.rel_permalink }}]{{ else }}P:{{ end }}" +
            "{{ $c = page.current_section; if $c }} C:[{{ $c.title }}|{{ $c.rel_permalink }}]{{ else }} C:{{ end }}" +
            "{{ $f = page.first_section; if $f }} F:[{{ $f.title }}|{{ $f.rel_permalink }}]{{ else }} F:{{ end }}";

        Assert.Equal("P: C:[首页|/] F:[首页|/]", await RenderFor(home, all, Tmpl));
        Assert.Equal(
            "P:[首页|/] C:[文档区|/docs/] F:[文档区|/docs/]",
            await RenderFor(docs, all, Tmpl));
        Assert.Equal(
            "P:[文档区|/docs/] C:[指南区|/docs/guide/] F:[文档区|/docs/]",
            await RenderFor(guide, all, Tmpl));
        Assert.Equal(
            "P:[指南区|/docs/guide/] C:[指南区|/docs/guide/] F:[文档区|/docs/]",
            await RenderFor(deep, all, Tmpl));
        Assert.Equal(
            "P:[文档区|/docs/] C:[文档区|/docs/] F:[文档区|/docs/]",
            await RenderFor(deep2, all, Tmpl));
        Assert.Equal(
            "P:[首页|/] C:[Tags|/tags/] F:[Tags|/tags/]",
            await RenderFor(tax, all, Tmpl));
        Assert.Equal(
            "P:[Tags|/tags/] C:[X|/tags/x/] F:[Tags|/tags/]",
            await RenderFor(term, all, Tmpl));
        Assert.Equal(
            "P:[帖子区|/posts/] C:[帖子区|/posts/] F:[帖子区|/posts/]",
            await RenderFor(post, all, Tmpl));
        Assert.Equal(
            "P:[首页|/] C:[首页|/] F:[首页|/]",
            await RenderFor(root, all, Tmpl));
    }

    /// <summary>祖先链是**页面集合**：可继续调用方法族（主题写 <c>.Ancestors.Reverse</c>
    /// 做面包屑——narrow 的 breadcrumb.html 实测），顺序为 home → … → 父级</summary>
    [Fact]
    public async Task 祖先链Reverse给出面包屑顺序()
    {
        var home = Node("首页", "/", "home");
        var docs = Node("文档区", "/docs/", "section");
        var guide = Node("指南区", "/docs/guide/", "section");
        var deep = Node("深页", "/docs/guide/deep/", "page");
        var html = await RenderFor(
            deep, [home, docs, guide, deep],
            "{{ for $it in as_list (page.ancestors.reverse) }}[{{ $it.title }}]{{ end }}");
        Assert.Equal("[首页][文档区][指南区]", html);
    }
}

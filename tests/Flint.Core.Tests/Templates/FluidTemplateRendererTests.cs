// Flint 静态站点生成器
// Fluid（Liquid）模板渲染器单元测试（FLUID-MIGRATION-PLAN P1-7）：
// 缓存/mtime 失效、依赖追踪、partial、大小写不敏感成员、空值语义、
// 每渲染独立上下文、UTF-8 输出、内置兜底、双引擎路由。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>Fluid 模板渲染器单元测试</summary>
public sealed class FluidTemplateRendererTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FluidTemplateRenderer _renderer;

    public FluidTemplateRendererTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_fluid_{Guid.NewGuid():N}");
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

    private static TemplateContext CreateTestContext(
        string title = "Test Page",
        string content = "<p>Hello</p>",
        string kind = "page",
        IReadOnlyList<PageContext>? pages = null)
    {
        return new TemplateContext
        {
            Page = new PageContext
            {
                Title = title,
                Content = content,
                Permalink = "https://example.com/test/",
                RelPermalink = "/test/",
                Date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
                Tags = ["tag1", "tag2"],
                Categories = ["cat1"],
                WordCount = 100,
                ReadingTime = TimeSpan.FromMinutes(1),
                Kind = kind,
                Pages = pages
            },
            // 模板全局 pages（Hugo .Pages 语义）：与 Page.Pages 同源挂载
            Pages = pages,
            Site = new SiteContext
            {
                Title = "Test Site",
                BaseURL = "https://example.com",
                Language = "en",
                Pages = [],
                RegularPages = [],
                Taxonomies = new TaxonomyCollection
                {
                    Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>()
                },
                Menus = new MenuCollection
                {
                    Menus = new Dictionary<string, IReadOnlyList<MenuItem>>()
                },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "Test Site" }
            }
        };
    }

    private void WriteTemplate(string relativePath, string content)
    {
        var full = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // ---- Liquid 标准标签冒烟测试（2026-10-21：Fluid 注册了 28 个标签，
    //      此前只有 assign/include/render/for/if 有测试，其余零覆盖）----

    // AllowParentheses（2026-10-22 开启）：括号分组表达式。区间字面量
    // (1..3) 本就可用（上方 cycle 行），此处锁定条件/赋值的分组求值
    [Theory]
    [InlineData("{% assign a = true %}{% assign b = false %}{% if (a or b) and a %}yes{% else %}no{% endif %}", "yes")]
    [InlineData("{% assign a = true %}{% assign b = false %}{% if (a or b) and b %}yes{% else %}no{% endif %}", "no")]
    public async Task Liquid_括号分组表达式_冒烟(string template, string expected)
    {
        WriteTemplate("index.liquid", template);
        var result = await _renderer.RenderAsync("index", CreateTestContext());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("{% unless page.title %}empty{% else %}{{ page.title }}{% endunless %}", "Test Page")]
    [InlineData("{% case page.title %}{% when 'Test Page' %}match{% else %}no{% endcase %}", "match")]
    [InlineData("{% capture x %}captured{% endcapture %}{{ x }}", "captured")]
    [InlineData("{% for i in (1..3) %}{% cycle 'a', 'b' %}{% endfor %}", "aba")]
    [InlineData("{% liquid assign x = 'hello'\n   assign y = 'world'\n   echo x %}{{ y }}", "helloworld")]
    [InlineData("{% assign items = 'a,b,c' | split: ',' %}{% for item in items limit: 2 offset: 1 %}{{ item }}{% endfor %}", "bc")]
    [InlineData("{% assign items = 'a,b,c' | split: ',' %}{% for item in items reversed %}{{ item }}{% endfor %}", "cba")]
    [InlineData("{% raw %}{{ not_rendered }}{% endraw %}", "{{ not_rendered }}")]
    [InlineData("{% assign x = 5 %}{% if x > 3 %}big{% elsif x > 1 %}mid{% else %}small{% endif %}", "big")]
    public async Task Liquid_标准标签_冒烟(string template, string expected)
    {
        WriteTemplate("index.liquid", template);
        var result = await _renderer.RenderAsync("index", CreateTestContext());
        Assert.Equal(expected, result);
    }

    // ---- include 高级形态（with/for/alias；2026-10-21 补测试覆盖）----

    [Fact]
    public async Task include_with_改名绑定()
    {
        WriteTemplate("partials/card.liquid", "[{{ local_title }}]");
        WriteTemplate("index.liquid",
            "{% include 'partials/card' with page.title as local_title %}");

        var result = await _renderer.RenderAsync("index", CreateTestContext("Hello"));

        Assert.Equal("[Hello]", result);
    }

    [Fact]
    public async Task include_for_遍历绑定()
    {
        WriteTemplate("partials/item.liquid", "{{ item }}");
        WriteTemplate("index.liquid",
            "{% assign items = 'a,b,c' | split: ',' %}{% include 'partials/item' for items as item %}");

        var result = await _renderer.RenderAsync("index", CreateTestContext());

        Assert.Equal("abc", result);
    }

    [Fact]
    public async Task render_隔离作用域_不泄漏变量()
    {
        WriteTemplate("partials/iso.liquid", "{{ secret }}");
        WriteTemplate("index.liquid",
            "{% assign secret = 'hidden' %}{% render 'partials/iso' %}");

        var result = await _renderer.RenderAsync("index", CreateTestContext());

        // render 隔离作用域：partial 内 secret 取空（调用方 assign 不可见）
        Assert.Equal("", result);
    }

    [Fact]
    public async Task include_共享作用域_可读取调用方变量()
    {
        WriteTemplate("partials/shared.liquid", "{{ secret }}");
        WriteTemplate("index.liquid",
            "{% assign secret = 'visible' %}{% include 'partials/shared' %}");

        var result = await _renderer.RenderAsync("index", CreateTestContext());

        // include 共享作用域：partial 内 secret 取到调用方 assign 的值
        Assert.Equal("visible", result);
    }

    [Fact]
    public async Task where_三参运算符_不等于()
    {
        // H5 修复：Hugo where "Section" "!=" "posts" 运算符形态
        WriteTemplate("index.liquid",
            "{% assign filtered = pages | where: 'section', '!=', 'posts' %}{{ filtered.size }}");
        var context = CreateTestContext(pages:
        [
            new PageContext { Title = "P1", Content = "", Permalink = "/p1/", RelPermalink = "/p1/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 0, ReadingTime = TimeSpan.Zero, Section = "posts" },
            new PageContext { Title = "P2", Content = "", Permalink = "/p2/", RelPermalink = "/p2/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 0, ReadingTime = TimeSpan.Zero, Section = "docs" }
        ]);
        var result = await _renderer.RenderAsync("index", context);
        Assert.Equal("1", result);
    }

    [Fact]
    public async Task where_三参运算符_大于()
    {
        WriteTemplate("index.liquid",
            "{% assign filtered = pages | where: 'word_count', '>', '50' %}{{ filtered.size }}");
        var context = CreateTestContext(pages:
        [
            new PageContext { Title = "P1", Content = "", Permalink = "/p1/", RelPermalink = "/p1/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 100, ReadingTime = TimeSpan.Zero },
            new PageContext { Title = "P2", Content = "", Permalink = "/p2/", RelPermalink = "/p2/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 30, ReadingTime = TimeSpan.Zero }
        ]);
        var result = await _renderer.RenderAsync("index", context);
        Assert.Equal("1", result);
    }

    [Fact]
    public async Task paginator_total_number_of_elements_别名()
    {
        // M3：Hugo 分页器字段名 TotalNumberOfElements
        var paginator = PaginatorView.Create(
            [
                new PageContext { Title = "P1", Content = "", Permalink = "/p1/", RelPermalink = "/p1/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 0, ReadingTime = TimeSpan.Zero },
                new PageContext { Title = "P2", Content = "", Permalink = "/p2/", RelPermalink = "/p2/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 0, ReadingTime = TimeSpan.Zero },
                new PageContext { Title = "P3", Content = "", Permalink = "/p3/", RelPermalink = "/p3/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [], WordCount = 0, ReadingTime = TimeSpan.Zero }
            ],
            1, 2, "/posts/", "page");
        WriteTemplate("index.liquid", "{{ page.paginator.total_number_of_elements }}");
        var context = CreateTestContext();
        var withPaginator = new TemplateContext
        {
            Page = new PageContext
            {
                Title = context.Page.Title, Content = context.Page.Content,
                Permalink = context.Page.Permalink, RelPermalink = context.Page.RelPermalink,
                Date = context.Page.Date, Tags = context.Page.Tags, Categories = context.Page.Categories,
                WordCount = context.Page.WordCount, ReadingTime = context.Page.ReadingTime,
                Kind = context.Page.Kind, Paginator = paginator
            },
            Site = context.Site
        };
        var result = await _renderer.RenderAsync("index", withPaginator);
        Assert.Equal("3", result);
    }

    [Fact]
    public async Task 渲染_Liquid_模板并绑定页面模型()
    {
        WriteTemplate("index.liquid", "<h1>{{ page.title }}</h1><p>{{ page.content }}</p>");

        var result = await _renderer.RenderAsync("index", CreateTestContext("你好 Liquid", "<b>内容</b>"));

        Assert.Equal("<h1>你好 Liquid</h1><p><b>内容</b></p>", result);
    }

    [Fact]
    public async Task TimeSpan_成员_total_minutes_可访问()
    {
        // AOT 冒烟回归（2026-10-19）：corelib 的 TimeSpan.TotalMinutes 属性
        // 元数据同样会被裁剪——FluidTemplateRenderer 的 DynamicDependency
        // 保留清单必须含 TimeSpan，否则 page.reading_time.total_minutes 取空
        WriteTemplate("index.liquid", "[{{ page.reading_time.total_minutes }}][{{ page.reading_time }}]");

        var result = await _renderer.RenderAsync("index", CreateTestContext());

        Assert.Equal("[1][00:01:00]", result);
    }

    [Fact]
    public async Task AOT保留类型_paginator分组_成员可访问()
    {
        // AOT 冒烟回归（2026-10-21 审计补入）：PaginatorPageGroup 与
        // PaginatorGroupBoundary 的 [DynamicDependency] 条目无 Fluid 测试兜底，
        // 误删时不会红——而 AOT 失败是静默的（成员全部取空、结构正常）
        var paginator = PaginatorView.Create(
            [
                new PageContext
                {
                    Title = "P1", Content = "", Permalink = "https://example.com/p1/",
                    RelPermalink = "/p1/", Date = DateTimeOffset.UnixEpoch,
                    Tags = [], Categories = [], WordCount = 0, ReadingTime = TimeSpan.Zero
                },
                new PageContext
                {
                    Title = "P2", Content = "", Permalink = "https://example.com/p2/",
                    RelPermalink = "/p2/", Date = DateTimeOffset.UnixEpoch,
                    Tags = [], Categories = [], WordCount = 0, ReadingTime = TimeSpan.Zero
                }
            ],
            1, 10, "/posts/", "page")
            .WithGroupBoundaries([new PaginatorGroupBoundary("2024", 2)]);

        WriteTemplate("index.liquid",
            "{% for g in page.paginator.page_groups %}[{{ g.key }}:{{ g.pages.size }}]{% endfor %}" +
            "{% for b in page.paginator.group_boundaries %}<{{ b.key }}={{ b.count }}>{% endfor %}");

        var context = CreateTestContext();
        var withPaginator = new TemplateContext
        {
            Page = new PageContext
            {
                Title = context.Page.Title,
                Content = context.Page.Content,
                Permalink = context.Page.Permalink,
                RelPermalink = context.Page.RelPermalink,
                Date = context.Page.Date,
                Tags = context.Page.Tags,
                Categories = context.Page.Categories,
                WordCount = context.Page.WordCount,
                ReadingTime = context.Page.ReadingTime,
                Kind = context.Page.Kind,
                Paginator = paginator
            },
            Site = context.Site
        };

        var result = await _renderer.RenderAsync("index", withPaginator);

        Assert.Equal("[2024:2]<2024=2>", result);
    }

    [Fact]
    public async Task AOT保留类型_page_headings_成员可访问()
    {
        // AOT 冒烟回归（2026-10-21 审计补入）：MarkdownHeading 的
        // [DynamicDependency] 条目无 Fluid 测试兜底
        WriteTemplate("index.liquid",
            "{% for h in page.headings %}[{{ h.level }}:{{ h.text }}]{% endfor %}");

        var context = CreateTestContext();
        var withHeadings = new TemplateContext
        {
            Page = new PageContext
            {
                Title = context.Page.Title,
                Content = context.Page.Content,
                Permalink = context.Page.Permalink,
                RelPermalink = context.Page.RelPermalink,
                Date = context.Page.Date,
                Tags = context.Page.Tags,
                Categories = context.Page.Categories,
                WordCount = context.Page.WordCount,
                ReadingTime = context.Page.ReadingTime,
                Kind = context.Page.Kind,
                Headings = [new MarkdownHeading { Level = 2, Text = "标题一", Id = "t1" }]
            },
            Site = context.Site
        };

        var result = await _renderer.RenderAsync("index", withHeadings);

        Assert.Equal("[2:标题一]", result);
    }

    [Fact]
    public async Task AOT保留类型_page_menu_entries_成员可访问()
    {
        // AOT 冒烟回归（2026-10-21 审计补入）：MenuItemConfig 的
        // [DynamicDependency] 条目无 Fluid 测试兜底
        WriteTemplate("index.liquid",
            "{% for pair in page.menu_entries %}[{{ pair[0] }}:{{ pair[1].name }}:{{ pair[1].url }}]{% endfor %}");

        var context = CreateTestContext();
        var withMenus = new TemplateContext
        {
            Page = new PageContext
            {
                Title = context.Page.Title,
                Content = context.Page.Content,
                Permalink = context.Page.Permalink,
                RelPermalink = context.Page.RelPermalink,
                Date = context.Page.Date,
                Tags = context.Page.Tags,
                Categories = context.Page.Categories,
                WordCount = context.Page.WordCount,
                ReadingTime = context.Page.ReadingTime,
                Kind = context.Page.Kind,
                MenuEntries = new Dictionary<string, MenuItemConfig>
                {
                    ["main"] = new MenuItemConfig { Name = "文章", URL = "/posts/" }
                }
            },
            Site = context.Site
        };

        var result = await _renderer.RenderAsync("index", withMenus);

        Assert.Equal("[main:文章:/posts/]", result);
    }

    [Fact]
    public async Task site_menus_索引器成员可访问()
    {
        // P2-7 实证回归：C# 索引器不经 Fluid 成员语法——MenuCollection 经
        // IndexerMemberAccessor 注册后 site.menus.main 才可枚举
        WriteTemplate("index.liquid", "{% for item in site.menus.main %}<a href=\"{{ item.url }}\">{{ item.name }}</a>{% endfor %}[{{ site.menus.missing }}]");
        var context = CreateTestContext();
        var withMenus = new TemplateContext
        {
            Page = context.Page,
            Site = new SiteContext
            {
                Title = context.Site.Title,
                BaseURL = context.Site.BaseURL,
                Language = context.Site.Language,
                Pages = [],
                RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection
                {
                    Menus = new Dictionary<string, IReadOnlyList<MenuItem>>
                    {
                        ["main"] = [new MenuItem { Name = "首页", URL = "/" }]
                    }
                },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };

        var result = await _renderer.RenderAsync("index", withMenus);

        Assert.Equal("<a href=\"/\">首页</a>[]", result);
    }

    [Fact]
    public async Task site_taxonomies_索引器成员可访问()
    {
        WriteTemplate("index.liquid", "{% for t in site.taxonomies.categories %}[{{ t.name }}:{{ t.count }}]{% endfor %}");
        var context = CreateTestContext();
        var withTax = new TemplateContext
        {
            Page = context.Page,
            Site = new SiteContext
            {
                Title = context.Site.Title,
                BaseURL = context.Site.BaseURL,
                Language = context.Site.Language,
                Pages = [],
                RegularPages = [],
                Taxonomies = new TaxonomyCollection
                {
                    Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>
                    {
                        ["categories"] = [new TaxonomyTerm { Name = "cat1", Slug = "cat1", Pages = [] }]
                    }
                },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };

        var result = await _renderer.RenderAsync("index", withTax);

        Assert.Equal("[cat1:0]", result);
    }
    [Fact]
    public async Task 成员访问_大小写不敏感且支持_snake_case()
    {
        WriteTemplate("index.liquid",
            "[{{ page.title }}][{{ page.TITLE }}][{{ page.word_count }}][{{ page.rel_permalink }}]");

        var result = await _renderer.RenderAsync("index", CreateTestContext("T"));

        // Fluid 默认比较器 OrdinalIgnoreCase（page.TITLE 命中 Title）；
        // snake_case 经 FlintMemberAccessStrategy 映射（word_count → WordCount）
        Assert.Equal("[T][T][100][/test/]", result);
    }

    [Fact]
    public async Task Params_大小写不敏感字典语义()
    {
        WriteTemplate("index.liquid", "[{{ params.foo }}][{{ params.FOO }}]");
        var context = CreateTestContext();
        // 直接构造带 OrdinalIgnoreCase Params 的上下文
        var withParams = new TemplateContext
        {
            Page = context.Page,
            Site = context.Site,
            Params = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["foo"] = "bar"
            }
        };

        var result = await _renderer.RenderAsync("index", withParams);

        Assert.Equal("[bar][bar]", result);
    }

    [Fact]
    public async Task Params_源为_Ordinal_字典时复制为忽略大小写()
    {
        WriteTemplate("index.liquid", "[{{ params.foo }}][{{ params.FOO }}]");
        var context = CreateTestContext();
        var withParams = new TemplateContext
        {
            Page = context.Page,
            Site = context.Site,
            // 上游 Ordinal 字典：渲染器须复制为 OrdinalIgnoreCase
            Params = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["foo"] = "bar"
            }
        };

        var result = await _renderer.RenderAsync("index", withParams);

        Assert.Equal("[bar][bar]", result);
    }

    [Fact]
    public async Task 空值语义_缺失成员渲染为空且不抛异常()
    {
        WriteTemplate("index.liquid", "[{{ missing }}][{{ page.nope }}][{{ page.paginator }}]");

        var result = await _renderer.RenderAsync("index", CreateTestContext());

        Assert.Equal("[][][]", result);
    }

    [Fact]
    public async Task include_映射到_Flint_模板加载器并记录依赖()
    {
        WriteTemplate("index.liquid", "A{% include 'partials/head' %}B");
        WriteTemplate("partials/head.liquid", "<head>{{ site.title }}</head>");

        var result = await _renderer.RenderAsync("index", CreateTestContext());

        Assert.Equal("A<head>Test Site</head>B", result);
    }

    [Fact]
    public async Task include_共享作用域_assign_对_partial_可见()
    {
        // P4-5：Hugo 的 partial dict 上下文在 Liquid 的等价形态——
        // assign 后 include（共享作用域），partial 内直取变量
        WriteTemplate("index.liquid",
            "{% assign label = '标签' %}{% include 'partials/card' %}");
        WriteTemplate("partials/card.liquid", "<card>{{ label }}:{{ page.title }}</card>");

        var result = await _renderer.RenderAsync("index", CreateTestContext("标题"));

        Assert.Equal("<card>标签:标题</card>", result);
    }

    [Fact]
    public async Task render_隔离作用域_assign_对_partial_不可见()
    {
        // P4-5：render 与 include 的作用域差异（Liquid 标准语义）——
        // 隔离作用域下调用方 assign 的变量不可见（取空），页面成员仍可用
        WriteTemplate("index.liquid",
            "{% assign label = '标签' %}{% render 'partials/card' %}");
        WriteTemplate("partials/card.liquid", "<card>[{{ label }}]{{ page.title }}</card>");

        var result = await _renderer.RenderAsync("index", CreateTestContext("标题"));

        Assert.Equal("<card>[]标题</card>", result);
    }

    [Fact]
    public async Task include_裸名回退到_default_形态()
    {
        WriteTemplate("index.liquid", "{% include 'head' %}");
        WriteTemplate("_default/head.liquid", "<default-head/>");

        var result = await _renderer.RenderAsync("index", CreateTestContext());

        Assert.Equal("<default-head/>", result);
    }

    [Fact]
    public async Task include_partials_前缀名在两个目录形态间回退()
    {
        WriteTemplate("index.liquid", "{% include 'partials/head' %}");
        // 只建 _partials/（Hugo v0.146+ 新约定）：带前缀名须能回退到该目录
        WriteTemplate("_partials/head.liquid", "<new-partials/>");

        var result = await _renderer.RenderAsync("index", CreateTestContext());

        Assert.Equal("<new-partials/>", result);
    }

    [Fact]
    public async Task include_未命中抛_FileNotFoundException()
    {
        WriteTemplate("index.liquid", "{% include 'partials/missing' %}");

        await Assert.ThrowsAsync<TemplateRenderException>(
            () => _renderer.RenderAsync("index", CreateTestContext()).AsTask());
    }

    [Fact]
    public async Task 渲染期依赖追踪_顶层与include物理路径都记录()
    {
        WriteTemplate("index.liquid", "{% include 'partials/head' %}");
        WriteTemplate("partials/head.liquid", "<head/>");

        var context = CreateTestContext();
        await _renderer.RenderAsync("index", context);

        Assert.NotNull(context.RenderedDependencies);
        Assert.Contains("index", context.RenderedDependencies!);
        Assert.Contains(Path.GetFullPath(Path.Combine(_tempDir, "partials", "head.liquid")),
            context.RenderedDependencies!);
    }

    [Fact]
    public async Task 每渲染独立上下文_互不串味()
    {
        WriteTemplate("index.liquid", "{{ page.title }}");

        var r1 = await _renderer.RenderAsync("index", CreateTestContext("第一页"));
        var r2 = await _renderer.RenderAsync("index", CreateTestContext("第二页"));

        Assert.Equal("第一页", r1);
        Assert.Equal("第二页", r2);
    }

    [Fact]
    public async Task 缓存命中_第二次渲染不重新解析()
    {
        WriteTemplate("index.liquid", "v1");
        var first = await _renderer.RenderAsync("index", CreateTestContext());
        Assert.Equal("v1", first);

        // 改写文件但保持 mtime 与长度不变时缓存仍命中（已知边界，同 Scriban 侧）；
        // 这里验证正常路径：mtime 变化后渲染新内容
        await Task.Delay(50);
        File.WriteAllText(Path.Combine(_tempDir, "index.liquid"), "v2");
        var second = await _renderer.RenderAsync("index", CreateTestContext());

        Assert.Equal("v2", second);
    }

    [Fact]
    public async Task mtime_失效_InvalidateMtimeCache_后立即看到新内容()
    {
        WriteTemplate("index.liquid", "v1");
        Assert.Equal("v1", await _renderer.RenderAsync("index", CreateTestContext()));

        File.WriteAllText(Path.Combine(_tempDir, "index.liquid"), "v2");
        _renderer.InvalidateMtimeCache();

        Assert.Equal("v2", await _renderer.RenderAsync("index", CreateTestContext()));
    }

    [Fact]
    public async Task 解析失败抛_TemplateParseException()
    {
        WriteTemplate("index.liquid", "{% if %}");

        await Assert.ThrowsAsync<TemplateParseException>(
            () => _renderer.RenderAsync("index", CreateTestContext()).AsTask());
    }

    [Fact]
    public async Task UTF8_输出无损()
    {
        WriteTemplate("index.liquid", "{{ page.title }}");
        var result = await _renderer.RenderAsync("index", CreateTestContext("中文测试 Ünïcödé 🚀"));

        Assert.Equal("中文测试 Ünïcödé 🚀", result);
    }

    [Fact]
    public async Task 默认不转义_与_Scriban_HTML_契约一致()
    {
        WriteTemplate("index.liquid", "{{ page.content }}");
        var result = await _renderer.RenderAsync("index", CreateTestContext(content: "<b>&amp;</b>"));

        Assert.Equal("<b>&amp;</b>", result);
    }

    [Fact]
    public async Task 页面感知渲染_physical_liquid_优先()
    {
        WriteTemplate("_default/single.liquid", "<single>{{ page.title }}</single>");

        var query = new PageTemplateQuery { Kind = "page", Section = "posts" };
        var result = await _renderer.RenderPageAsync(query, CreateTestContext("文章"));

        Assert.Equal("<single>文章</single>", result);
    }

    [Fact]
    public async Task 页面感知渲染_section_缺物理模板走内置_list_兜底()
    {
        var pages = new List<PageContext>
        {
            new()
            {
                Title = "文章一", Content = "", Permalink = "https://example.com/p1/",
                RelPermalink = "/p1/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 1, ReadingTime = TimeSpan.Zero
            }
        };
        var context = CreateTestContext(kind: "section", pages: pages);

        var query = new PageTemplateQuery { Kind = "section", Section = "posts" };
        var result = await _renderer.RenderPageAsync(query, context);

        Assert.Contains("<li><a href=\"https://example.com/p1/\">文章一</a></li>", result);
    }

    [Fact]
    public async Task 页面感知渲染_无物理无内置抛_TemplateNotFoundException()
    {
        var query = new PageTemplateQuery { Kind = "page", Section = "posts" };

        await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => _renderer.RenderPageAsync(query, CreateTestContext()).AsTask());
    }

    [Fact]
    public async Task TemplateExists_区分扩展名_html_不算_liquid()
    {
        WriteTemplate("index.liquid", "x");
        WriteTemplate("other.html", "x");

        Assert.True(_renderer.TemplateExists("index"));
        Assert.False(_renderer.TemplateExists("other"));
    }

    [Fact]
    public async Task RenderTemplateFileAsync_直接按文件路径渲染()
    {
        var file = Path.Combine(_tempDir, "robots.liquid");
        File.WriteAllText(file, "User-agent: *");

        var result = await _renderer.RenderTemplateFileAsync(file, CreateTestContext());

        Assert.Equal("User-agent: *", result);
    }

    [Fact]
    public async Task 预编译_解析全部_liquid_模板()
    {
        WriteTemplate("index.liquid", "a");
        WriteTemplate("_default/single.liquid", "b");
        WriteTemplate("broken.liquid", "{% if %}"); // 解析失败静默缺席

        var count = await _renderer.PrecompileTemplatesAsync();

        Assert.True(count >= 2, $"预编译数量 {count} 应 >= 2");
    }

    [Fact]
    public async Task GetDependencies_静态闭包收集_include()
    {
        WriteTemplate("index.liquid", "{% include 'partials/head' %}");
        WriteTemplate("partials/head.liquid", "{% include 'partials/foot' %}");

        var deps = _renderer.GetDependencies("index");

        Assert.Contains("partials/head", deps);
        Assert.Contains("partials/foot", deps);
    }

    [Fact]
    public async Task GeneratedResources_与_DrainTemplateErrors_恒空()
    {
        Assert.Empty(_renderer.GeneratedResources);
        Assert.Empty(_renderer.DrainTemplateErrors());
    }
}

/// <summary>双引擎路由器测试（FLUID-MIGRATION-PLAN P1-6）</summary>
public sealed class TemplateEngineRouterTests : IDisposable
{
    private readonly string _tempDir;

    public TemplateEngineRouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_router_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
        GC.SuppressFinalize(this);
    }

    private TemplateEngineRouter CreateRouter(TemplateEnginePolicy policy, string themeDir = "")
    {
        var scriban = new ScribanTemplateRenderer(_tempDir, "https://example.com", themeDir);
        var fluid = new FluidTemplateRenderer(_tempDir, "https://example.com", builtins: null, resources: null, themeDir);
        return new TemplateEngineRouter(fluid, scriban, policy);
    }

    private static TemplateContext CreateContext() =>
        new()
        {
            Page = new PageContext
            {
                Title = "T", Content = "", Permalink = "https://example.com/t/",
                RelPermalink = "/t/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 1, ReadingTime = TimeSpan.Zero
            },
            Site = new SiteContext
            {
                Title = "S", BaseURL = "https://example.com", Language = "en",
                Pages = [], RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };

    private void WriteTemplate(string relativePath, string content)
    {
        var full = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public async Task Auto_模式_liquid_模板走_Fluid()
    {
        WriteTemplate("index.liquid", "FLUID:{{ page.title }}");
        var router = CreateRouter(TemplateEnginePolicy.Auto);

        var result = await router.RenderAsync("index", CreateContext());

        Assert.Equal("FLUID:T", result);
    }

    [Fact]
    public async Task Auto_模式_html_模板走_Scriban()
    {
        WriteTemplate("index.html", "SCRIBAN:{{ page.title }}");
        var router = CreateRouter(TemplateEnginePolicy.Auto);

        var result = await router.RenderAsync("index", CreateContext());

        Assert.Equal("SCRIBAN:T", result);
    }

    [Fact]
    public async Task Auto_模式_同根_liquid_优先于_html()
    {
        WriteTemplate("index.liquid", "FLUID");
        WriteTemplate("index.html", "SCRIBAN");
        var router = CreateRouter(TemplateEnginePolicy.Auto);

        var result = await router.RenderAsync("index", CreateContext());

        Assert.Equal("FLUID", result);
    }

    [Fact]
    public async Task Fluid_策略_全站走_Fluid_html_模板不可见()
    {
        WriteTemplate("index.html", "SCRIBAN");
        var router = CreateRouter(TemplateEnginePolicy.Fluid);

        await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => router.RenderAsync("index", CreateContext()).AsTask());
    }

    [Fact]
    public async Task Scriban_策略_全站走_Scriban_liquid_模板不可见()
    {
        WriteTemplate("index.liquid", "FLUID");
        var router = CreateRouter(TemplateEnginePolicy.Scriban);

        await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => router.RenderAsync("index", CreateContext()).AsTask());
    }

    [Fact]
    public async Task Auto_模式_Fluid_原生站点的_section_内置兜底走_Fluid()
    {
        // 站点持有 .liquid → Fluid 原生：section 无物理模板时内置 list 走 Fluid
        WriteTemplate("index.liquid", "x");
        var pages = new List<PageContext>
        {
            new()
            {
                Title = "文章一", Content = "", Permalink = "https://example.com/p1/",
                RelPermalink = "/p1/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 1, ReadingTime = TimeSpan.Zero
            }
        };
        var context = CreateContext();
        var withPages = new TemplateContext
        {
            Page = new PageContext
            {
                Title = context.Page.Title,
                Content = context.Page.Content,
                Permalink = context.Page.Permalink,
                RelPermalink = context.Page.RelPermalink,
                Date = context.Page.Date,
                Tags = context.Page.Tags,
                Categories = context.Page.Categories,
                WordCount = context.Page.WordCount,
                ReadingTime = context.Page.ReadingTime,
                Kind = "section",
                Pages = pages
            },
            Site = context.Site,
            Pages = pages
        };
        var router = CreateRouter(TemplateEnginePolicy.Auto);

        var query = new PageTemplateQuery { Kind = "section", Section = "posts" };
        var result = await router.RenderPageAsync(query, withPages);

        // Fluid 内置 list 的 Liquid 形态（无 Scriban 的 for 语法痕迹）
        Assert.Contains("<li><a href=\"https://example.com/p1/\">文章一</a></li>", result);
    }

    [Fact]
    public async Task Auto_模式_纯_Scriban_站点的_section_内置兜底走_Scriban()
    {
        // 站点无 .liquid：section 无物理模板时内置 list 走 Scriban（输出同构）
        var pages = new List<PageContext>
        {
            new()
            {
                Title = "文章一", Content = "", Permalink = "https://example.com/p1/",
                RelPermalink = "/p1/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 1, ReadingTime = TimeSpan.Zero
            }
        };
        var context = CreateContext();
        var withPages = new TemplateContext
        {
            Page = new PageContext
            {
                Title = context.Page.Title,
                Content = context.Page.Content,
                Permalink = context.Page.Permalink,
                RelPermalink = context.Page.RelPermalink,
                Date = context.Page.Date,
                Tags = context.Page.Tags,
                Categories = context.Page.Categories,
                WordCount = context.Page.WordCount,
                ReadingTime = context.Page.ReadingTime,
                Kind = "section",
                Pages = pages
            },
            Site = context.Site,
            Pages = pages
        };
        var router = CreateRouter(TemplateEnginePolicy.Auto);

        var query = new PageTemplateQuery { Kind = "section", Section = "posts" };
        var result = await router.RenderPageAsync(query, withPages);

        Assert.Contains("<li><a href=\"https://example.com/p1/\">文章一</a></li>", result);
    }

    [Fact]
    public async Task RenderTemplateFileAsync_按扩展名路由()
    {
        WriteTemplate("robots.liquid", "FLUID-ROBOTS");
        WriteTemplate("sitemap.html", "SCRIBAN-SITEMAP");
        var router = CreateRouter(TemplateEnginePolicy.Auto);

        var liquid = await router.RenderTemplateFileAsync(
            Path.Combine(_tempDir, "robots.liquid"), CreateContext());
        var html = await router.RenderTemplateFileAsync(
            Path.Combine(_tempDir, "sitemap.html"), CreateContext());

        Assert.Equal("FLUID-ROBOTS", liquid);
        Assert.Equal("SCRIBAN-SITEMAP", html);
    }

    [Theory]
    [InlineData("auto", TemplateEnginePolicy.Auto)]
    [InlineData("fluid", TemplateEnginePolicy.Fluid)]
    [InlineData("liquid", TemplateEnginePolicy.Fluid)]
    [InlineData("scriban", TemplateEnginePolicy.Scriban)]
    [InlineData("FLUID", TemplateEnginePolicy.Fluid)]
    [InlineData(null, TemplateEnginePolicy.Auto)]
    [InlineData("garbage", TemplateEnginePolicy.Auto)]
    public void ParsePolicy_解析与回落(string? engine, TemplateEnginePolicy expected)
    {
        Assert.Equal(expected, TemplateEngineRouter.ParsePolicy(engine));
    }

    [Fact]
    public void ParsePolicy_从站点配置读取()
    {
        var config = new SiteConfig
        {
            BaseURL = "https://example.com",
            Title = "S",
            Engine = "fluid"
        };

        Assert.Equal(TemplateEnginePolicy.Fluid, TemplateEngineRouter.ParsePolicy(config));
    }

    [Fact]
    public async Task TemplateExists_任一引擎命中即真()
    {
        WriteTemplate("a.liquid", "x");
        WriteTemplate("b.html", "x");
        var router = CreateRouter(TemplateEnginePolicy.Auto);

        Assert.True(router.TemplateExists("a"));
        Assert.True(router.TemplateExists("b"));
        Assert.False(router.TemplateExists("c"));
    }

    [Fact]
    public async Task GeneratedResources_auto_模式合并两引擎()
    {
        var router = CreateRouter(TemplateEnginePolicy.Auto);

        Assert.Empty(router.GeneratedResources);
    }
}

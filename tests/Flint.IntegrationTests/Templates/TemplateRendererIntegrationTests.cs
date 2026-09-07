// Flint 静态站点生成器
// 模板渲染器集成测试
// 测试单页模板渲染、列表模板渲染、partial 模板包含、内置函数执行、条件渲染、循环渲染、模板继承
// _Requirements: 5.4, 5.5, 5.6, 5.7_

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Templates;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Templates;

/// <summary>
/// 模板渲染器集成测试
/// 验证 ScribanTemplateRenderer 的各种渲染功能
/// </summary>
public class TemplateRendererIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _layoutsDir;
    private readonly string _partialsDir;
    private ScribanTemplateRenderer _renderer;

    public TemplateRendererIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-template-test-{Guid.NewGuid():N}");
        _layoutsDir = Path.Combine(_tempDir, "layouts");
        _partialsDir = Path.Combine(_layoutsDir, "partials");

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_layoutsDir);
        Directory.CreateDirectory(_partialsDir);

        _renderer = new ScribanTemplateRenderer(_layoutsDir, "https://example.com");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    #region 辅助方法

    /// <summary>
    /// 创建模板文件
    /// </summary>
    private void CreateTemplate(string name, string content)
    {
        var path = Path.Combine(_layoutsDir, name.EndsWith(".html") ? name : name + ".html");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    /// <summary>
    /// 创建 partial 模板文件
    /// </summary>
    private void CreatePartial(string name, string content)
    {
        var path = Path.Combine(_partialsDir, name.EndsWith(".html") ? name : name + ".html");
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    /// <summary>
    /// 创建测试页面上下文
    /// </summary>
    private static PageContext CreatePageContext(
        string title = "测试页面",
        string content = "<p>测试内容</p>",
        DateTimeOffset? date = null,
        string[]? tags = null,
        string[]? categories = null,
        string? description = null,
        string? summary = null,
        bool draft = false,
        int weight = 0,
        string? type = null,
        string? layout = null)
    {
        return new PageContext
        {
            Title = title,
            Content = content,
            Permalink = $"https://example.com/{title.ToLowerInvariant().Replace(" ", "-")}/",
            RelPermalink = $"/{title.ToLowerInvariant().Replace(" ", "-")}/",
            Date = date ?? DateTimeOffset.Now,
            Tags = tags ?? [],
            Categories = categories ?? [],
            WordCount = content.Length / 5,
            ReadingTime = TimeSpan.FromMinutes(content.Length / 200.0),
            Description = description,
            Summary = summary,
            Draft = draft,
            Weight = weight,
            Type = type,
            Layout = layout
        };
    }

    /// <summary>
    /// 创建测试站点上下文
    /// </summary>
    private static SiteContext CreateSiteContext(
        string title = "测试站点",
        string baseUrl = "https://example.com",
        string language = "zh-CN",
        IReadOnlyList<PageContext>? pages = null,
        IReadOnlyDictionary<string, object>? params_ = null)
    {
        var pageList = pages ?? [];
        return new SiteContext
        {
            Title = title,
            BaseURL = baseUrl,
            Language = language,
            Pages = pageList,
            RegularPages = pageList,
            Taxonomies = new TaxonomyCollection
            {
                Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>()
            },
            Menus = new MenuCollection
            {
                Menus = new Dictionary<string, IReadOnlyList<MenuItem>>()
            },
            Config = new { },
            Params = params_ ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// 创建模板上下文
    /// </summary>
    private static TemplateContext CreateTemplateContext(
        PageContext? page = null,
        SiteContext? site = null,
        bool isHome = false,
        bool isList = false,
        bool isSingle = true,
        IReadOnlyDictionary<string, object>? params_ = null,
        IReadOnlyDictionary<string, object>? data = null)
    {
        return new TemplateContext
        {
            Page = page ?? CreatePageContext(),
            Site = site ?? CreateSiteContext(),
            IsHome = isHome,
            IsList = isList,
            IsSingle = isSingle,
            Params = params_ ?? new Dictionary<string, object>(),
            Data = data ?? new Dictionary<string, object>()
        };
    }

    #endregion

    #region 单页模板渲染测试

    /// <summary>
    /// 测试基本单页模板渲染 - 页面标题
    /// </summary>
    [Fact]
    public async Task RenderAsync_SinglePageTemplate_ShouldRenderPageTitle()
    {
        // Arrange
        CreateTemplate("single", "<h1>{{ page.title }}</h1>");
        var context = CreateTemplateContext(
            page: CreatePageContext(title: "我的测试文章"));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<h1>我的测试文章</h1>");
    }

    /// <summary>
    /// 测试单页模板渲染 - 页面内容
    /// </summary>
    [Fact]
    public async Task RenderAsync_SinglePageTemplate_ShouldRenderPageContent()
    {
        // Arrange
        CreateTemplate("single", "<article>{{ page.content }}</article>");
        var context = CreateTemplateContext(
            page: CreatePageContext(content: "<p>这是文章正文内容。</p>"));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<article><p>这是文章正文内容。</p></article>");
    }

    /// <summary>
    /// 测试单页模板渲染 - 页面日期
    /// </summary>
    [Fact]
    public async Task RenderAsync_SinglePageTemplate_ShouldRenderPageDate()
    {
        // Arrange
        // 使用 .NET 日期格式字符串
        CreateTemplate("single", "<time>{{ page.date | date_format 'yyyy-MM-dd' }}</time>");
        var testDate = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(8));
        var context = CreateTemplateContext(
            page: CreatePageContext(date: testDate));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<time>2024-06-15</time>");
    }

    /// <summary>
    /// 测试单页模板渲染 - 页面标签
    /// </summary>
    [Fact]
    public async Task RenderAsync_SinglePageTemplate_ShouldRenderPageTags()
    {
        // Arrange
        CreateTemplate("single", """
            <ul class="tags">
            {{ for tag in page.tags }}
              <li>{{ tag }}</li>
            {{ end }}
            </ul>
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(tags: ["C#", "编程", "教程"]));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<li>C#</li>");
        result.Should().Contain("<li>编程</li>");
        result.Should().Contain("<li>教程</li>");
    }

    /// <summary>
    /// 测试单页模板渲染 - 页面分类
    /// </summary>
    [Fact]
    public async Task RenderAsync_SinglePageTemplate_ShouldRenderPageCategories()
    {
        // Arrange
        CreateTemplate("single", """
            <ul class="categories">
            {{ for cat in page.categories }}
              <li>{{ cat }}</li>
            {{ end }}
            </ul>
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(categories: ["技术", "开发"]));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<li>技术</li>");
        result.Should().Contain("<li>开发</li>");
    }

    /// <summary>
    /// 测试单页模板渲染 - 页面描述
    /// </summary>
    [Fact]
    public async Task RenderAsync_SinglePageTemplate_ShouldRenderPageDescription()
    {
        // Arrange
        CreateTemplate("single", "<meta name=\"description\" content=\"{{ page.description }}\">");
        var context = CreateTemplateContext(
            page: CreatePageContext(description: "这是一篇关于 C# 编程的文章"));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("content=\"这是一篇关于 C# 编程的文章\"");
    }

    /// <summary>
    /// 测试单页模板渲染 - 页面永久链接
    /// </summary>
    [Fact]
    public async Task RenderAsync_SinglePageTemplate_ShouldRenderPagePermalink()
    {
        // Arrange
        CreateTemplate("single", "<a href=\"{{ page.permalink }}\">永久链接</a>");
        var context = CreateTemplateContext(
            page: CreatePageContext(title: "test-post"));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("href=\"https://example.com/test-post/\"");
    }

    /// <summary>
    /// 测试单页模板渲染 - 字数统计和阅读时间
    /// </summary>
    [Fact]
    public async Task RenderAsync_SinglePageTemplate_ShouldRenderWordCountAndReadingTime()
    {
        // Arrange
        CreateTemplate("single", """
            <span class="word-count">{{ page.word_count }} 字</span>
            <span class="reading-time">{{ page.reading_time.TotalMinutes | round 0 }} 分钟</span>
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(content: new string('字', 1000)));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("class=\"word-count\"");
        result.Should().Contain("class=\"reading-time\"");
    }

    /// <summary>
    /// 测试不同页面类型的单页模板渲染
    /// </summary>
    [Theory]
    [InlineData("post", "文章")]
    [InlineData("page", "页面")]
    [InlineData("article", "文章")]
    [InlineData("tutorial", "教程")]
    public async Task RenderAsync_SinglePageTemplate_DifferentPageTypes_ShouldRenderCorrectly(
        string pageType, string expectedLabel)
    {
        // Arrange
        CreateTemplate("single", """
            {{ if page.type == "post" }}文章{{ else if page.type == "page" }}页面{{ else if page.type == "article" }}文章{{ else if page.type == "tutorial" }}教程{{ else }}其他{{ end }}
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(type: pageType));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expectedLabel);
    }

    #endregion

    #region 列表模板渲染测试（分页场景）

    /// <summary>
    /// 测试列表模板渲染 - 基本页面列表
    /// </summary>
    [Fact]
    public async Task RenderAsync_ListTemplate_ShouldRenderPageList()
    {
        // Arrange
        CreateTemplate("list", """
            <ul class="posts">
            {{ for p in site.pages }}
              <li><a href="{{ p.permalink }}">{{ p.title }}</a></li>
            {{ end }}
            </ul>
            """);

        var pages = new[]
        {
            CreatePageContext(title: "文章一"),
            CreatePageContext(title: "文章二"),
            CreatePageContext(title: "文章三")
        };
        var context = CreateTemplateContext(
            site: CreateSiteContext(pages: pages),
            isList: true,
            isSingle: false);

        // Act
        var result = await _renderer.RenderAsync("list", context);

        // Assert
        result.Should().Contain("文章一");
        result.Should().Contain("文章二");
        result.Should().Contain("文章三");
        result.Should().Contain("<ul class=\"posts\">");
    }

    /// <summary>
    /// 测试列表模板渲染 - 按日期排序
    /// </summary>
    [Fact]
    public async Task RenderAsync_ListTemplate_ShouldRenderSortedByDate()
    {
        // Arrange
        CreateTemplate("list", """
            {{ for p in site.pages | sort 'date' }}
              <article>{{ p.title }} - {{ p.date | date_format '%Y-%m-%d' }}</article>
            {{ end }}
            """);

        var pages = new[]
        {
            CreatePageContext(title: "旧文章", date: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            CreatePageContext(title: "新文章", date: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero)),
            CreatePageContext(title: "中间文章", date: new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero))
        };
        var context = CreateTemplateContext(
            site: CreateSiteContext(pages: pages),
            isList: true,
            isSingle: false);

        // Act
        var result = await _renderer.RenderAsync("list", context);

        // Assert
        result.Should().Contain("旧文章");
        result.Should().Contain("新文章");
        result.Should().Contain("中间文章");
    }

    /// <summary>
    /// 测试列表模板渲染 - 限制数量
    /// </summary>
    [Fact]
    public async Task RenderAsync_ListTemplate_ShouldLimitPageCount()
    {
        // Arrange
        CreateTemplate("list", """
            {{ for p in site.pages | slice 0 2 }}
              <article>{{ p.title }}</article>
            {{ end }}
            """);

        var pages = new[]
        {
            CreatePageContext(title: "文章一"),
            CreatePageContext(title: "文章二"),
            CreatePageContext(title: "文章三"),
            CreatePageContext(title: "文章四"),
            CreatePageContext(title: "文章五")
        };
        var context = CreateTemplateContext(
            site: CreateSiteContext(pages: pages),
            isList: true,
            isSingle: false);

        // Act
        var result = await _renderer.RenderAsync("list", context);

        // Assert
        result.Should().Contain("文章一");
        result.Should().Contain("文章二");
        result.Should().NotContain("文章三");
        result.Should().NotContain("文章四");
        result.Should().NotContain("文章五");
    }

    /// <summary>
    /// 测试列表模板渲染 - 空列表处理
    /// </summary>
    [Fact]
    public async Task RenderAsync_ListTemplate_EmptyList_ShouldRenderEmptyMessage()
    {
        // Arrange
        CreateTemplate("list", """
            {{ if site.pages | len == 0 }}
              <p class="empty">暂无文章</p>
            {{ else }}
              {{ for p in site.pages }}
                <article>{{ p.title }}</article>
              {{ end }}
            {{ end }}
            """);

        var context = CreateTemplateContext(
            site: CreateSiteContext(pages: []),
            isList: true,
            isSingle: false);

        // Act
        var result = await _renderer.RenderAsync("list", context);

        // Assert
        result.Should().Contain("<p class=\"empty\">暂无文章</p>");
    }

    /// <summary>
    /// 测试列表模板渲染 - 分页信息
    /// </summary>
    [Fact]
    public async Task RenderAsync_ListTemplate_ShouldRenderPaginationInfo()
    {
        // Arrange
        CreateTemplate("list", """
            <div class="pagination">
              <span>共 {{ site.pages | len }} 篇文章</span>
            </div>
            """);

        var pages = Enumerable.Range(1, 25)
            .Select(i => CreatePageContext(title: $"文章{i}"))
            .ToArray();
        var context = CreateTemplateContext(
            site: CreateSiteContext(pages: pages),
            isList: true,
            isSingle: false);

        // Act
        var result = await _renderer.RenderAsync("list", context);

        // Assert
        result.Should().Contain("共 25 篇文章");
    }

    #endregion

    #region Partial 模板包含测试（多层嵌套）

    /// <summary>
    /// 测试 partial 模板包含 - 基本包含
    /// </summary>
    [Fact]
    public async Task RenderAsync_PartialTemplate_ShouldIncludePartial()
    {
        // Arrange
        CreatePartial("header", "<header><h1>{{ site.title }}</h1></header>");
        CreateTemplate("single", """
            {{ include 'partials/header' }}
            <main>{{ page.content }}</main>
            """);

        var context = CreateTemplateContext(
            site: CreateSiteContext(title: "我的博客"));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<header><h1>我的博客</h1></header>");
        result.Should().Contain("<main>");
    }

    /// <summary>
    /// 测试 partial 模板包含 - 多个 partial
    /// </summary>
    [Fact]
    public async Task RenderAsync_PartialTemplate_ShouldIncludeMultiplePartials()
    {
        // Arrange
        CreatePartial("header", "<header>头部</header>");
        CreatePartial("footer", "<footer>底部</footer>");
        CreatePartial("sidebar", "<aside>侧边栏</aside>");
        CreateTemplate("single", """
            {{ include 'partials/header' }}
            <main>{{ page.content }}</main>
            {{ include 'partials/sidebar' }}
            {{ include 'partials/footer' }}
            """);

        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<header>头部</header>");
        result.Should().Contain("<footer>底部</footer>");
        result.Should().Contain("<aside>侧边栏</aside>");
    }

    /// <summary>
    /// 测试 partial 模板包含 - 两层嵌套
    /// </summary>
    [Fact]
    public async Task RenderAsync_PartialTemplate_TwoLevelNesting_ShouldRenderCorrectly()
    {
        // Arrange
        CreatePartial("nav-item", "<li>{{ item }}</li>");
        CreatePartial("nav", """
            <nav>
              <ul>
                {{ for item in items }}
                  {{ include 'partials/nav-item' }}
                {{ end }}
              </ul>
            </nav>
            """);
        CreateTemplate("single", """
            {{ items = ["首页", "关于", "联系"] }}
            {{ include 'partials/nav' }}
            <main>{{ page.content }}</main>
            """);

        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<nav>");
        result.Should().Contain("<ul>");
    }

    /// <summary>
    /// 测试 partial 模板包含 - 三层嵌套
    /// </summary>
    [Fact]
    public async Task RenderAsync_PartialTemplate_ThreeLevelNesting_ShouldRenderCorrectly()
    {
        // Arrange
        CreatePartial("icon", "<i class=\"icon\">★</i>");
        CreatePartial("button", """
            <button>{{ include 'partials/icon' }} {{ text }}</button>
            """);
        CreatePartial("toolbar", """
            <div class="toolbar">
              {{ text = "保存" }}{{ include 'partials/button' }}
              {{ text = "取消" }}{{ include 'partials/button' }}
            </div>
            """);
        CreateTemplate("single", """
            {{ include 'partials/toolbar' }}
            <main>{{ page.content }}</main>
            """);

        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<div class=\"toolbar\">");
        result.Should().Contain("<button>");
        result.Should().Contain("<i class=\"icon\">★</i>");
    }

    /// <summary>
    /// 测试 partial 模板包含 - 传递变量
    /// </summary>
    [Fact]
    public async Task RenderAsync_PartialTemplate_ShouldPassVariables()
    {
        // Arrange
        CreatePartial("card", """
            <div class="card">
              <h3>{{ card_title }}</h3>
              <p>{{ card_content }}</p>
            </div>
            """);
        CreateTemplate("single", """
            {{ card_title = "卡片标题" }}
            {{ card_content = "卡片内容" }}
            {{ include 'partials/card' }}
            """);

        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<h3>卡片标题</h3>");
        result.Should().Contain("<p>卡片内容</p>");
    }

    /// <summary>
    /// 测试 partial 模板包含 - 访问页面上下文
    /// </summary>
    [Fact]
    public async Task RenderAsync_PartialTemplate_ShouldAccessPageContext()
    {
        // Arrange
        CreatePartial("meta", """
            <meta name="title" content="{{ page.title }}">
            <meta name="description" content="{{ page.description }}">
            """);
        CreateTemplate("single", """
            <head>
              {{ include 'partials/meta' }}
            </head>
            """);

        var context = CreateTemplateContext(
            page: CreatePageContext(
                title: "测试标题",
                description: "测试描述"));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("content=\"测试标题\"");
        result.Should().Contain("content=\"测试描述\"");
    }

    #endregion

    #region 内置函数执行测试（日期、字符串、数学函数）

    /// <summary>
    /// 测试日期函数 - date_format
    /// </summary>
    [Theory]
    [InlineData("yyyy-MM-dd", "2024-06-15")]
    [InlineData("yyyy年MM月dd日", "2024年06月15日")]
    [InlineData("MM/dd/yyyy", "06/15/2024")]
    public async Task RenderAsync_DateFunction_DateFormat_ShouldFormatCorrectly(
        string format, string expected)
    {
        // Arrange
        CreateTemplate("single", $"{{{{ page.date | date_format '{format}' }}}}");
        var testDate = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(8));
        var context = CreateTemplateContext(
            page: CreatePageContext(date: testDate));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// 测试日期函数 - now
    /// </summary>
    [Fact]
    public async Task RenderAsync_DateFunction_Now_ShouldReturnCurrentDate()
    {
        // Arrange
        CreateTemplate("single", "{{ now | date_format 'yyyy' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(DateTime.Now.Year.ToString());
    }

    /// <summary>
    /// 测试字符串函数 - upper
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_Upper_ShouldConvertToUpperCase()
    {
        // Arrange
        CreateTemplate("single", "{{ 'hello world' | upper }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("HELLO WORLD");
    }

    /// <summary>
    /// 测试字符串函数 - lower
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_Lower_ShouldConvertToLowerCase()
    {
        // Arrange
        CreateTemplate("single", "{{ 'HELLO WORLD' | lower }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("hello world");
    }

    /// <summary>
    /// 测试字符串函数 - title
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_Title_ShouldCapitalizeWords()
    {
        // Arrange
        CreateTemplate("single", "{{ 'hello world' | title }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("Hello World");
    }

    /// <summary>
    /// 测试字符串函数 - trim
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_Trim_ShouldRemoveWhitespace()
    {
        // Arrange
        CreateTemplate("single", "[{{ '  hello  ' | trim }}]");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("[hello]");
    }

    /// <summary>
    /// 测试字符串函数 - replace
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_Replace_ShouldReplaceText()
    {
        // Arrange
        CreateTemplate("single", "{{ 'hello world' | replace 'world' 'Flint' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("hello Flint");
    }

    /// <summary>
    /// 测试字符串函数 - truncate
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_Truncate_ShouldTruncateText()
    {
        // Arrange
        CreateTemplate("single", "{{ '这是一段很长的文本内容' | truncate 10 '...' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().EndWith("...");
        result.Trim().Length.Should().BeLessThanOrEqualTo(10);
    }

    /// <summary>
    /// 测试字符串函数 - split 和 join
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_SplitAndJoin_ShouldWork()
    {
        // Arrange
        CreateTemplate("single", "{{ 'a,b,c' | split ',' | join '-' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("a-b-c");
    }

    /// <summary>
    /// 测试字符串函数 - has_prefix
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_HasPrefix_ShouldCheckPrefix()
    {
        // Arrange
        CreateTemplate("single", "{{ if 'hello world' | has_prefix 'hello' }}是{{ else }}否{{ end }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("是");
    }

    /// <summary>
    /// 测试字符串函数 - has_suffix
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_HasSuffix_ShouldCheckSuffix()
    {
        // Arrange
        CreateTemplate("single", "{{ if 'hello world' | has_suffix 'world' }}是{{ else }}否{{ end }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("是");
    }

    /// <summary>
    /// 测试字符串函数 - contains
    /// </summary>
    [Fact]
    public async Task RenderAsync_StringFunction_Contains_ShouldCheckContains()
    {
        // Arrange
        CreateTemplate("single", "{{ if 'hello world' | contains 'lo wo' }}是{{ else }}否{{ end }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("是");
    }

    /// <summary>
    /// 测试数学函数 - add（使用 Scriban 原生加法）
    /// </summary>
    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(10, 20, 30)]
    [InlineData(-5, 10, 5)]
    public async Task RenderAsync_MathFunction_Add_ShouldAddNumbers(int a, int b, int expected)
    {
        // Arrange
        // 使用 Scriban 原生加法运算符
        CreateTemplate("single", $"{{{{ {a} + {b} }}}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected.ToString());
    }

    /// <summary>
    /// 测试数学函数 - sub（使用 Scriban 原生减法）
    /// </summary>
    [Theory]
    [InlineData(10, 3, 7)]
    [InlineData(5, 10, -5)]
    public async Task RenderAsync_MathFunction_Sub_ShouldSubtractNumbers(int a, int b, int expected)
    {
        // Arrange
        // 使用 Scriban 原生减法运算符
        CreateTemplate("single", $"{{{{ {a} - {b} }}}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected.ToString());
    }

    /// <summary>
    /// 测试数学函数 - mul
    /// </summary>
    [Theory]
    [InlineData(3, 4, 12)]
    [InlineData(5, 0, 0)]
    [InlineData(-2, 3, -6)]
    public async Task RenderAsync_MathFunction_Mul_ShouldMultiplyNumbers(int a, int b, int expected)
    {
        // Arrange
        // 使用 Scriban 原生乘法运算符
        CreateTemplate("single", $"{{{{ {a} * {b} }}}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected.ToString());
    }

    /// <summary>
    /// 测试数学函数 - div（使用 Scriban 原生除法）
    /// </summary>
    [Fact]
    public async Task RenderAsync_MathFunction_Div_ShouldDivideNumbers()
    {
        // Arrange
        // 使用 Scriban 原生除法运算符
        CreateTemplate("single", "{{ 10 / 2 }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("5");
    }

    /// <summary>
    /// 测试数学函数 - mod（使用 Scriban 原生取模）
    /// </summary>
    [Theory]
    [InlineData(10, 3, 1)]
    [InlineData(15, 5, 0)]
    [InlineData(7, 2, 1)]
    public async Task RenderAsync_MathFunction_Mod_ShouldCalculateModulo(int a, int b, int expected)
    {
        // Arrange
        // 使用 Scriban 原生取模运算符
        CreateTemplate("single", $"{{{{ {a} % {b} }}}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected.ToString());
    }

    /// <summary>
    /// 测试数学函数 - ceil
    /// </summary>
    [Fact]
    public async Task RenderAsync_MathFunction_Ceil_ShouldCeilNumber()
    {
        // Arrange
        CreateTemplate("single", "{{ ceil 3.2 }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("4");
    }

    /// <summary>
    /// 测试数学函数 - floor
    /// </summary>
    [Fact]
    public async Task RenderAsync_MathFunction_Floor_ShouldFloorNumber()
    {
        // Arrange
        CreateTemplate("single", "{{ floor 3.8 }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("3");
    }

    /// <summary>
    /// 测试数学函数 - round
    /// </summary>
    [Theory]
    [InlineData("3.4", 0, "3")]
    [InlineData("3.5", 0, "4")]
    [InlineData("3.14159", 2, "3.14")]
    public async Task RenderAsync_MathFunction_Round_ShouldRoundNumber(
        string number, int decimals, string expected)
    {
        // Arrange
        CreateTemplate("single", $"{{{{ round {number} {decimals} }}}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// 测试数学函数 - abs
    /// </summary>
    [Theory]
    [InlineData(-5, "5")]
    [InlineData(5, "5")]
    [InlineData(0, "0")]
    public async Task RenderAsync_MathFunction_Abs_ShouldReturnAbsoluteValue(int number, string expected)
    {
        // Arrange
        // 使用管道语法调用 abs 函数
        CreateTemplate("single", $"{{{{ {number} | abs }}}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    #endregion

    #region 条件渲染和循环渲染测试

    /// <summary>
    /// 测试条件渲染 - if 语句
    /// </summary>
    [Fact]
    public async Task RenderAsync_ConditionalRendering_If_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("single", """
            {{ if page.draft }}
              <span class="draft">草稿</span>
            {{ end }}
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(draft: true));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<span class=\"draft\">草稿</span>");
    }

    /// <summary>
    /// 测试条件渲染 - if-else 语句
    /// </summary>
    [Theory]
    [InlineData(true, "草稿")]
    [InlineData(false, "已发布")]
    public async Task RenderAsync_ConditionalRendering_IfElse_ShouldRenderCorrectly(
        bool isDraft, string expected)
    {
        // Arrange
        CreateTemplate("single", """
            {{ if page.draft }}草稿{{ else }}已发布{{ end }}
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(draft: isDraft));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// 测试条件渲染 - if-else if-else 语句
    /// </summary>
    [Theory]
    [InlineData(0, "无权重")]
    [InlineData(5, "低权重")]
    [InlineData(50, "中权重")]
    [InlineData(100, "高权重")]
    public async Task RenderAsync_ConditionalRendering_IfElseIfElse_ShouldRenderCorrectly(
        int weight, string expected)
    {
        // Arrange
        CreateTemplate("single", """
            {{ if page.weight == 0 }}无权重{{ else if page.weight < 10 }}低权重{{ else if page.weight < 80 }}中权重{{ else }}高权重{{ end }}
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(weight: weight));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// 测试条件渲染 - 嵌套 if 语句
    /// </summary>
    [Fact]
    public async Task RenderAsync_ConditionalRendering_NestedIf_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("single", """
            {{ if page.draft }}
              {{ if page.weight > 0 }}
                <span>重要草稿</span>
              {{ else }}
                <span>普通草稿</span>
              {{ end }}
            {{ end }}
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(draft: true, weight: 10));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<span>重要草稿</span>");
    }

    /// <summary>
    /// 测试循环渲染 - for 循环
    /// </summary>
    [Fact]
    public async Task RenderAsync_LoopRendering_For_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("single", """
            <ul>
            {{ for tag in page.tags }}
              <li>{{ tag }}</li>
            {{ end }}
            </ul>
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(tags: ["C#", "编程", "教程"]));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<li>C#</li>");
        result.Should().Contain("<li>编程</li>");
        result.Should().Contain("<li>教程</li>");
    }

    /// <summary>
    /// 测试循环渲染 - for 循环带索引
    /// </summary>
    [Fact]
    public async Task RenderAsync_LoopRendering_ForWithIndex_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("single", """
            <ol>
            {{ for tag in page.tags }}
              <li>{{ for.index + 1 }}. {{ tag }}</li>
            {{ end }}
            </ol>
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(tags: ["第一", "第二", "第三"]));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<li>1. 第一</li>");
        result.Should().Contain("<li>2. 第二</li>");
        result.Should().Contain("<li>3. 第三</li>");
    }

    /// <summary>
    /// 测试循环渲染 - for 循环带 first/last 判断
    /// </summary>
    [Fact]
    public async Task RenderAsync_LoopRendering_ForWithFirstLast_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("single", """
            {{ for tag in page.tags }}{{ if for.first }}[{{ end }}{{ tag }}{{ if !for.last }}, {{ end }}{{ if for.last }}]{{ end }}{{ end }}
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(tags: ["A", "B", "C"]));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("[A, B, C]");
    }

    /// <summary>
    /// 测试循环渲染 - 嵌套 for 循环
    /// </summary>
    [Fact]
    public async Task RenderAsync_LoopRendering_NestedFor_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("single", """
            {{ rows = [[1, 2], [3, 4], [5, 6]] }}
            <table>
            {{ for row in rows }}
              <tr>
              {{ for cell in row }}
                <td>{{ cell }}</td>
              {{ end }}
              </tr>
            {{ end }}
            </table>
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().Contain("<table>");
        result.Should().Contain("<tr>");
        result.Should().Contain("<td>1</td>");
        result.Should().Contain("<td>6</td>");
    }

    /// <summary>
    /// 测试循环渲染 - 空集合处理
    /// </summary>
    [Fact]
    public async Task RenderAsync_LoopRendering_EmptyCollection_ShouldRenderNothing()
    {
        // Arrange
        CreateTemplate("single", """
            {{ for tag in page.tags }}
              <span>{{ tag }}</span>
            {{ end }}
            """);
        var context = CreateTemplateContext(
            page: CreatePageContext(tags: []));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Should().NotContain("<span>");
    }

    /// <summary>
    /// 测试循环渲染 - range 函数
    /// </summary>
    [Fact]
    public async Task RenderAsync_LoopRendering_Range_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("single", """
            {{ for i in range 5 }}{{ i }}{{ if !for.last }},{{ end }}{{ end }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("0,1,2,3,4");
    }

    #endregion

    #region 模板继承测试

    /// <summary>
    /// 测试模板继承 - 基本布局
    /// </summary>
    [Fact]
    public async Task RenderAsync_TemplateInheritance_BasicLayout_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("_default/baseof", """
            <!DOCTYPE html>
            <html>
            <head><title>{{ page.title }}</title></head>
            <body>
              {{ include 'partials/header' }}
              <main>{{ page.content }}</main>
              {{ include 'partials/footer' }}
            </body>
            </html>
            """);
        CreatePartial("header", "<header>网站头部</header>");
        CreatePartial("footer", "<footer>网站底部</footer>");

        var context = CreateTemplateContext(
            page: CreatePageContext(
                title: "测试页面",
                content: "<p>页面内容</p>"));

        // Act
        var result = await _renderer.RenderAsync("_default/baseof", context);

        // Assert
        result.Should().Contain("<!DOCTYPE html>");
        result.Should().Contain("<title>测试页面</title>");
        result.Should().Contain("<header>网站头部</header>");
        result.Should().Contain("<p>页面内容</p>");
        result.Should().Contain("<footer>网站底部</footer>");
    }

    /// <summary>
    /// 测试模板继承 - 站点信息
    /// </summary>
    [Fact]
    public async Task RenderAsync_TemplateInheritance_SiteInfo_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("_default/baseof", """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
              <title>{{ page.title }} | {{ site.title }}</title>
              <base href="{{ site.base_url }}">
            </head>
            <body>
              <main>{{ page.content }}</main>
            </body>
            </html>
            """);

        var context = CreateTemplateContext(
            page: CreatePageContext(title: "关于我们"),
            site: CreateSiteContext(
                title: "我的博客",
                baseUrl: "https://myblog.com",
                language: "zh-CN"));

        // Act
        var result = await _renderer.RenderAsync("_default/baseof", context);

        // Assert
        result.Should().Contain("lang=\"zh-CN\"");
        result.Should().Contain("<title>关于我们 | 我的博客</title>");
        result.Should().Contain("href=\"https://myblog.com\"");
    }

    /// <summary>
    /// 测试模板继承 - 条件性内容块
    /// </summary>
    [Fact]
    public async Task RenderAsync_TemplateInheritance_ConditionalBlocks_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("_default/baseof", """
            <!DOCTYPE html>
            <html>
            <head>
              <title>{{ page.title }}</title>
              {{ if page.description }}
              <meta name="description" content="{{ page.description }}">
              {{ end }}
            </head>
            <body>
              {{ if is_home }}
              <div class="hero">欢迎来到首页</div>
              {{ end }}
              <main>{{ page.content }}</main>
            </body>
            </html>
            """);

        var context = CreateTemplateContext(
            page: CreatePageContext(
                title: "首页",
                description: "这是网站首页"),
            isHome: true);

        // Act
        var result = await _renderer.RenderAsync("_default/baseof", context);

        // Assert
        result.Should().Contain("<meta name=\"description\" content=\"这是网站首页\">");
        result.Should().Contain("<div class=\"hero\">欢迎来到首页</div>");
    }

    /// <summary>
    /// 测试模板继承 - 多种页面类型
    /// </summary>
    [Theory]
    [InlineData(true, false, false, "首页")]
    [InlineData(false, true, false, "列表页")]
    [InlineData(false, false, true, "详情页")]
    public async Task RenderAsync_TemplateInheritance_PageTypes_ShouldRenderCorrectly(
        bool isHome, bool isList, bool isSingle, string expected)
    {
        // Arrange
        CreateTemplate("_default/baseof", """
            {{ if is_home }}首页{{ else if is_list }}列表页{{ else if is_single }}详情页{{ else }}其他{{ end }}
            """);

        var context = CreateTemplateContext(
            isHome: isHome,
            isList: isList,
            isSingle: isSingle);

        // Act
        var result = await _renderer.RenderAsync("_default/baseof", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    #endregion

    #region URL 函数测试

    /// <summary>
    /// 测试 URL 函数 - absURL
    /// </summary>
    [Theory]
    [InlineData("/about", "https://example.com/about")]
    [InlineData("posts/hello", "https://example.com/posts/hello")]
    [InlineData("https://other.com/page", "https://other.com/page")]
    public async Task RenderAsync_UrlFunction_AbsUrl_ShouldConvertToAbsoluteUrl(
        string input, string expected)
    {
        // Arrange
        CreateTemplate("single", $"{{{{ absURL '{input}' }}}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// 测试 URL 函数 - relURL
    /// </summary>
    [Theory]
    [InlineData("about", "/about")]
    [InlineData("/posts/hello", "/posts/hello")]
    public async Task RenderAsync_UrlFunction_RelUrl_ShouldConvertToRelativeUrl(
        string input, string expected)
    {
        // Arrange
        CreateTemplate("single", $"{{{{ relURL '{input}' }}}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    #endregion

    #region 编码函数测试

    /// <summary>
    /// 测试编码函数 - base64Encode
    /// </summary>
    [Fact]
    public async Task RenderAsync_EncodingFunction_Base64Encode_ShouldEncodeCorrectly()
    {
        // Arrange
        CreateTemplate("single", "{{ base64Encode 'Hello World' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("SGVsbG8gV29ybGQ=");
    }

    /// <summary>
    /// 测试编码函数 - base64Decode
    /// </summary>
    [Fact]
    public async Task RenderAsync_EncodingFunction_Base64Decode_ShouldDecodeCorrectly()
    {
        // Arrange
        CreateTemplate("single", "{{ base64Decode 'SGVsbG8gV29ybGQ=' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("Hello World");
    }

    /// <summary>
    /// 测试编码函数 - htmlEscape
    /// </summary>
    [Fact]
    public async Task RenderAsync_EncodingFunction_HtmlEscape_ShouldEscapeCorrectly()
    {
        // Arrange
        CreateTemplate("single", "{{ htmlEscape '<script>alert(1)</script>' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Contain("&lt;script&gt;");
        result.Trim().Should().NotContain("<script>");
    }

    /// <summary>
    /// 测试编码函数 - md5
    /// </summary>
    [Fact]
    public async Task RenderAsync_EncodingFunction_Md5_ShouldHashCorrectly()
    {
        // Arrange
        CreateTemplate("single", "{{ md5 'hello' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("5d41402abc4b2a76b9719d911017c592");
    }

    #endregion

    #region 集合函数测试

    /// <summary>
    /// 测试集合函数 - len
    /// </summary>
    [Fact]
    public async Task RenderAsync_CollectionFunction_Len_ShouldReturnLength()
    {
        // Arrange
        CreateTemplate("single", "{{ page.tags | len }}");
        var context = CreateTemplateContext(
            page: CreatePageContext(tags: ["A", "B", "C", "D", "E"]));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("5");
    }

    /// <summary>
    /// 测试集合函数 - first
    /// </summary>
    [Fact]
    public async Task RenderAsync_CollectionFunction_First_ShouldReturnFirstElement()
    {
        // Arrange
        CreateTemplate("single", "{{ page.tags | first }}");
        var context = CreateTemplateContext(
            page: CreatePageContext(tags: ["第一", "第二", "第三"]));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("第一");
    }

    /// <summary>
    /// 测试集合函数 - last
    /// </summary>
    [Fact]
    public async Task RenderAsync_CollectionFunction_Last_ShouldReturnLastElement()
    {
        // Arrange
        CreateTemplate("single", "{{ page.tags | last }}");
        var context = CreateTemplateContext(
            page: CreatePageContext(tags: ["第一", "第二", "第三"]));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("第三");
    }

    /// <summary>
    /// 测试集合函数 - reverse
    /// </summary>
    [Fact]
    public async Task RenderAsync_CollectionFunction_Reverse_ShouldReverseCollection()
    {
        // Arrange
        CreateTemplate("single", "{{ for t in page.tags | reverse }}{{ t }}{{ end }}");
        var context = CreateTemplateContext(
            page: CreatePageContext(tags: ["A", "B", "C"]));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("CBA");
    }

    /// <summary>
    /// 测试集合函数 - uniq
    /// </summary>
    [Fact]
    public async Task RenderAsync_CollectionFunction_Uniq_ShouldRemoveDuplicates()
    {
        // Arrange
        CreateTemplate("single", "{{ arr = ['A', 'B', 'A', 'C', 'B'] }}{{ arr | uniq | len }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("3");
    }

    #endregion

    #region 比较函数测试

    /// <summary>
    /// 测试比较函数 - eq
    /// </summary>
    [Theory]
    [InlineData("'a'", "'a'", true)]
    [InlineData("'a'", "'b'", false)]
    [InlineData("1", "1", true)]
    [InlineData("1", "2", false)]
    public async Task RenderAsync_ComparisonFunction_Eq_ShouldCompareCorrectly(
        string a, string b, bool expected)
    {
        // Arrange
        CreateTemplate("single", $"{{{{ if eq {a} {b} }}}}是{{{{ else }}}}否{{{{ end }}}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be(expected ? "是" : "否");
    }

    /// <summary>
    /// 测试比较函数 - default
    /// </summary>
    [Fact]
    public async Task RenderAsync_ComparisonFunction_Default_ShouldReturnDefaultValue()
    {
        // Arrange
        CreateTemplate("single", "{{ page.description | default '无描述' }}");
        var context = CreateTemplateContext(
            page: CreatePageContext(description: null));

        // Act
        var result = await _renderer.RenderAsync("single", context);

        // Assert
        result.Trim().Should().Be("无描述");
    }

    #endregion

    #region 综合测试

    /// <summary>
    /// 测试综合场景 - 完整博客文章页面
    /// </summary>
    [Fact]
    public async Task RenderAsync_CompleteScenario_BlogPostPage_ShouldRenderCorrectly()
    {
        // Arrange
        CreatePartial("header", """
            <header>
              <nav>
                <a href="{{ site.base_url }}">{{ site.title }}</a>
              </nav>
            </header>
            """);
        CreatePartial("footer", """
            <footer>
              <p>© {{ now | date_format 'yyyy' }} {{ site.title }}</p>
            </footer>
            """);
        CreateTemplate("post", """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
              <meta charset="UTF-8">
              <title>{{ page.title }} | {{ site.title }}</title>
              {{ if page.description }}
              <meta name="description" content="{{ page.description }}">
              {{ end }}
            </head>
            <body>
              {{ include 'partials/header' }}
              <main>
                <article>
                  <h1>{{ page.title }}</h1>
                  <div class="meta">
                    <time>{{ page.date | date_format 'yyyy-MM-dd' }}</time>
                    <span>{{ page.word_count }} 字</span>
                    {{ if page.tags | len > 0 }}
                    <div class="tags">
                      {{ for tag in page.tags }}
                      <span class="tag">{{ tag }}</span>
                      {{ end }}
                    </div>
                    {{ end }}
                  </div>
                  <div class="content">
                    {{ page.content }}
                  </div>
                </article>
              </main>
              {{ include 'partials/footer' }}
            </body>
            </html>
            """);

        var testDate = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.FromHours(8));
        var context = CreateTemplateContext(
            page: CreatePageContext(
                title: "我的第一篇博客文章",
                content: "<p>这是文章的正文内容，包含了很多有趣的信息。</p>",
                date: testDate,
                description: "这是一篇关于编程的文章",
                tags: ["C#", "编程", "教程"]),
            site: CreateSiteContext(
                title: "我的技术博客",
                baseUrl: "https://myblog.com",
                language: "zh-CN"));

        // Act
        var result = await _renderer.RenderAsync("post", context);

        // Assert
        result.Should().Contain("<!DOCTYPE html>");
        result.Should().Contain("lang=\"zh-CN\"");
        result.Should().Contain("<title>我的第一篇博客文章 | 我的技术博客</title>");
        result.Should().Contain("content=\"这是一篇关于编程的文章\"");
        result.Should().Contain("<h1>我的第一篇博客文章</h1>");
        result.Should().Contain("2024-06-15");
        result.Should().Contain("<span class=\"tag\">C#</span>");
        result.Should().Contain("<span class=\"tag\">编程</span>");
        result.Should().Contain("<span class=\"tag\">教程</span>");
        result.Should().Contain("这是文章的正文内容");
        result.Should().Contain("<header>");
        result.Should().Contain("<footer>");
    }

    /// <summary>
    /// 测试综合场景 - 文章列表页面
    /// </summary>
    [Fact]
    public async Task RenderAsync_CompleteScenario_BlogListPage_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("list", """
            <!DOCTYPE html>
            <html>
            <head><title>文章列表 | {{ site.title }}</title></head>
            <body>
              <h1>所有文章</h1>
              <p>共 {{ site.pages | len }} 篇文章</p>
              <ul class="posts">
              {{ for p in site.pages }}
                <li>
                  <a href="{{ p.permalink }}">{{ p.title }}</a>
                  <time>{{ p.date | date_format 'yyyy-MM-dd' }}</time>
                  {{ if p.draft }}<span class="draft">草稿</span>{{ end }}
                </li>
              {{ end }}
              </ul>
            </body>
            </html>
            """);

        var pages = new[]
        {
            CreatePageContext(title: "文章一", date: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            CreatePageContext(title: "文章二", date: new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), draft: true),
            CreatePageContext(title: "文章三", date: new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero))
        };
        var context = CreateTemplateContext(
            site: CreateSiteContext(title: "我的博客", pages: pages),
            isList: true,
            isSingle: false);

        // Act
        var result = await _renderer.RenderAsync("list", context);

        // Assert
        result.Should().Contain("<title>文章列表 | 我的博客</title>");
        result.Should().Contain("共 3 篇文章");
        result.Should().Contain("文章一");
        result.Should().Contain("文章二");
        result.Should().Contain("文章三");
        result.Should().Contain("<span class=\"draft\">草稿</span>");
    }

    #endregion
}

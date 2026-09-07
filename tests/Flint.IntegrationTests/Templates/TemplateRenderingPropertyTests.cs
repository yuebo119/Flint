// Flint 静态站点生成器
// 模板渲染正确性属性测试
// **Property 13: 模板渲染正确性**
// 生成随机页面上下文，验证渲染结果包含正确数据
// 验证 partial 模板正确包含
// 验证内置函数正确执行
// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**

#pragma warning disable CA1056 // URI 属性应为 Uri 类型 - 测试中使用字符串更方便

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Templates;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.Templates;

/// <summary>
/// 模板渲染正确性属性测试
/// **Feature: Flint-integration-tests, Property 13: 模板渲染正确性**
/// 验证模板渲染器对各种输入的正确处理
/// </summary>
public class TemplateRenderingPropertyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _layoutsDir;
    private readonly string _partialsDir;
    private readonly ScribanTemplateRenderer _renderer;

    public TemplateRenderingPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-template-prop-{Guid.NewGuid():N}");
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
        string title,
        string content,
        DateTimeOffset date,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> categories,
        string? description = null,
        bool draft = false,
        int weight = 0)
    {
        return new PageContext
        {
            Title = title,
            Content = content,
            Permalink = $"https://example.com/{title.ToLowerInvariant().Replace(" ", "-")}/",
            RelPermalink = $"/{title.ToLowerInvariant().Replace(" ", "-")}/",
            Date = date,
            Tags = tags,
            Categories = categories,
            WordCount = content.Length / 5,
            ReadingTime = TimeSpan.FromMinutes(Math.Max(1, content.Length / 200.0)),
            Description = description,
            Draft = draft,
            Weight = weight
        };
    }

    /// <summary>
    /// 创建测试站点上下文
    /// </summary>
    private static SiteContext CreateSiteContext(
        string title,
        string baseUrl,
        string language,
        IReadOnlyList<PageContext>? pages = null)
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
            Config = new Flint.Core.Configuration.SiteConfig { BaseURL = "https://example.com", Title = "T" }
        };
    }

    /// <summary>
    /// 创建模板上下文
    /// </summary>
    private static TemplateContext CreateTemplateContext(
        PageContext page,
        SiteContext site,
        bool isHome = false,
        bool isList = false,
        bool isSingle = true)
    {
        return new TemplateContext
        {
            Page = page,
            Site = site,
            IsHome = isHome,
            IsList = isList,
            IsSingle = isSingle,
            Params = new Dictionary<string, object>(),
            Data = new Dictionary<string, object>()
        };
    }

    #endregion

    #region Property 13: 模板渲染正确性 - 属性测试

    /// <summary>
    /// Property 13: 模板渲染正确性 - 页面标题渲染
    /// 对于任意有效的页面标题，渲染结果应包含该标题
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplateTestArbitraries) })]
    public Property TemplateRendering_PageTitle_ShouldBeIncludedInOutput(
        TemplatePageTitleTestData testData)
    {
        // Arrange
        CreateTemplate("test-title", "<h1>{{ page.title }}</h1>");
        var page = CreatePageContext(
            title: testData.Title,
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = _renderer.RenderAsync("test-title", context).AsTask().Result;

        // Assert
        var containsTitle = result.Contains(testData.Title);

        return containsTitle
            .Label($"Title='{testData.Title}', ContainsTitle={containsTitle}")
            .Classify(testData.Title.Length <= 10, "短标题")
            .Classify(testData.Title.Length > 10 && testData.Title.Length <= 50, "中等标题")
            .Classify(testData.Title.Length > 50, "长标题");
    }

    /// <summary>
    /// Property 13: 模板渲染正确性 - 页面内容渲染
    /// 对于任意有效的页面内容，渲染结果应包含该内容
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplateTestArbitraries) })]
    public Property TemplateRendering_PageContent_ShouldBeIncludedInOutput(
        TemplatePageContentTestData testData)
    {
        // Arrange
        CreateTemplate("test-content", "<article>{{ page.content }}</article>");
        var page = CreatePageContext(
            title: "测试",
            content: testData.Content,
            date: DateTimeOffset.Now,
            tags: [],
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = _renderer.RenderAsync("test-content", context).AsTask().Result;

        // Assert
        var containsContent = result.Contains(testData.Content);

        return containsContent
            .Label($"ContentLength={testData.Content.Length}, ContainsContent={containsContent}")
            .Classify(testData.Content.Length <= 100, "短内容")
            .Classify(testData.Content.Length > 100, "长内容");
    }

    /// <summary>
    /// Property 13: 模板渲染正确性 - 标签列表渲染
    /// 对于任意有效的标签列表，渲染结果应包含所有标签
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplateTestArbitraries) })]
    public Property TemplateRendering_Tags_ShouldAllBeIncludedInOutput(
        TemplateTagsTestData testData)
    {
        // Arrange
        CreateTemplate("test-tags", """
            {{ for tag in page.tags }}<span class="tag">{{ tag }}</span>{{ end }}
            """);
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: testData.Tags,
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = _renderer.RenderAsync("test-tags", context).AsTask().Result;

        // Assert
        var allTagsIncluded = testData.Tags.All(tag => result.Contains(tag));

        return allTagsIncluded
            .Label($"TagCount={testData.Tags.Count}, AllTagsIncluded={allTagsIncluded}")
            .Classify(testData.Tags.Count == 0, "无标签")
            .Classify(testData.Tags.Count > 0 && testData.Tags.Count <= 3, "少量标签")
            .Classify(testData.Tags.Count > 3, "多标签");
    }

    /// <summary>
    /// Property 13: 模板渲染正确性 - 站点信息渲染
    /// 对于任意有效的站点信息，渲染结果应包含站点标题和基础 URL
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplateTestArbitraries) })]
    public Property TemplateRendering_SiteInfo_ShouldBeIncludedInOutput(
        TemplateSiteInfoTestData testData)
    {
        // Arrange
        CreateTemplate("test-site", """
            <title>{{ site.title }}</title>
            <base href="{{ site.base_url }}">
            <html lang="{{ site.language }}">
            """);
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: []);
        var site = CreateSiteContext(testData.Title, testData.BaseUrl, testData.Language);
        var context = CreateTemplateContext(page, site);

        // Act
        var result = _renderer.RenderAsync("test-site", context).AsTask().Result;

        // Assert
        var containsTitle = result.Contains(testData.Title);
        var containsBaseUrl = result.Contains(testData.BaseUrl);
        var containsLanguage = result.Contains(testData.Language);

        return (containsTitle && containsBaseUrl && containsLanguage)
            .Label($"Title={testData.Title}, BaseUrl={testData.BaseUrl}, Language={testData.Language}")
            .Classify(testData.Language.StartsWith("zh"), "中文站点")
            .Classify(testData.Language.StartsWith("en"), "英文站点")
            .Classify(!testData.Language.StartsWith("zh") && !testData.Language.StartsWith("en"), "其他语言站点");
    }

    /// <summary>
    /// Property 13: 模板渲染正确性 - Partial 模板包含
    /// 对于任意有效的 partial 内容，渲染结果应包含 partial 的内容
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplateTestArbitraries) })]
    public Property TemplateRendering_PartialInclude_ShouldIncludePartialContent(
        TemplatePartialTestData testData)
    {
        // Arrange
        CreatePartial("test-partial", testData.PartialContent);
        CreateTemplate("test-with-partial", """
            <header>{{ include 'partials/test-partial' }}</header>
            <main>{{ page.content }}</main>
            """);
        var page = CreatePageContext(
            title: "测试",
            content: "<p>主内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = _renderer.RenderAsync("test-with-partial", context).AsTask().Result;

        // Assert
        var containsPartialContent = result.Contains(testData.ExpectedText);

        return containsPartialContent
            .Label($"PartialContent='{testData.PartialContent}', ExpectedText='{testData.ExpectedText}'")
            .Classify(testData.PartialContent.Length <= 50, "短 partial")
            .Classify(testData.PartialContent.Length > 50, "长 partial");
    }

    /// <summary>
    /// Property 13: 模板渲染正确性 - 日期格式化函数
    /// 对于任意有效的日期，date_format 函数应正确格式化
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplateTestArbitraries) })]
    public Property TemplateRendering_DateFormat_ShouldFormatCorrectly(
        TemplateDateTestData testData)
    {
        // Arrange
        CreateTemplate("test-date", "{{ page.date | date_format 'yyyy-MM-dd' }}");
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: testData.Date,
            tags: [],
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = _renderer.RenderAsync("test-date", context).AsTask().Result;

        // Assert
        var expectedDate = testData.Date.ToString("yyyy-MM-dd");
        var containsFormattedDate = result.Trim() == expectedDate;

        return containsFormattedDate
            .Label($"Date={testData.Date}, Expected={expectedDate}, Actual={result.Trim()}")
            .Classify(testData.Date.Year < 2020, "历史日期")
            .Classify(testData.Date.Year >= 2020 && testData.Date.Year <= 2025, "近期日期")
            .Classify(testData.Date.Year > 2025, "未来日期");
    }

    /// <summary>
    /// Property 13: 模板渲染正确性 - 字符串函数
    /// 对于任意有效的字符串，upper/lower 函数应正确转换
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplateTestArbitraries) })]
    public Property TemplateRendering_StringFunctions_ShouldTransformCorrectly(
        TemplateStringFunctionTestData testData)
    {
        // Arrange
        // 使用唯一的模板名称避免缓存问题
        var templateName = $"test-string-{Guid.NewGuid():N}";
        CreateTemplate(templateName, $"{{{{ '{testData.Input}' | {testData.Function} }}}}");
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = _renderer.RenderAsync(templateName, context).AsTask().Result;

        // Assert
        var isCorrect = result.Trim() == testData.Expected;

        return isCorrect
            .Label($"Input='{testData.Input}', Function={testData.Function}, Expected='{testData.Expected}', Actual='{result.Trim()}'")
            .Classify(testData.Function == "upper", "大写转换")
            .Classify(testData.Function == "lower", "小写转换")
            .Classify(testData.Function == "trim", "去空格");
    }

    /// <summary>
    /// Property 13: 模板渲染正确性 - 条件渲染
    /// 对于任意有效的条件，if 语句应正确渲染
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplateTestArbitraries) })]
    public Property TemplateRendering_ConditionalRendering_ShouldRenderCorrectBranch(
        TemplateConditionalTestData testData)
    {
        // Arrange
        CreateTemplate("test-conditional", """
            {{ if page.draft }}草稿{{ else }}已发布{{ end }}
            """);
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: [],
            draft: testData.IsDraft);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = _renderer.RenderAsync("test-conditional", context).AsTask().Result;

        // Assert
        var expectedText = testData.IsDraft ? "草稿" : "已发布";
        var isCorrect = result.Trim() == expectedText;

        return isCorrect
            .Label($"IsDraft={testData.IsDraft}, Expected='{expectedText}', Actual='{result.Trim()}'")
            .Classify(testData.IsDraft, "草稿状态")
            .Classify(!testData.IsDraft, "已发布状态");
    }

    /// <summary>
    /// Property 13: 模板渲染正确性 - 循环渲染
    /// 对于任意有效的集合，for 循环应正确渲染所有元素
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplateTestArbitraries) })]
    public Property TemplateRendering_LoopRendering_ShouldRenderAllElements(
        TemplateLoopTestData testData)
    {
        // Arrange
        CreateTemplate("test-loop", """
            {{ for cat in page.categories }}[{{ cat }}]{{ end }}
            """);
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: testData.Categories);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = _renderer.RenderAsync("test-loop", context).AsTask().Result;

        // Assert
        var allCategoriesIncluded = testData.Categories.All(cat => result.Contains($"[{cat}]"));

        return allCategoriesIncluded
            .Label($"CategoryCount={testData.Categories.Count}, AllIncluded={allCategoriesIncluded}")
            .Classify(testData.Categories.Count == 0, "空集合")
            .Classify(testData.Categories.Count == 1, "单元素")
            .Classify(testData.Categories.Count > 1, "多元素");
    }

    #endregion

    #region Property 13: 模板渲染正确性 - 显式测试

    /// <summary>
    /// Property 13 的显式测试版本 - 页面标题渲染
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Theory]
    [InlineData("简单标题")]
    [InlineData("Hello World")]
    [InlineData("包含特殊字符的标题 <>&\"'")]
    [InlineData("很长很长很长很长很长很长很长很长很长很长的标题")]
    [InlineData("中英混合 Mixed Title 123")]
    public async Task TemplateRendering_PageTitle_ExplicitTest(string title)
    {
        // Arrange
        CreateTemplate("explicit-title", "<h1>{{ page.title }}</h1>");
        var page = CreatePageContext(
            title: title,
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = await _renderer.RenderAsync("explicit-title", context);

        // Assert
        result.Should().Contain(title);
    }

    /// <summary>
    /// Property 13 的显式测试版本 - 多标签渲染
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Fact]
    public async Task TemplateRendering_MultipleTags_ExplicitTest()
    {
        // Arrange
        var tags = new[] { "C#", "编程", "教程", ".NET", "性能优化" };
        CreateTemplate("explicit-tags", """
            {{ for tag in page.tags }}<span>{{ tag }}</span>{{ end }}
            """);
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: tags,
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = await _renderer.RenderAsync("explicit-tags", context);

        // Assert
        foreach (var tag in tags)
        {
            result.Should().Contain($"<span>{tag}</span>");
        }
    }

    /// <summary>
    /// Property 13 的显式测试版本 - Partial 嵌套
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Fact]
    public async Task TemplateRendering_NestedPartials_ExplicitTest()
    {
        // Arrange
        CreatePartial("inner", "<span class=\"inner\">内层内容</span>");
        CreatePartial("outer", """
            <div class="outer">
              {{ include 'partials/inner' }}
            </div>
            """);
        CreateTemplate("explicit-nested", """
            <main>
              {{ include 'partials/outer' }}
            </main>
            """);
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = await _renderer.RenderAsync("explicit-nested", context);

        // Assert
        result.Should().Contain("<main>");
        result.Should().Contain("<div class=\"outer\">");
        result.Should().Contain("<span class=\"inner\">内层内容</span>");
    }

    /// <summary>
    /// Property 13 的显式测试版本 - 内置函数组合
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Fact]
    public async Task TemplateRendering_BuiltinFunctionsCombination_ExplicitTest()
    {
        // Arrange
        // 使用 my_title 避免与只读变量 title 冲突
        CreateTemplate("explicit-functions", """
            {{ my_title = '  hello world  ' }}
            {{ my_title | trim | upper }}
            """);
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = await _renderer.RenderAsync("explicit-functions", context);

        // Assert
        result.Trim().Should().Be("HELLO WORLD");
    }

    /// <summary>
    /// Property 13 的显式测试版本 - 复杂条件渲染
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Theory]
    [InlineData(true, 0, "草稿-无权重")]
    [InlineData(true, 10, "草稿-有权重")]
    [InlineData(false, 0, "已发布-无权重")]
    [InlineData(false, 10, "已发布-有权重")]
    public async Task TemplateRendering_ComplexConditional_ExplicitTest(
        bool isDraft, int weight, string expected)
    {
        // Arrange
        CreateTemplate("explicit-conditional", """
            {{ if page.draft }}草稿{{ else }}已发布{{ end }}-{{ if page.weight > 0 }}有权重{{ else }}无权重{{ end }}
            """);
        var page = CreatePageContext(
            title: "测试",
            content: "<p>内容</p>",
            date: DateTimeOffset.Now,
            tags: [],
            categories: [],
            draft: isDraft,
            weight: weight);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN");
        var context = CreateTemplateContext(page, site);

        // Act
        var result = await _renderer.RenderAsync("explicit-conditional", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// Property 13 的显式测试版本 - 页面列表渲染
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 5.9**
    /// </summary>
    [Fact]
    public async Task TemplateRendering_PageList_ExplicitTest()
    {
        // Arrange
        CreateTemplate("explicit-list", """
            {{ for p in site.pages }}
            <article>
              <h2>{{ p.title }}</h2>
              <time>{{ p.date | date_format 'yyyy-MM-dd' }}</time>
            </article>
            {{ end }}
            """);
        var pages = new[]
        {
            CreatePageContext("文章一", "<p>内容一</p>", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), [], []),
            CreatePageContext("文章二", "<p>内容二</p>", new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), [], []),
            CreatePageContext("文章三", "<p>内容三</p>", new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), [], [])
        };
        var page = CreatePageContext("列表页", "<p>内容</p>", DateTimeOffset.Now, [], []);
        var site = CreateSiteContext("测试站点", "https://example.com", "zh-CN", pages);
        var context = CreateTemplateContext(page, site, isList: true, isSingle: false);

        // Act
        var result = await _renderer.RenderAsync("explicit-list", context);

        // Assert
        result.Should().Contain("<h2>文章一</h2>");
        result.Should().Contain("<h2>文章二</h2>");
        result.Should().Contain("<h2>文章三</h2>");
        result.Should().Contain("2024-01-01");
        result.Should().Contain("2024-02-01");
        result.Should().Contain("2024-03-01");
    }

    #endregion
}


#region 测试数据类型

/// <summary>
/// 模板页面标题测试数据
/// </summary>
public sealed class TemplatePageTitleTestData
{
    /// <summary>
    /// 页面标题
    /// </summary>
    public required string Title { get; init; }
}

/// <summary>
/// 模板页面内容测试数据
/// </summary>
public sealed class TemplatePageContentTestData
{
    /// <summary>
    /// 页面内容
    /// </summary>
    public required string Content { get; init; }
}

/// <summary>
/// 模板标签测试数据
/// </summary>
public sealed class TemplateTagsTestData
{
    /// <summary>
    /// 标签列表
    /// </summary>
    public required IReadOnlyList<string> Tags { get; init; }
}

/// <summary>
/// 模板站点信息测试数据
/// </summary>
public sealed class TemplateSiteInfoTestData
{
    /// <summary>
    /// 站点标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 基础 URL
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// 语言代码
    /// </summary>
    public required string Language { get; init; }
}

/// <summary>
/// 模板 Partial 测试数据
/// </summary>
public sealed class TemplatePartialTestData
{
    /// <summary>
    /// Partial 内容
    /// </summary>
    public required string PartialContent { get; init; }

    /// <summary>
    /// 预期在输出中的文本
    /// </summary>
    public required string ExpectedText { get; init; }
}

/// <summary>
/// 模板日期测试数据
/// </summary>
public sealed class TemplateDateTestData
{
    /// <summary>
    /// 日期
    /// </summary>
    public required DateTimeOffset Date { get; init; }
}

/// <summary>
/// 模板字符串函数测试数据
/// </summary>
public sealed class TemplateStringFunctionTestData
{
    /// <summary>
    /// 输入字符串
    /// </summary>
    public required string Input { get; init; }

    /// <summary>
    /// 函数名称
    /// </summary>
    public required string Function { get; init; }

    /// <summary>
    /// 预期输出
    /// </summary>
    public required string Expected { get; init; }
}

/// <summary>
/// 模板条件测试数据
/// </summary>
public sealed class TemplateConditionalTestData
{
    /// <summary>
    /// 是否为草稿
    /// </summary>
    public required bool IsDraft { get; init; }
}

/// <summary>
/// 模板循环测试数据
/// </summary>
public sealed class TemplateLoopTestData
{
    /// <summary>
    /// 分类列表
    /// </summary>
    public required IReadOnlyList<string> Categories { get; init; }
}

#endregion

#region FsCheck 生成器

/// <summary>
/// 模板测试数据生成器
/// </summary>
public static class TemplateTestArbitraries
{
    /// <summary>
    /// 页面标题测试数据生成器
    /// </summary>
    public static Arbitrary<TemplatePageTitleTestData> TemplatePageTitleTestDataArb() =>
        (from title in Gen.Elements(
            "简单标题",
            "Hello World",
            "中英混合 Mixed Title",
            "包含数字 123",
            "很长很长很长很长很长很长很长很长的标题内容",
            "技术博客文章",
            "C# 编程教程",
            ".NET 性能优化指南",
            "前端开发最佳实践",
            "系统架构设计")
         select new TemplatePageTitleTestData { Title = title }).ToArbitrary();

    /// <summary>
    /// 页面内容测试数据生成器
    /// </summary>
    public static Arbitrary<TemplatePageContentTestData> TemplatePageContentTestDataArb() =>
        (from content in Gen.Elements(
            "<p>简单内容</p>",
            "<p>这是一段较长的内容，包含了很多文字。</p>",
            "<h2>标题</h2><p>段落内容</p>",
            "<ul><li>列表项一</li><li>列表项二</li></ul>",
            "<p>包含<strong>粗体</strong>和<em>斜体</em>的内容</p>",
            "<pre><code>代码块内容</code></pre>",
            "<blockquote>引用内容</blockquote>",
            "<p>中文内容测试</p>",
            "<p>English content test</p>",
            "<p>混合内容 Mixed Content 123</p>")
         select new TemplatePageContentTestData { Content = content }).ToArbitrary();

    /// <summary>
    /// 标签测试数据生成器
    /// </summary>
    public static Arbitrary<TemplateTagsTestData> TemplateTagsTestDataArb() =>
        (from tagCount in Gen.Choose(0, 5)
         from tags in Gen.ListOf<string>(Gen.Elements(
             "C#", "编程", "教程", ".NET", "性能",
             "前端", "后端", "全栈", "架构", "设计",
             "测试", "部署", "运维", "安全", "优化")).Select(t => t.Take(tagCount).ToList())
         select new TemplateTagsTestData { Tags = tags.Distinct().ToList() }).ToArbitrary();

    /// <summary>
    /// 站点信息测试数据生成器
    /// </summary>
    public static Arbitrary<TemplateSiteInfoTestData> TemplateSiteInfoTestDataArb() =>
        (from title in Gen.Elements("我的博客", "技术站点", "开发者社区", "My Blog", "Tech Site")
         from baseUrl in Gen.Elements(
             "https://example.com",
             "https://myblog.com",
             "https://tech.example.org",
             "https://dev.example.net")
         from language in Gen.Elements("zh-CN", "zh-TW", "en", "en-US", "ja", "ko")
         select new TemplateSiteInfoTestData
         {
             Title = title,
             BaseUrl = baseUrl,
             Language = language
         }).ToArbitrary();

    /// <summary>
    /// Partial 测试数据生成器
    /// </summary>
    public static Arbitrary<TemplatePartialTestData> TemplatePartialTestDataArb() =>
        (from content in Gen.Elements(
            ("<header>头部内容</header>", "头部内容"),
            ("<footer>底部内容</footer>", "底部内容"),
            ("<nav>导航内容</nav>", "导航内容"),
            ("<aside>侧边栏</aside>", "侧边栏"),
            ("<div class=\"widget\">小部件</div>", "小部件"))
         select new TemplatePartialTestData
         {
             PartialContent = content.Item1,
             ExpectedText = content.Item2
         }).ToArbitrary();

    /// <summary>
    /// 日期测试数据生成器
    /// </summary>
    public static Arbitrary<TemplateDateTestData> TemplateDateTestDataArb() =>
        (from year in Gen.Choose(2020, 2030)
         from month in Gen.Choose(1, 12)
         from day in Gen.Choose(1, 28)
         select new TemplateDateTestData
         {
             Date = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero)
         }).ToArbitrary();

    /// <summary>
    /// 字符串函数测试数据生成器
    /// </summary>
    public static Arbitrary<TemplateStringFunctionTestData> TemplateStringFunctionTestDataArb() =>
        Gen.OneOf(
            // upper 函数测试
            Gen.Elements("hello", "world", "test", "abc")
                .Select(s => new TemplateStringFunctionTestData
                {
                    Input = s,
                    Function = "upper",
                    Expected = s.ToUpperInvariant()
                }),
            // lower 函数测试
            Gen.Elements("HELLO", "WORLD", "TEST", "ABC")
                .Select(s => new TemplateStringFunctionTestData
                {
                    Input = s,
                    Function = "lower",
                    Expected = s.ToLowerInvariant()
                }),
            // trim 函数测试
            Gen.Elements("  hello  ", "  world  ", "  test  ")
                .Select(s => new TemplateStringFunctionTestData
                {
                    Input = s,
                    Function = "trim",
                    Expected = s.Trim()
                })
        ).ToArbitrary();

    /// <summary>
    /// 条件测试数据生成器
    /// </summary>
    public static Arbitrary<TemplateConditionalTestData> TemplateConditionalTestDataArb() =>
        ArbMap.Default.GeneratorFor<bool>()
            .Select(b => new TemplateConditionalTestData { IsDraft = b })
            .ToArbitrary();

    /// <summary>
    /// 循环测试数据生成器
    /// </summary>
    public static Arbitrary<TemplateLoopTestData> TemplateLoopTestDataArb() =>
        (from catCount in Gen.Choose(0, 5)
         from categories in Gen.ListOf<string>(Gen.Elements(
             "技术", "生活", "旅行", "美食", "摄影",
             "编程", "设计", "产品", "运营", "管理")).Select(c => c.Take(catCount).ToList())
         select new TemplateLoopTestData { Categories = categories.Distinct().ToList() }).ToArbitrary();
}

#endregion

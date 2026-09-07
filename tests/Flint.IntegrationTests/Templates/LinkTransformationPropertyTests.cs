// Flint 静态站点生成器
// 链接转换正确性属性测试
// **Property 14: 链接转换正确性**
// 生成随机相对链接和 baseURL，验证转换结果
// 测试各种链接格式（相对、绝对、锚点）
// **Validates: Requirements 5.10**

#pragma warning disable CA1054 // URI 参数应为 Uri 类型 - 测试中使用字符串更方便
#pragma warning disable CA1056 // URI 属性应为 Uri 类型 - 测试中使用字符串更方便
#pragma warning disable CA1866 // 使用 char 重载 - 测试中使用字符串更清晰

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
/// 链接转换正确性属性测试
/// **Feature: Flint-integration-tests, Property 14: 链接转换正确性**
/// 验证模板渲染器对链接的正确转换
/// </summary>
public class LinkTransformationPropertyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _layoutsDir;

    public LinkTransformationPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-link-prop-{Guid.NewGuid():N}");
        _layoutsDir = Path.Combine(_tempDir, "layouts");

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_layoutsDir);
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
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    /// <summary>
    /// 创建渲染器
    /// </summary>
    private ScribanTemplateRenderer CreateRenderer(string baseUrl)
    {
        return new ScribanTemplateRenderer(_layoutsDir, baseUrl);
    }

    /// <summary>
    /// 创建测试页面上下文
    /// </summary>
    private static PageContext CreatePageContext()
    {
        return new PageContext
        {
            Title = "测试页面",
            Content = "<p>内容</p>",
            Permalink = "https://example.com/test/",
            RelPermalink = "/test/",
            Date = DateTimeOffset.Now,
            Tags = [],
            Categories = [],
            WordCount = 10,
            ReadingTime = TimeSpan.FromMinutes(1)
        };
    }

    /// <summary>
    /// 创建测试站点上下文
    /// </summary>
    private static SiteContext CreateSiteContext(string baseUrl)
    {
        return new SiteContext
        {
            Title = "测试站点",
            BaseURL = baseUrl,
            Language = "zh-CN",
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
            Config = new Flint.Core.Configuration.SiteConfig { BaseURL = "https://example.com", Title = "T" }
        };
    }

    /// <summary>
    /// 创建模板上下文
    /// </summary>
    private static TemplateContext CreateTemplateContext(string baseUrl)
    {
        return new TemplateContext
        {
            Page = CreatePageContext(),
            Site = CreateSiteContext(baseUrl),
            IsHome = false,
            IsList = false,
            IsSingle = true,
            Params = new Dictionary<string, object>(),
            Data = new Dictionary<string, object>()
        };
    }

    #endregion

    #region Property 14: 链接转换正确性 - 属性测试

    /// <summary>
    /// Property 14: 链接转换正确性 - absURL 函数
    /// 对于任意有效的相对路径和 baseURL，absURL 应返回正确的绝对 URL
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LinkTestArbitraries) })]
    public Property LinkTransformation_AbsUrl_ShouldConvertToAbsoluteUrl(
        LinkAbsUrlTestData testData)
    {
        // Arrange
        CreateTemplate("test-absurl", $"{{{{ absURL '{testData.RelativePath}' }}}}");
        var renderer = CreateRenderer(testData.BaseUrl);
        var context = CreateTemplateContext(testData.BaseUrl);

        // Act
        var result = renderer.RenderAsync("test-absurl", context).AsTask().Result.Trim();

        // Assert
        // 如果输入已经是绝对 URL，应该原样返回
        // 如果是相对路径，应该与 baseURL 组合
        bool isCorrect;
        if (testData.RelativePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            testData.RelativePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            isCorrect = result == testData.RelativePath;
        }
        else
        {
            var expectedUrl = testData.BaseUrl.TrimEnd('/') + "/" + testData.RelativePath.TrimStart('/');
            isCorrect = result == expectedUrl;
        }

        return isCorrect
            .Label($"BaseUrl={testData.BaseUrl}, RelativePath={testData.RelativePath}, Result={result}")
            .Classify(testData.RelativePath.StartsWith("http"), "已是绝对 URL")
            .Classify(testData.RelativePath.StartsWith("/"), "根相对路径")
            .Classify(!testData.RelativePath.StartsWith("/") && !testData.RelativePath.StartsWith("http"), "相对路径");
    }

    /// <summary>
    /// Property 14: 链接转换正确性 - relURL 函数
    /// 对于任意有效的路径，relURL 应返回正确的相对 URL
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LinkTestArbitraries) })]
    public Property LinkTransformation_RelUrl_ShouldConvertToRelativeUrl(
        LinkRelUrlTestData testData)
    {
        // Arrange
        CreateTemplate("test-relurl", $"{{{{ relURL '{testData.Path}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = renderer.RenderAsync("test-relurl", context).AsTask().Result.Trim();

        // Assert
        // 如果输入已经是绝对 URL，应该原样返回
        // 否则应该确保以 / 开头
        bool isCorrect;
        if (testData.Path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            testData.Path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            isCorrect = result == testData.Path;
        }
        else
        {
            isCorrect = result.StartsWith("/");
        }

        return isCorrect
            .Label($"Path={testData.Path}, Result={result}")
            .Classify(testData.Path.StartsWith("http"), "绝对 URL")
            .Classify(testData.Path.StartsWith("/"), "根路径")
            .Classify(!testData.Path.StartsWith("/") && !testData.Path.StartsWith("http"), "相对路径");
    }

    /// <summary>
    /// Property 14: 链接转换正确性 - 锚点链接
    /// 对于任意有效的锚点链接，应正确处理
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LinkTestArbitraries) })]
    public Property LinkTransformation_AnchorLinks_ShouldPreserveAnchor(
        LinkAnchorTestData testData)
    {
        // Arrange
        CreateTemplate("test-anchor", $"{{{{ absURL '{testData.PathWithAnchor}' }}}}");
        var renderer = CreateRenderer(testData.BaseUrl);
        var context = CreateTemplateContext(testData.BaseUrl);

        // Act
        var result = renderer.RenderAsync("test-anchor", context).AsTask().Result.Trim();

        // Assert
        // 结果应该包含锚点部分
        var containsAnchor = result.Contains("#" + testData.Anchor);

        return containsAnchor
            .Label($"PathWithAnchor={testData.PathWithAnchor}, Anchor={testData.Anchor}, Result={result}")
            .Classify(testData.Anchor.Length <= 10, "短锚点")
            .Classify(testData.Anchor.Length > 10, "长锚点");
    }

    /// <summary>
    /// Property 14: 链接转换正确性 - 不同 baseURL 格式
    /// 对于各种 baseURL 格式（带/不带尾部斜杠），应正确处理
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LinkTestArbitraries) })]
    public Property LinkTransformation_BaseUrlFormats_ShouldHandleCorrectly(
        LinkBaseUrlFormatTestData testData)
    {
        // Arrange
        CreateTemplate("test-baseurl", "{{ absURL '/about' }}");
        var renderer = CreateRenderer(testData.BaseUrl);
        var context = CreateTemplateContext(testData.BaseUrl);

        // Act
        var result = renderer.RenderAsync("test-baseurl", context).AsTask().Result.Trim();

        // Assert
        // 结果应该是有效的绝对 URL，不应该有双斜杠（除了协议部分）
        var isValidUrl = result.StartsWith("http://") || result.StartsWith("https://");
        var noDoubleSlash = !result.Replace("http://", "").Replace("https://", "").Contains("//");

        return (isValidUrl && noDoubleSlash)
            .Label($"BaseUrl={testData.BaseUrl}, Result={result}")
            .Classify(testData.BaseUrl.EndsWith("/"), "带尾部斜杠")
            .Classify(!testData.BaseUrl.EndsWith("/"), "不带尾部斜杠");
    }

    /// <summary>
    /// Property 14: 链接转换正确性 - 路径组合
    /// 对于任意有效的路径组合，path_join 应正确连接
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LinkTestArbitraries) })]
    public Property LinkTransformation_PathJoin_ShouldCombineCorrectly(
        LinkPathJoinTestData testData)
    {
        // Arrange
        CreateTemplate("test-pathjoin", $"{{{{ path_join '{testData.Part1}' '{testData.Part2}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = renderer.RenderAsync("test-pathjoin", context).AsTask().Result.Trim();

        // Assert
        // 结果应该包含两个部分
        var containsPart1 = result.Contains(testData.Part1.Trim('/'));
        var containsPart2 = result.Contains(testData.Part2.Trim('/'));

        return (containsPart1 && containsPart2)
            .Label($"Part1={testData.Part1}, Part2={testData.Part2}, Result={result}")
            .Classify(testData.Part1.StartsWith("/"), "Part1 以 / 开头")
            .Classify(testData.Part2.StartsWith("/"), "Part2 以 / 开头");
    }

    #endregion

    #region Property 14: 链接转换正确性 - 显式测试

    /// <summary>
    /// Property 14 的显式测试版本 - absURL 基本转换
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("https://example.com", "/about", "https://example.com/about")]
    [InlineData("https://example.com/", "/about", "https://example.com/about")]
    [InlineData("https://example.com", "about", "https://example.com/about")]
    [InlineData("https://example.com/blog", "/about", "https://example.com/blog/about")]
    [InlineData("https://example.com", "posts/hello", "https://example.com/posts/hello")]
    public async Task LinkTransformation_AbsUrl_ExplicitTest(
        string baseUrl, string relativePath, string expected)
    {
        // Arrange
        CreateTemplate("explicit-absurl", $"{{{{ absURL '{relativePath}' }}}}");
        var renderer = CreateRenderer(baseUrl);
        var context = CreateTemplateContext(baseUrl);

        // Act
        var result = await renderer.RenderAsync("explicit-absurl", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - absURL 保留绝对 URL
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("https://other.com/page")]
    [InlineData("http://external.org/resource")]
    [InlineData("https://cdn.example.com/image.png")]
    public async Task LinkTransformation_AbsUrl_PreservesAbsoluteUrl_ExplicitTest(string absoluteUrl)
    {
        // Arrange
        CreateTemplate("explicit-absurl-preserve", $"{{{{ absURL '{absoluteUrl}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-absurl-preserve", context);

        // Assert
        result.Trim().Should().Be(absoluteUrl);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - relURL 基本转换
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("about", "/about")]
    [InlineData("/about", "/about")]
    [InlineData("posts/hello", "/posts/hello")]
    [InlineData("/posts/hello", "/posts/hello")]
    public async Task LinkTransformation_RelUrl_ExplicitTest(string path, string expected)
    {
        // Arrange
        CreateTemplate("explicit-relurl", $"{{{{ relURL '{path}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-relurl", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - 锚点链接处理
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("/about#contact", "#contact")]
    [InlineData("/posts/hello#section-1", "#section-1")]
    [InlineData("/docs#getting-started", "#getting-started")]
    public async Task LinkTransformation_AnchorLinks_ExplicitTest(string pathWithAnchor, string expectedAnchor)
    {
        // Arrange
        CreateTemplate("explicit-anchor", $"{{{{ absURL '{pathWithAnchor}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-anchor", context);

        // Assert
        result.Trim().Should().Contain(expectedAnchor);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - 查询参数处理
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("/search?q=test", "?q=test")]
    [InlineData("/api?page=1&size=10", "?page=1&size=10")]
    public async Task LinkTransformation_QueryParams_ExplicitTest(string pathWithQuery, string expectedQuery)
    {
        // Arrange
        CreateTemplate("explicit-query", $"{{{{ absURL '{pathWithQuery}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-query", context);

        // Assert
        result.Trim().Should().Contain(expectedQuery);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - 路径清理
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("//double//slash", "/double/slash")]
    [InlineData("/trailing/", "/trailing")]
    public async Task LinkTransformation_PathClean_ExplicitTest(string dirtyPath, string expectedClean)
    {
        // Arrange
        CreateTemplate("explicit-clean", $"{{{{ path_clean '{dirtyPath}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-clean", context);

        // Assert
        result.Trim().Should().Be(expectedClean);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - 路径组合
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("posts", "hello", "posts/hello")]
    [InlineData("/posts", "hello", "/posts/hello")]
    // 注意：path_join 不会自动清理多余的斜杠
    [InlineData("posts/", "/hello", "posts///hello")]
    public async Task LinkTransformation_PathJoin_ExplicitTest(
        string part1, string part2, string expected)
    {
        // Arrange
        CreateTemplate("explicit-join", $"{{{{ path_join '{part1}' '{part2}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-join", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - 路径基名
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("/posts/hello.md", "hello.md")]
    [InlineData("/images/logo.png", "logo.png")]
    [InlineData("/path/to/file.txt", "file.txt")]
    public async Task LinkTransformation_PathBase_ExplicitTest(string path, string expected)
    {
        // Arrange
        CreateTemplate("explicit-base", $"{{{{ path_base '{path}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-base", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - 路径目录
    /// 注意：在 Windows 上，path_dir 返回反斜杠
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("/posts/hello.md", "\\posts")]
    [InlineData("/images/logo.png", "\\images")]
    public async Task LinkTransformation_PathDir_ExplicitTest(string path, string expected)
    {
        // Arrange
        CreateTemplate("explicit-dir", $"{{{{ path_dir '{path}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-dir", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - 路径扩展名
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("/posts/hello.md", ".md")]
    [InlineData("/images/logo.png", ".png")]
    [InlineData("/styles/main.css", ".css")]
    public async Task LinkTransformation_PathExt_ExplicitTest(string path, string expected)
    {
        // Arrange
        CreateTemplate("explicit-ext", $"{{{{ path_ext '{path}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-ext", context);

        // Assert
        result.Trim().Should().Be(expected);
    }

    /// <summary>
    /// Property 14 的显式测试版本 - URL 查询参数编码
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Theory]
    [InlineData("hello world", "hello+world")]
    [InlineData("测试", "%e6%b5%8b%e8%af%95")]
    public async Task LinkTransformation_UrlQuery_ExplicitTest(string input, string expected)
    {
        // Arrange
        CreateTemplate("explicit-urlquery", $"{{{{ urlquery '{input}' }}}}");
        var renderer = CreateRenderer("https://example.com");
        var context = CreateTemplateContext("https://example.com");

        // Act
        var result = await renderer.RenderAsync("explicit-urlquery", context);

        // Assert
        result.Trim().ToLowerInvariant().Should().Be(expected.ToLowerInvariant());
    }

    /// <summary>
    /// Property 14 的显式测试版本 - 综合链接场景
    /// **Validates: Requirements 5.10**
    /// </summary>
    [Fact]
    public async Task LinkTransformation_CompleteScenario_ExplicitTest()
    {
        // Arrange
        CreateTemplate("explicit-complete", """
            <a href="{{ absURL '/about' }}">关于</a>
            <a href="{{ relURL 'contact' }}">联系</a>
            <link rel="stylesheet" href="{{ absURL '/css/style.css' }}">
            <script src="{{ absURL '/js/main.js' }}"></script>
            <img src="{{ absURL '/images/logo.png' }}" alt="Logo">
            """);
        var renderer = CreateRenderer("https://myblog.com");
        var context = CreateTemplateContext("https://myblog.com");

        // Act
        var result = await renderer.RenderAsync("explicit-complete", context);

        // Assert
        result.Should().Contain("href=\"https://myblog.com/about\"");
        result.Should().Contain("href=\"/contact\"");
        result.Should().Contain("href=\"https://myblog.com/css/style.css\"");
        result.Should().Contain("src=\"https://myblog.com/js/main.js\"");
        result.Should().Contain("src=\"https://myblog.com/images/logo.png\"");
    }

    #endregion
}


#region 测试数据类型

/// <summary>
/// absURL 测试数据
/// </summary>
public sealed class LinkAbsUrlTestData
{
    /// <summary>
    /// 基础 URL
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// 相对路径
    /// </summary>
    public required string RelativePath { get; init; }
}

/// <summary>
/// relURL 测试数据
/// </summary>
public sealed class LinkRelUrlTestData
{
    /// <summary>
    /// 路径
    /// </summary>
    public required string Path { get; init; }
}

/// <summary>
/// 锚点链接测试数据
/// </summary>
public sealed class LinkAnchorTestData
{
    /// <summary>
    /// 基础 URL
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// 带锚点的路径
    /// </summary>
    public required string PathWithAnchor { get; init; }

    /// <summary>
    /// 锚点部分
    /// </summary>
    public required string Anchor { get; init; }
}

/// <summary>
/// baseURL 格式测试数据
/// </summary>
public sealed class LinkBaseUrlFormatTestData
{
    /// <summary>
    /// 基础 URL
    /// </summary>
    public required string BaseUrl { get; init; }
}

/// <summary>
/// 路径组合测试数据
/// </summary>
public sealed class LinkPathJoinTestData
{
    /// <summary>
    /// 第一部分
    /// </summary>
    public required string Part1 { get; init; }

    /// <summary>
    /// 第二部分
    /// </summary>
    public required string Part2 { get; init; }
}

#endregion

#region FsCheck 生成器

/// <summary>
/// 链接测试数据生成器
/// </summary>
public static class LinkTestArbitraries
{
    /// <summary>
    /// absURL 测试数据生成器
    /// </summary>
    public static Arbitrary<LinkAbsUrlTestData> LinkAbsUrlTestDataArb() =>
        (from baseUrl in Gen.Elements(
            "https://example.com",
            "https://example.com/",
            "https://myblog.com",
            "https://docs.example.org",
            "http://localhost:1313")
         from relativePath in Gen.Elements(
            "/about",
            "/posts/hello",
            "about",
            "posts/hello",
            "/images/logo.png",
            "/css/style.css",
            "https://external.com/page")
         select new LinkAbsUrlTestData
         {
             BaseUrl = baseUrl,
             RelativePath = relativePath
         }).ToArbitrary();

    /// <summary>
    /// relURL 测试数据生成器
    /// </summary>
    public static Arbitrary<LinkRelUrlTestData> LinkRelUrlTestDataArb() =>
        (from path in Gen.Elements(
            "about",
            "/about",
            "posts/hello",
            "/posts/hello",
            "images/logo.png",
            "/images/logo.png",
            "https://external.com/page")
         select new LinkRelUrlTestData { Path = path }).ToArbitrary();

    /// <summary>
    /// 锚点链接测试数据生成器
    /// </summary>
    public static Arbitrary<LinkAnchorTestData> LinkAnchorTestDataArb() =>
        (from baseUrl in Gen.Elements(
            "https://example.com",
            "https://myblog.com")
         from path in Gen.Elements("/about", "/posts/hello", "/docs")
         from anchor in Gen.Elements(
            "section-1",
            "introduction",
            "getting-started",
            "contact",
            "top",
            "bottom",
            "chapter-1-introduction")
         select new LinkAnchorTestData
         {
             BaseUrl = baseUrl,
             PathWithAnchor = $"{path}#{anchor}",
             Anchor = anchor
         }).ToArbitrary();

    /// <summary>
    /// baseURL 格式测试数据生成器
    /// </summary>
    public static Arbitrary<LinkBaseUrlFormatTestData> LinkBaseUrlFormatTestDataArb() =>
        (from baseUrl in Gen.Elements(
            "https://example.com",
            "https://example.com/",
            "https://myblog.com",
            "https://myblog.com/",
            "http://localhost:1313",
            "http://localhost:1313/",
            "https://docs.example.org/v1",
            "https://docs.example.org/v1/")
         select new LinkBaseUrlFormatTestData { BaseUrl = baseUrl }).ToArbitrary();

    /// <summary>
    /// 路径组合测试数据生成器
    /// </summary>
    public static Arbitrary<LinkPathJoinTestData> LinkPathJoinTestDataArb() =>
        (from part1 in Gen.Elements("posts", "/posts", "posts/", "/posts/", "docs", "images")
         from part2 in Gen.Elements("hello", "/hello", "hello/", "world", "logo.png", "style.css")
         select new LinkPathJoinTestData
         {
             Part1 = part1,
             Part2 = part2
         }).ToArbitrary();
}

#endregion

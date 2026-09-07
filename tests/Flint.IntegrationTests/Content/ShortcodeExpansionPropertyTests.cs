// Flint 静态站点生成器
// 短代码展开正确性属性测试
// **Property 4: 短代码展开正确性**
// 生成包含随机短代码的内容，验证展开结果
// 验证输出不包含未处理的短代码标记
// **Validates: Requirements 2.3**

using System.Text;
using System.Text.RegularExpressions;
using Flint.Core.Content.Shortcodes;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.Content;

/// <summary>
/// 短代码展开正确性属性测试
/// **Feature: Flint-integration-tests, Property 4: 短代码展开正确性**
/// 验证所有已注册的短代码都被正确展开，输出不包含未处理的短代码标记
/// </summary>
public partial class ShortcodeExpansionPropertyTests : IDisposable
{
    private readonly ShortcodeProcessor _processor;
    private readonly ShortcodeRegistry _registry;
    private readonly string _tempDir;

    public ShortcodeExpansionPropertyTests()
    {
        _registry = new ShortcodeRegistry(registerBuiltins: true);
        _processor = new ShortcodeProcessor(_registry);
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-shortcode-prop-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
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
    /// 检查内容是否包含未处理的短代码标记
    /// </summary>
    private static bool ContainsUnprocessedShortcodeMarkers(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        // 检查是否包含 {{< 或 {{%（短代码开始标记）
        // 但排除占位符 {{SHORTCODE: 和 {{NESTED:
        var hasOpenMarker = UnprocessedShortcodePattern().IsMatch(content);

        return hasOpenMarker;
    }

#pragma warning disable CA1054 // URI 参数应为 Uri 类型 - 测试中使用字符串更方便

    /// <summary>
    /// 匹配未处理的短代码标记的正则表达式
    /// 匹配 {{< 或 {{% 开头的短代码，但排除注释形式的错误消息
    /// </summary>
    [GeneratedRegex(@"\{\{[<%]\s*(?!--)", RegexOptions.Compiled)]
    private static partial Regex UnprocessedShortcodePattern();

    /// <summary>
    /// 检查输出是否包含预期的 HTML 结构
    /// </summary>
    private static bool ContainsExpectedHtmlStructure(string output, ShortcodeTestData testData)
    {
        // 根据短代码类型检查预期的 HTML 结构
        return testData.ShortcodeType switch
        {
            ShortcodeType.Figure => output.Contains("<figure", StringComparison.Ordinal) || output.Contains("<!-- figure:", StringComparison.Ordinal),
            ShortcodeType.Highlight => output.Contains("<div class=\"highlight\"", StringComparison.Ordinal) || output.Contains("<!-- highlight:", StringComparison.Ordinal),
            ShortcodeType.Youtube => output.Contains("youtube-container", StringComparison.Ordinal) || output.Contains("<!-- youtube:", StringComparison.Ordinal),
            ShortcodeType.Vimeo => output.Contains("vimeo-container", StringComparison.Ordinal) || output.Contains("<!-- vimeo:", StringComparison.Ordinal),
            ShortcodeType.Gist => output.Contains("gist.github.com", StringComparison.Ordinal) || output.Contains("<!-- gist:", StringComparison.Ordinal),
            ShortcodeType.Ref => output.Contains('/') || output.Contains("<!-- ref:", StringComparison.Ordinal),
            ShortcodeType.Relref => !output.StartsWith('/') || output.Contains("<!-- relref:", StringComparison.Ordinal),
            ShortcodeType.Tweet => output.Contains("twitter-tweet", StringComparison.Ordinal) || output.Contains("<!-- tweet:", StringComparison.Ordinal),
            ShortcodeType.Instagram => output.Contains("instagram-container", StringComparison.Ordinal) || output.Contains("<!-- instagram:", StringComparison.Ordinal),
            ShortcodeType.Param => output.Length == 0 || output.Contains("<!-- param:", StringComparison.Ordinal),
            _ => true
        };
    }

#pragma warning restore CA1054

    #endregion

    #region Property 4: 短代码展开正确性 - 属性测试

    /// <summary>
    /// Property 4: 短代码展开正确性 - 单个短代码
    /// 对于任意有效的短代码，处理后的输出不应包含未处理的短代码标记
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ShortcodeArbitraries) })]
    public Property ShortcodeExpansion_SingleShortcode_ShouldNotContainUnprocessedMarkers(
        ShortcodeTestData testData)
    {
        // Arrange
        var content = testData.GenerateShortcode();

        // Act
        var result = _processor.Process(content);

        // Assert
        // 验证输出不包含未处理的短代码标记
        var noUnprocessedMarkers = !ContainsUnprocessedShortcodeMarkers(result);
        // 验证输出包含预期的 HTML 结构或错误注释
        var hasExpectedStructure = ContainsExpectedHtmlStructure(result, testData);

        return (noUnprocessedMarkers && hasExpectedStructure)
            .Label($"Type={testData.ShortcodeType}, NoUnprocessedMarkers={noUnprocessedMarkers}, HasExpectedStructure={hasExpectedStructure}")
            .Classify(testData.ShortcodeType == ShortcodeType.Figure, "Figure 短代码")
            .Classify(testData.ShortcodeType == ShortcodeType.Highlight, "Highlight 短代码")
            .Classify(testData.ShortcodeType == ShortcodeType.Youtube, "YouTube 短代码")
            .Classify(testData.ShortcodeType == ShortcodeType.Vimeo, "Vimeo 短代码")
            .Classify(testData.ShortcodeType == ShortcodeType.Gist, "Gist 短代码")
            .Classify(testData.ShortcodeType == ShortcodeType.Ref, "Ref 短代码")
            .Classify(testData.ShortcodeType == ShortcodeType.Relref, "Relref 短代码")
            .Classify(testData.ShortcodeType == ShortcodeType.Tweet, "Tweet 短代码")
            .Classify(testData.ShortcodeType == ShortcodeType.Instagram, "Instagram 短代码")
            .Classify(testData.ShortcodeType == ShortcodeType.Param, "Param 短代码");
    }

    /// <summary>
    /// Property 4: 短代码展开正确性 - 多个短代码
    /// 对于包含多个短代码的内容，处理后的输出不应包含未处理的短代码标记
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ShortcodeArbitraries) })]
    public Property ShortcodeExpansion_MultipleShortcodes_ShouldNotContainUnprocessedMarkers(
        MultipleShortcodesTestData testData)
    {
        // Arrange
        var content = testData.GenerateContent();

        // Act
        var result = _processor.Process(content);

        // Assert
        // 验证输出不包含未处理的短代码标记
        var noUnprocessedMarkers = !ContainsUnprocessedShortcodeMarkers(result);

        return noUnprocessedMarkers
            .Label($"ShortcodeCount={testData.Shortcodes.Count}, NoUnprocessedMarkers={noUnprocessedMarkers}")
            .Classify(testData.Shortcodes.Count == 1, "单个短代码")
            .Classify(testData.Shortcodes.Count == 2, "两个短代码")
            .Classify(testData.Shortcodes.Count >= 3, "三个或更多短代码");
    }

    /// <summary>
    /// Property 4: 短代码展开正确性 - 带内容的短代码
    /// 对于带内容的短代码（如 highlight），处理后的输出应包含内容且不包含未处理的标记
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ShortcodeArbitraries) })]
    public Property ShortcodeExpansion_PairedShortcode_ShouldExpandCorrectly(
        PairedShortcodeTestData testData)
    {
        // Arrange
        var content = testData.GenerateShortcode();

        // Act
        var result = _processor.Process(content);

        // Assert
        // 验证输出不包含未处理的短代码标记
        var noUnprocessedMarkers = !ContainsUnprocessedShortcodeMarkers(result);
        // 验证输出包含预期的 HTML 结构
        var hasHighlightStructure = result.Contains("<div class=\"highlight\"") ||
                                    result.Contains("<!-- highlight:");

        return (noUnprocessedMarkers && hasHighlightStructure)
            .Label($"Language={testData.Language}, NoUnprocessedMarkers={noUnprocessedMarkers}, HasHighlightStructure={hasHighlightStructure}")
            .Classify(testData.Language == "csharp", "C# 代码")
            .Classify(testData.Language == "javascript", "JavaScript 代码")
            .Classify(testData.Language == "python", "Python 代码")
            .Classify(testData.Language == "go", "Go 代码");
    }

    /// <summary>
    /// Property 4: 短代码展开正确性 - 混合内容
    /// 对于包含短代码和普通文本的混合内容，处理后应保留普通文本且展开短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ShortcodeArbitraries) })]
    public Property ShortcodeExpansion_MixedContent_ShouldPreserveTextAndExpandShortcodes(
        MixedContentTestData testData)
    {
        // Arrange
        var content = testData.GenerateContent();

        // Act
        var result = _processor.Process(content);

        // Assert
        // 验证输出不包含未处理的短代码标记
        var noUnprocessedMarkers = !ContainsUnprocessedShortcodeMarkers(result);
        // 验证普通文本被保留
        var textPreserved = testData.TextParts.All(text => result.Contains(text));

        return (noUnprocessedMarkers && textPreserved)
            .Label($"TextPartsCount={testData.TextParts.Count}, NoUnprocessedMarkers={noUnprocessedMarkers}, TextPreserved={textPreserved}")
            .Classify(testData.TextParts.Count == 1, "单段文本")
            .Classify(testData.TextParts.Count >= 2, "多段文本");
    }

    /// <summary>
    /// Property 4: 短代码展开正确性 - 使用 % 分隔符
    /// 对于使用 % 分隔符的短代码，处理后的输出不应包含未处理的短代码标记
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ShortcodeArbitraries) })]
    public Property ShortcodeExpansion_PercentDelimiter_ShouldNotContainUnprocessedMarkers(
        PercentDelimiterTestData testData)
    {
        // Arrange
        var content = testData.GenerateShortcode();

        // Act
        var result = _processor.Process(content);

        // Assert
        // 验证输出不包含未处理的短代码标记
        var noUnprocessedMarkers = !ContainsUnprocessedShortcodeMarkers(result);

        return noUnprocessedMarkers
            .Label($"ShortcodeType={testData.ShortcodeType}, NoUnprocessedMarkers={noUnprocessedMarkers}")
            .Classify(testData.IsPaired, "配对短代码")
            .Classify(!testData.IsPaired, "自闭合短代码");
    }

    #endregion

    #region Property 4: 短代码展开正确性 - 显式测试

    /// <summary>
    /// Property 4 的显式测试版本 - Figure 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("{{< figure src=\"/images/test.jpg\" >}}", "<figure>")]
    [InlineData("{{< figure src=\"/images/test.jpg\" title=\"标题\" >}}", "<h4>标题</h4>")]
    [InlineData("{{< figure src=\"/images/test.jpg\" caption=\"说明\" >}}", "<p>说明</p>")]
    public void ShortcodeExpansion_Figure_ExplicitTest(string shortcode, string expectedContent)
    {
        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Contain(expectedContent);
    }

    /// <summary>
    /// Property 4 的显式测试版本 - Highlight 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("csharp", "Console.WriteLine(\"Hello\");")]
    [InlineData("javascript", "console.log('Hello');")]
    [InlineData("python", "print('Hello')")]
    [InlineData("go", "fmt.Println(\"Hello\")")]
    public void ShortcodeExpansion_Highlight_ExplicitTest(string language, string code)
    {
        // Arrange
        var shortcode = $"{{{{< highlight {language} >}}}}{code}{{{{< /highlight >}}}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Contain("<div class=\"highlight\">");
        result.Should().Contain($"language-{language}");
    }

    /// <summary>
    /// Property 4 的显式测试版本 - YouTube 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("dQw4w9WgXcQ")]
    [InlineData("abc123XYZ")]
    [InlineData("video_id_123")]
    public void ShortcodeExpansion_Youtube_ExplicitTest(string videoId)
    {
        // Arrange
        var shortcode = $"{{{{< youtube \"{videoId}\" >}}}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Contain("youtube-container");
        result.Should().Contain($"youtube.com/embed/{videoId}");
    }

    /// <summary>
    /// Property 4 的显式测试版本 - Vimeo 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("123456789")]
    [InlineData("987654321")]
    public void ShortcodeExpansion_Vimeo_ExplicitTest(string videoId)
    {
        // Arrange
        var shortcode = $"{{{{< vimeo \"{videoId}\" >}}}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Contain("vimeo-container");
        result.Should().Contain($"player.vimeo.com/video/{videoId}");
    }

    /// <summary>
    /// Property 4 的显式测试版本 - Gist 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("octocat", "abc123def456")]
    [InlineData("microsoft", "xyz789")]
    public void ShortcodeExpansion_Gist_ExplicitTest(string user, string gistId)
    {
        // Arrange
        var shortcode = $"{{{{< gist \"{user}\" \"{gistId}\" >}}}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Contain("gist.github.com");
        result.Should().Contain(user);
        result.Should().Contain(gistId);
    }

    /// <summary>
    /// Property 4 的显式测试版本 - Ref 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("posts/my-article.md", "/posts/my-article/")]
    [InlineData("about.md", "/about/")]
    [InlineData("docs/guide/intro.md", "/docs/guide/intro/")]
#pragma warning disable CA1054 // URI 参数应为 Uri 类型 - 测试中使用字符串更方便
    public void ShortcodeExpansion_Ref_ExplicitTest(string path, string expectedUrl)
#pragma warning restore CA1054
    {
        // Arrange
        var shortcode = $"{{{{< ref \"{path}\" >}}}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Be(expectedUrl);
    }

    /// <summary>
    /// Property 4 的显式测试版本 - Relref 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("posts/other-post.md", "posts/other-post/")]
    [InlineData("about.md", "about/")]
#pragma warning disable CA1054 // URI 参数应为 Uri 类型 - 测试中使用字符串更方便
    public void ShortcodeExpansion_Relref_ExplicitTest(string path, string expectedUrl)
#pragma warning restore CA1054
    {
        // Arrange
        var shortcode = $"{{{{< relref \"{path}\" >}}}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Be(expectedUrl);
    }

    /// <summary>
    /// Property 4 的显式测试版本 - Tweet 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Fact]
    public void ShortcodeExpansion_Tweet_ExplicitTest()
    {
        // Arrange
        var shortcode = "{{< tweet user=\"twitter\" id=\"1234567890\" >}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Contain("twitter-tweet");
        result.Should().Contain("twitter.com/twitter/status/1234567890");
    }

    /// <summary>
    /// Property 4 的显式测试版本 - Instagram 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("CxYz123AbC")]
    [InlineData("post_id_456")]
    public void ShortcodeExpansion_Instagram_ExplicitTest(string postId)
    {
        // Arrange
        var shortcode = $"{{{{< instagram \"{postId}\" >}}}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Contain("instagram-container");
        result.Should().Contain($"instagram.com/p/{postId}/embed");
    }

    /// <summary>
    /// Property 4 的显式测试版本 - Param 短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("author")]
    [InlineData("title")]
    [InlineData("description")]
    public void ShortcodeExpansion_Param_ExplicitTest(string paramName)
    {
        // Arrange
        var shortcode = $"{{{{< param \"{paramName}\" >}}}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().BeEmpty("param 需要 Page 参数上下文（未接线），返回空串");
    }

    /// <summary>
    /// Property 4 的显式测试版本 - 多个短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Fact]
    public void ShortcodeExpansion_MultipleShortcodes_ExplicitTest()
    {
        // Arrange
        var content = """
            这是一段介绍文字。

            {{< figure src="/images/photo.jpg" title="照片" >}}

            这是一段代码：

            {{< highlight csharp >}}
            Console.WriteLine("Hello");
            {{< /highlight >}}

            观看视频：

            {{< youtube "dQw4w9WgXcQ" >}}
            """;

        // Act
        var result = _processor.Process(content);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Contain("<figure>");
        result.Should().Contain("<div class=\"highlight\">");
        result.Should().Contain("youtube-container");
        result.Should().Contain("这是一段介绍文字");
        result.Should().Contain("这是一段代码");
        result.Should().Contain("观看视频");
    }

    /// <summary>
    /// Property 4 的显式测试版本 - 使用 % 分隔符
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Fact]
    public void ShortcodeExpansion_PercentDelimiter_ExplicitTest()
    {
        // Arrange
        var shortcode = "{{% highlight go %}}fmt.Println(\"Hello\"){{% /highlight %}}";

        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{% ");
        result.Should().NotContain(" %}}");
        result.Should().Contain("<div class=\"highlight\">");
        result.Should().Contain("language-go");
    }

    /// <summary>
    /// Property 4 的显式测试版本 - 缺少必需参数的短代码
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData("{{< figure >}}", "<!-- figure: 缺少 src 参数 -->")]
    [InlineData("{{< youtube >}}", "<!-- youtube: 缺少视频 ID -->")]
    [InlineData("{{< vimeo >}}", "<!-- vimeo: 缺少视频 ID -->")]
    [InlineData("{{< gist >}}", "<!-- gist: 缺少 user 或 id 参数 -->")]
    public void ShortcodeExpansion_MissingRequiredParams_ShouldReturnErrorComment(
        string shortcode, string expectedError)
    {
        // Act
        var result = _processor.Process(shortcode);

        // Assert
        result.Should().NotContain("{{<");
        result.Should().NotContain(">}}");
        result.Should().Contain(expectedError);
    }

    /// <summary>
    /// Property 4 的显式测试版本 - 未知短代码（fail-fast 语义，对齐 Hugo）
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Fact]
    public void ShortcodeExpansion_UnknownShortcode_ShouldThrow()
    {
        // Arrange
        var shortcode = "{{< unknown_shortcode param=\"value\" >}}";

        // Act & Assert
        var act = () => _processor.Process(shortcode);
        act.Should().Throw<Flint.Core.Content.Shortcodes.ShortcodeNotFoundException>()
            .Where(e => e.ShortcodeName == "unknown_shortcode");
    }

    /// <summary>
    /// Property 4 的显式测试版本 - 空内容
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Fact]
    public void ShortcodeExpansion_EmptyContent_ShouldReturnEmpty()
    {
        // Act
        var result = _processor.Process(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Property 4 的显式测试版本 - 无短代码内容
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Fact]
    public void ShortcodeExpansion_NoShortcodes_ShouldReturnOriginalContent()
    {
        // Arrange
        var content = "这是一段普通文本，没有任何短代码。";

        // Act
        var result = _processor.Process(content);

        // Assert
        result.Should().Be(content);
    }

    #endregion
}


#region 测试数据类型

/// <summary>
/// 短代码类型枚举
/// </summary>
public enum ShortcodeType
{
    /// <summary>
    /// Figure 短代码 - 图片展示
    /// </summary>
    Figure,

    /// <summary>
    /// Highlight 短代码 - 代码高亮
    /// </summary>
    Highlight,

    /// <summary>
    /// YouTube 短代码 - YouTube 视频嵌入
    /// </summary>
    Youtube,

    /// <summary>
    /// Vimeo 短代码 - Vimeo 视频嵌入
    /// </summary>
    Vimeo,

    /// <summary>
    /// Gist 短代码 - GitHub Gist 嵌入
    /// </summary>
    Gist,

    /// <summary>
    /// Ref 短代码 - 内部链接引用（绝对路径）
    /// </summary>
    Ref,

    /// <summary>
    /// Relref 短代码 - 内部链接引用（相对路径）
    /// </summary>
    Relref,

    /// <summary>
    /// Tweet 短代码 - Twitter 推文嵌入
    /// </summary>
    Tweet,

    /// <summary>
    /// Instagram 短代码 - Instagram 帖子嵌入
    /// </summary>
    Instagram,

    /// <summary>
    /// Param 短代码 - 获取页面参数
    /// </summary>
    Param
}

/// <summary>
/// 短代码测试数据
/// </summary>
public sealed class ShortcodeTestData
{
    /// <summary>
    /// 短代码类型
    /// </summary>
    public required ShortcodeType ShortcodeType { get; init; }

    /// <summary>
    /// 短代码参数
    /// </summary>
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }

    /// <summary>
    /// 生成短代码字符串
    /// </summary>
    public string GenerateShortcode()
    {
        var sb = new StringBuilder();
        sb.Append("{{< ");
        sb.Append(GetShortcodeName());

        foreach (var (key, value) in Parameters)
        {
            if (key.StartsWith("_pos", StringComparison.Ordinal))
            {
                // 位置参数
                sb.Append($" \"{value}\"");
            }
            else
            {
                // 命名参数
                sb.Append($" {key}=\"{value}\"");
            }
        }

        sb.Append(" >}}");
        return sb.ToString();
    }

    /// <summary>
    /// 获取短代码名称
    /// </summary>
    private string GetShortcodeName() => ShortcodeType switch
    {
        ShortcodeType.Figure => "figure",
        ShortcodeType.Highlight => "highlight",
        ShortcodeType.Youtube => "youtube",
        ShortcodeType.Vimeo => "vimeo",
        ShortcodeType.Gist => "gist",
        ShortcodeType.Ref => "ref",
        ShortcodeType.Relref => "relref",
        ShortcodeType.Tweet => "tweet",
        ShortcodeType.Instagram => "instagram",
        ShortcodeType.Param => "param",
        _ => "unknown"
    };

    /// <summary>
    /// 重写 ToString 用于测试输出
    /// </summary>
    public override string ToString() =>
        $"ShortcodeTestData {{ Type={ShortcodeType}, Params=[{string.Join(", ", Parameters.Select(p => $"{p.Key}={p.Value}"))}] }}";
}

/// <summary>
/// 多个短代码测试数据
/// </summary>
public sealed class MultipleShortcodesTestData
{
    /// <summary>
    /// 短代码列表
    /// </summary>
    public required IReadOnlyList<ShortcodeTestData> Shortcodes { get; init; }

    /// <summary>
    /// 生成包含多个短代码的内容
    /// </summary>
    public string GenerateContent()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Shortcodes.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }
            sb.Append(Shortcodes[i].GenerateShortcode());
        }
        return sb.ToString();
    }

    /// <summary>
    /// 重写 ToString 用于测试输出
    /// </summary>
    public override string ToString() =>
        $"MultipleShortcodesTestData {{ Count={Shortcodes.Count} }}";
}

/// <summary>
/// 配对短代码测试数据（带内容的短代码）
/// </summary>
public sealed class PairedShortcodeTestData
{
    /// <summary>
    /// 编程语言
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// 代码内容
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 是否显示行号
    /// </summary>
    public bool ShowLineNumbers { get; init; }

    /// <summary>
    /// 生成短代码字符串
    /// </summary>
    public string GenerateShortcode()
    {
        var sb = new StringBuilder();
        sb.Append("{{< highlight ");
        sb.Append(Language);

        if (ShowLineNumbers)
        {
            sb.Append(" options=\"linenos\"");
        }

        sb.Append(" >}}");
        sb.Append(Code);
        sb.Append("{{< /highlight >}}");

        return sb.ToString();
    }

    /// <summary>
    /// 重写 ToString 用于测试输出
    /// </summary>
    public override string ToString() =>
        $"PairedShortcodeTestData {{ Language={Language}, CodeLength={Code.Length}, ShowLineNumbers={ShowLineNumbers} }}";
}

/// <summary>
/// 混合内容测试数据（短代码和普通文本）
/// </summary>
public sealed class MixedContentTestData
{
    /// <summary>
    /// 文本部分列表
    /// </summary>
    public required IReadOnlyList<string> TextParts { get; init; }

    /// <summary>
    /// 短代码
    /// </summary>
    public required ShortcodeTestData Shortcode { get; init; }

    /// <summary>
    /// 生成混合内容
    /// </summary>
    public string GenerateContent()
    {
        var sb = new StringBuilder();

        // 添加第一个文本部分（如果存在）
        if (TextParts.Count > 0)
        {
            sb.AppendLine(TextParts[0]);
            sb.AppendLine();
        }

        // 添加短代码
        sb.AppendLine(Shortcode.GenerateShortcode());

        // 添加剩余的文本部分
        for (var i = 1; i < TextParts.Count; i++)
        {
            sb.AppendLine();
            sb.Append(TextParts[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 重写 ToString 用于测试输出
    /// </summary>
    public override string ToString() =>
        $"MixedContentTestData {{ TextPartsCount={TextParts.Count}, ShortcodeType={Shortcode.ShortcodeType} }}";
}

/// <summary>
/// 使用 % 分隔符的短代码测试数据
/// </summary>
public sealed class PercentDelimiterTestData
{
    /// <summary>
    /// 短代码类型
    /// </summary>
    public required ShortcodeType ShortcodeType { get; init; }

    /// <summary>
    /// 是否为配对短代码
    /// </summary>
    public bool IsPaired { get; init; }

    /// <summary>
    /// 短代码参数
    /// </summary>
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }

    /// <summary>
    /// 内部内容（仅配对短代码）
    /// </summary>
    public string? InnerContent { get; init; }

    /// <summary>
    /// 生成短代码字符串
    /// </summary>
    public string GenerateShortcode()
    {
        var sb = new StringBuilder();
        sb.Append("{{% ");
        sb.Append(GetShortcodeName());

        foreach (var (key, value) in Parameters)
        {
            if (key.StartsWith("_pos", StringComparison.Ordinal))
            {
                sb.Append($" {value}");
            }
            else
            {
                sb.Append($" {key}=\"{value}\"");
            }
        }

        sb.Append(" %}}");

        if (IsPaired && InnerContent != null)
        {
            sb.Append(InnerContent);
            sb.Append("{{% /");
            sb.Append(GetShortcodeName());
            sb.Append(" %}}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取短代码名称
    /// </summary>
    private string GetShortcodeName() => ShortcodeType switch
    {
        ShortcodeType.Highlight => "highlight",
        ShortcodeType.Figure => "figure",
        _ => "highlight"
    };

    /// <summary>
    /// 重写 ToString 用于测试输出
    /// </summary>
    public override string ToString() =>
        $"PercentDelimiterTestData {{ Type={ShortcodeType}, IsPaired={IsPaired} }}";
}

#endregion

#region FsCheck 生成器

/// <summary>
/// 短代码属性测试专用的 FsCheck 生成器
/// </summary>
public static class ShortcodeArbitraries
{
    /// <summary>
    /// 有效的图片路径列表
    /// </summary>
    private static readonly string[] ValidImagePaths =
    [
        "/images/test.jpg",
        "/images/photo.png",
        "/assets/img/banner.gif",
        "/static/images/logo.svg",
        "/media/picture.webp"
    ];

    /// <summary>
    /// 有效的标题列表
    /// </summary>
    private static readonly string[] ValidTitles =
    [
        "测试图片",
        "示例照片",
        "Banner Image",
        "Logo",
        "风景照片"
    ];

    /// <summary>
    /// 有效的视频 ID 列表
    /// </summary>
    private static readonly string[] ValidVideoIds =
    [
        "dQw4w9WgXcQ",
        "abc123XYZ",
        "video_id_456",
        "test123",
        "sample789"
    ];

    /// <summary>
    /// 有效的 GitHub 用户名列表
    /// </summary>
    private static readonly string[] ValidGitHubUsers =
    [
        "octocat",
        "microsoft",
        "dotnet",
        "github",
        "testuser"
    ];

    /// <summary>
    /// 有效的 Gist ID 列表
    /// </summary>
    private static readonly string[] ValidGistIds =
    [
        "abc123def456",
        "xyz789",
        "gist123",
        "sample456",
        "test789"
    ];

    /// <summary>
    /// 有效的文件路径列表
    /// </summary>
    private static readonly string[] ValidFilePaths =
    [
        "posts/my-article.md",
        "about.md",
        "docs/guide/intro.md",
        "blog/2024/post.md",
        "pages/contact.md"
    ];

    /// <summary>
    /// 有效的编程语言列表
    /// </summary>
    private static readonly string[] ValidLanguages =
    [
        "csharp",
        "javascript",
        "python",
        "go",
        "rust",
        "java",
        "typescript",
        "sql",
        "html",
        "css"
    ];

    /// <summary>
    /// 有效的代码片段列表
    /// </summary>
    private static readonly string[] ValidCodeSnippets =
    [
        "Console.WriteLine(\"Hello\");",
        "console.log('Hello');",
        "print('Hello')",
        "fmt.Println(\"Hello\")",
        "println!(\"Hello\")",
        "System.out.println(\"Hello\");",
        "SELECT * FROM users;",
        "<div>Hello</div>",
        ".class { color: red; }"
    ];

    /// <summary>
    /// 有效的参数名列表
    /// </summary>
    private static readonly string[] ValidParamNames =
    [
        "author",
        "title",
        "description",
        "date",
        "tags"
    ];

    /// <summary>
    /// 有效的文本片段列表
    /// </summary>
    private static readonly string[] ValidTextParts =
    [
        "这是一段介绍文字。",
        "以下是示例代码：",
        "观看下面的视频：",
        "查看这张图片：",
        "更多信息请参考：",
        "This is some text.",
        "Here is an example:",
        "Watch this video:"
    ];

    /// <summary>
    /// 生成 ShortcodeTestData
    /// </summary>
    public static Arbitrary<ShortcodeTestData> ShortcodeTestData() =>
        (from shortcodeType in Gen.Elements(
            ShortcodeType.Figure,
            ShortcodeType.Youtube,
            ShortcodeType.Vimeo,
            ShortcodeType.Gist,
            ShortcodeType.Ref,
            ShortcodeType.Relref,
            ShortcodeType.Tweet,
            ShortcodeType.Instagram,
            ShortcodeType.Param)
         from parameters in GenerateParametersForType(shortcodeType)
         select new ShortcodeTestData
         {
             ShortcodeType = shortcodeType,
             Parameters = parameters
         }).ToArbitrary();

    /// <summary>
    /// 根据短代码类型生成参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateParametersForType(ShortcodeType type)
    {
        return type switch
        {
            ShortcodeType.Figure => GenerateFigureParameters(),
            ShortcodeType.Youtube => GenerateYoutubeParameters(),
            ShortcodeType.Vimeo => GenerateVimeoParameters(),
            ShortcodeType.Gist => GenerateGistParameters(),
            ShortcodeType.Ref => GenerateRefParameters(),
            ShortcodeType.Relref => GenerateRelrefParameters(),
            ShortcodeType.Tweet => GenerateTweetParameters(),
            ShortcodeType.Instagram => GenerateInstagramParameters(),
            ShortcodeType.Param => GenerateParamParameters(),
            _ => Gen.Constant<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>())
        };
    }

    /// <summary>
    /// 生成 Figure 短代码参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateFigureParameters() =>
        from src in Gen.Elements(ValidImagePaths)
        from hasTitle in ArbMap.Default.GeneratorFor<bool>()
        from title in Gen.Elements(ValidTitles)
        from hasCaption in ArbMap.Default.GeneratorFor<bool>()
        from caption in Gen.Elements(ValidTitles)
        select CreateDictionary(
            ("src", src),
            hasTitle ? ("title", title) : default,
            hasCaption ? ("caption", caption) : default);

    /// <summary>
    /// 生成 YouTube 短代码参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateYoutubeParameters() =>
        from videoId in Gen.Elements(ValidVideoIds)
        from usePositional in ArbMap.Default.GeneratorFor<bool>()
        select usePositional
            ? CreateDictionary(("_pos0", videoId))
            : CreateDictionary(("id", videoId));

    /// <summary>
    /// 生成 Vimeo 短代码参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateVimeoParameters() =>
        from videoId in Gen.Elements("123456789", "987654321", "456789123", "789123456")
        from usePositional in ArbMap.Default.GeneratorFor<bool>()
        select usePositional
            ? CreateDictionary(("_pos0", videoId))
            : CreateDictionary(("id", videoId));

    /// <summary>
    /// 生成 Gist 短代码参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateGistParameters() =>
        from user in Gen.Elements(ValidGitHubUsers)
        from gistId in Gen.Elements(ValidGistIds)
        from usePositional in ArbMap.Default.GeneratorFor<bool>()
        select usePositional
            ? CreateDictionary(("_pos0", user), ("_pos1", gistId))
            : CreateDictionary(("user", user), ("id", gistId));

    /// <summary>
    /// 生成 Ref 短代码参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateRefParameters() =>
        from path in Gen.Elements(ValidFilePaths)
        select CreateDictionary(("_pos0", path));

    /// <summary>
    /// 生成 Relref 短代码参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateRelrefParameters() =>
        from path in Gen.Elements(ValidFilePaths)
        select CreateDictionary(("_pos0", path));

    /// <summary>
    /// 生成 Tweet 短代码参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateTweetParameters() =>
        from user in Gen.Elements("twitter", "github", "dotnet", "microsoft")
        from tweetId in Gen.Elements("1234567890", "9876543210", "1111111111")
        select CreateDictionary(("user", user), ("id", tweetId));

    /// <summary>
    /// 生成 Instagram 短代码参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateInstagramParameters() =>
        from postId in Gen.Elements("CxYz123AbC", "post_id_456", "AbCdEfGhIj", "test123post")
        from usePositional in ArbMap.Default.GeneratorFor<bool>()
        select usePositional
            ? CreateDictionary(("_pos0", postId))
            : CreateDictionary(("id", postId));

    /// <summary>
    /// 生成 Param 短代码参数
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenerateParamParameters() =>
        from paramName in Gen.Elements(ValidParamNames)
        select CreateDictionary(("_pos0", paramName));

    /// <summary>
    /// 创建字典辅助方法
    /// </summary>
    private static IReadOnlyDictionary<string, string> CreateDictionary(
        params (string Key, string Value)[] items)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (key, value) in items)
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
            {
                dict[key] = value;
            }
        }
        return dict;
    }

    /// <summary>
    /// 生成 MultipleShortcodesTestData
    /// </summary>
    public static Arbitrary<MultipleShortcodesTestData> MultipleShortcodesTestData() =>
        (from count in Gen.Choose(1, 4)
         from shortcodes in Gen.ListOf<ShortcodeTestData>(ShortcodeTestData().Generator).Select(s => s.Take(count).ToList())
         select new MultipleShortcodesTestData
         {
             Shortcodes = shortcodes
         }).ToArbitrary();

    /// <summary>
    /// 生成 PairedShortcodeTestData
    /// </summary>
    public static Arbitrary<PairedShortcodeTestData> PairedShortcodeTestData() =>
        (from language in Gen.Elements(ValidLanguages)
         from code in Gen.Elements(ValidCodeSnippets)
         from showLineNumbers in ArbMap.Default.GeneratorFor<bool>()
         select new PairedShortcodeTestData
         {
             Language = language,
             Code = code,
             ShowLineNumbers = showLineNumbers
         }).ToArbitrary();

    /// <summary>
    /// 生成 MixedContentTestData
    /// </summary>
    public static Arbitrary<MixedContentTestData> MixedContentTestData() =>
        (from textCount in Gen.Choose(1, 3)
         from textParts in Gen.ListOf<string>(Gen.Elements(ValidTextParts)).Select(t => t.Take(textCount).ToList())
         from shortcode in ShortcodeTestData().Generator
         select new MixedContentTestData
         {
             TextParts = textParts,
             Shortcode = shortcode
         }).ToArbitrary();

    /// <summary>
    /// 生成 PercentDelimiterTestData
    /// </summary>
    public static Arbitrary<PercentDelimiterTestData> PercentDelimiterTestData() =>
        (from isPaired in ArbMap.Default.GeneratorFor<bool>()
         from language in Gen.Elements(ValidLanguages)
         from code in Gen.Elements(ValidCodeSnippets)
         select new PercentDelimiterTestData
         {
             ShortcodeType = ShortcodeType.Highlight,
             IsPaired = isPaired,
             Parameters = new Dictionary<string, string> { ["_pos0"] = language },
             InnerContent = isPaired ? code : null
         }).ToArbitrary();
}

#endregion

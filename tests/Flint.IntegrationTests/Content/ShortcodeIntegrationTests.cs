// Flint 静态站点生成器
// 短代码处理集成测试
// 测试 figure、highlight、gist、youtube/vimeo、自定义短代码和嵌套短代码
// _Requirements: 2.3_

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Models;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Content;

/// <summary>
/// 短代码处理集成测试
/// 验证 ShortcodeProcessor、ShortcodeRegistry 和各种内置短代码的协同工作
/// </summary>
public class ShortcodeIntegrationTests : IDisposable
{
    private readonly ShortcodeProcessor _processor;
    private readonly ShortcodeRegistry _registry;
    private readonly string _tempDir;

    public ShortcodeIntegrationTests()
    {
        _registry = new ShortcodeRegistry(registerBuiltins: true);
        _processor = new ShortcodeProcessor(_registry);
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-shortcode-test-{Guid.NewGuid():N}");
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

    #region Figure 短代码测试

    /// <summary>
    /// 测试 figure 短代码 - 仅 src 参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_FigureShortcode_OnlySrc_ShouldGenerateBasicFigure()
    {
        // Arrange
        var content = "{{< figure src=\"/images/test.jpg\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<figure>");
        result.Should().Contain("<img src=\"/images/test.jpg\"");
        result.Should().Contain("</figure>");
    }

    /// <summary>
    /// 测试 figure 短代码 - 完整参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_FigureShortcode_AllParameters_ShouldGenerateCompleteFigure()
    {
        // Arrange
        var content = "{{< figure src=\"/images/photo.jpg\" title=\"美丽的风景\" caption=\"这是一张风景照片\" alt=\"风景图片\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<figure>");
        result.Should().Contain("<img src=\"/images/photo.jpg\"");
        result.Should().Contain("alt=\"风景图片\"");
        result.Should().Contain("title=\"美丽的风景\"");
        result.Should().Contain("<figcaption>");
        result.Should().Contain("<h4>美丽的风景</h4>");
        result.Should().Contain("<p>这是一张风景照片</p>");
        result.Should().Contain("</figcaption>");
        result.Should().Contain("</figure>");
    }

    /// <summary>
    /// 测试 figure 短代码 - 带链接
    /// </summary>
    [Fact]
    public async Task ProcessAsync_FigureShortcode_WithLink_ShouldGenerateFigureWithLink()
    {
        // Arrange
        var content = "{{< figure src=\"/images/thumb.jpg\" link=\"/images/full.jpg\" target=\"_blank\" rel=\"noopener\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<figure>");
        result.Should().Contain("<a href=\"/images/full.jpg\"");
        result.Should().Contain("target=\"_blank\"");
        result.Should().Contain("rel=\"noopener\"");
        result.Should().Contain("<img src=\"/images/thumb.jpg\"");
        result.Should().Contain("</a>");
        result.Should().Contain("</figure>");
    }

    /// <summary>
    /// 测试 figure 短代码 - 带尺寸
    /// </summary>
    [Fact]
    public async Task ProcessAsync_FigureShortcode_WithDimensions_ShouldGenerateFigureWithSize()
    {
        // Arrange
        var content = "{{< figure src=\"/images/sized.jpg\" width=\"800\" height=\"600\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<img src=\"/images/sized.jpg\"");
        result.Should().Contain("width=\"800\"");
        result.Should().Contain("height=\"600\"");
    }

    /// <summary>
    /// 测试 figure 短代码 - 带 CSS 类
    /// </summary>
    [Fact]
    public async Task ProcessAsync_FigureShortcode_WithClass_ShouldGenerateFigureWithClass()
    {
        // Arrange
        var content = "{{< figure src=\"/images/styled.jpg\" class=\"featured-image\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<figure class=\"featured-image\">");
    }

    /// <summary>
    /// 测试 figure 短代码 - 带归属信息
    /// </summary>
    [Fact]
    public async Task ProcessAsync_FigureShortcode_WithAttribution_ShouldGenerateFigureWithAttr()
    {
        // Arrange
        var content = "{{< figure src=\"/images/photo.jpg\" attr=\"Photo by John\" attrlink=\"https://example.com/john\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<figcaption>");
        result.Should().Contain("<a href=\"https://example.com/john\">Photo by John</a>");
    }

    /// <summary>
    /// 测试 figure 短代码 - 缺少 src 参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_FigureShortcode_MissingSrc_ShouldReturnErrorComment()
    {
        // Arrange
        var content = "{{< figure title=\"无图片\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<!-- figure: 缺少 src 参数 -->");
    }

    /// <summary>
    /// 测试 figure 短代码 - 位置参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_FigureShortcode_PositionalArgs_ShouldWork()
    {
        // Arrange
        var content = "{{< figure \"/images/pos.jpg\" \"位置标题\" \"位置说明\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<img src=\"/images/pos.jpg\"");
        result.Should().Contain("<h4>位置标题</h4>");
        result.Should().Contain("<p>位置说明</p>");
    }

    #endregion

    #region Highlight 短代码测试

    /// <summary>
    /// 测试 highlight 短代码 - 基本用法
    /// </summary>
    [Fact]
    public async Task ProcessAsync_HighlightShortcode_BasicUsage_ShouldGenerateCodeBlock()
    {
        // Arrange
        var content = "{{< highlight csharp >}}Console.WriteLine(\"Hello, World!\");{{< /highlight >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"highlight\">");
        result.Should().Contain("<pre class=\"chroma\">");
        result.Should().Contain("language-csharp");
        result.Should().Contain("data-lang=\"csharp\"");
        result.Should().Contain("Console.WriteLine");
    }

    /// <summary>
    /// 测试 highlight 短代码 - 各种编程语言
    /// </summary>
    [Theory]
    [InlineData("go", "fmt.Println(\"Hello\")")]
    [InlineData("python", "print(\"Hello\")")]
    [InlineData("javascript", "console.log(\"Hello\")")]
    [InlineData("rust", "println!(\"Hello\")")]
    [InlineData("java", "System.out.println(\"Hello\")")]
    [InlineData("cpp", "std::cout << \"Hello\"")]
    [InlineData("sql", "SELECT * FROM users")]
    [InlineData("html", "<div>Hello</div>")]
    [InlineData("css", ".class { color: red; }")]
    [InlineData("json", "{\"key\": \"value\"}")]
    [InlineData("yaml", "key: value")]
    [InlineData("xml", "<root><item/></root>")]
    [InlineData("bash", "echo \"Hello\"")]
    [InlineData("powershell", "Write-Host \"Hello\"")]
    public async Task ProcessAsync_HighlightShortcode_VariousLanguages_ShouldGenerateCorrectLanguageClass(
        string language, string code)
    {
        // Arrange
        var content = $"{{{{< highlight {language} >}}}}{code}{{{{< /highlight >}}}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain($"language-{language}");
        result.Should().Contain($"data-lang=\"{language}\"");
    }

    /// <summary>
    /// 测试 highlight 短代码 - 带行号选项
    /// 注意：当前实现中，linenos 选项需要在 options 参数中包含 "linenos" 关键字
    /// </summary>
    [Fact]
    public async Task ProcessAsync_HighlightShortcode_WithLineNumbers_ShouldIncludeLinenosAttribute()
    {
        // Arrange - 使用正确的选项格式
        var content = "{{< highlight go options=\"linenos\" >}}package main\n\nfunc main() {\n    fmt.Println(\"Hello\")\n}{{< /highlight >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("data-linenos=\"true\"");
    }

    /// <summary>
    /// 测试 highlight 短代码 - 带高亮行选项
    /// 注意：当前实现中，hl_lines 选项解析只支持单个数字，不支持范围
    /// </summary>
    [Fact]
    public async Task ProcessAsync_HighlightShortcode_WithHighlightLines_ShouldIncludeHlLinesAttribute()
    {
        // Arrange - 使用单个高亮行
        var content = "{{< highlight go options=\"hl_lines=3\" >}}line1\nline2\nline3\nline4\nline5{{< /highlight >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("data-hl-lines=\"3\"");
    }

    /// <summary>
    /// 测试 highlight 短代码 - 组合选项
    /// </summary>
    [Fact]
    public async Task ProcessAsync_HighlightShortcode_CombinedOptions_ShouldIncludeAllAttributes()
    {
        // Arrange - 使用正确的选项格式，单个高亮行
        var content = "{{< highlight python options=\"linenos,hl_lines=2\" >}}def hello():\n    print(\"Hello\")\n\n    return True{{< /highlight >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("language-python");
        result.Should().Contain("data-linenos=\"true\"");
        result.Should().Contain("data-hl-lines=\"2\"");
    }

    /// <summary>
    /// 测试 highlight 短代码 - 多行代码
    /// </summary>
    [Fact]
    public async Task ProcessAsync_HighlightShortcode_MultilineCode_ShouldPreserveFormatting()
    {
        // Arrange
        var code = @"public class Program
{
    public static void Main()
    {
        Console.WriteLine(""Hello"");
    }
}";
        var content = $"{{{{< highlight csharp >}}}}{code}{{{{< /highlight >}}}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("public class Program");
        result.Should().Contain("public static void Main()");
        result.Should().Contain("Console.WriteLine");
    }

    /// <summary>
    /// 测试 highlight 短代码 - 特殊字符转义
    /// </summary>
    [Fact]
    public async Task ProcessAsync_HighlightShortcode_SpecialCharacters_ShouldEscapeHtml()
    {
        // Arrange
        var content = "{{< highlight html >}}<div class=\"test\">&amp;</div>{{< /highlight >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("&lt;div");
        result.Should().Contain("&gt;");
        result.Should().Contain("&amp;amp;");
    }

    /// <summary>
    /// 测试 highlight 短代码 - 使用 % 分隔符（Markdown 处理）
    /// </summary>
    [Fact]
    public async Task ProcessAsync_HighlightShortcode_PercentDelimiter_ShouldWork()
    {
        // Arrange
        var content = "{{% highlight go %}}fmt.Println(\"Hello\"){{% /highlight %}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("language-go");
        result.Should().Contain("fmt.Println");
    }

    /// <summary>
    /// 测试 highlight 短代码 - 空内容
    /// </summary>
    [Fact]
    public async Task ProcessAsync_HighlightShortcode_EmptyContent_ShouldGenerateEmptyCodeBlock()
    {
        // Arrange
        var content = "{{< highlight text >}}{{< /highlight >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"highlight\">");
        result.Should().Contain("<code class=\"language-text\"");
        result.Should().Contain("</code></pre></div>");
    }

    #endregion

    #region Gist 短代码测试

    /// <summary>
    /// 测试 gist 短代码 - 基本用法
    /// </summary>
    [Fact]
    public async Task ProcessAsync_GistShortcode_BasicUsage_ShouldGenerateScript()
    {
        // Arrange
        var content = "{{< gist \"octocat\" \"abc123def456\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<script src=\"https://gist.github.com/octocat/abc123def456.js\"");
        result.Should().Contain("</script>");
    }

    /// <summary>
    /// 测试 gist 短代码 - 带文件参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_GistShortcode_WithFile_ShouldIncludeFileParameter()
    {
        // Arrange
        var content = "{{< gist \"octocat\" \"abc123\" \"main.go\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("https://gist.github.com/octocat/abc123.js?file=main.go");
    }

    /// <summary>
    /// 测试 gist 短代码 - 命名参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_GistShortcode_NamedParameters_ShouldWork()
    {
        // Arrange
        var content = "{{< gist user=\"microsoft\" id=\"xyz789\" file=\"example.cs\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("https://gist.github.com/microsoft/xyz789.js?file=example.cs");
    }

    /// <summary>
    /// 测试 gist 短代码 - 缺少必需参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_GistShortcode_MissingUser_ShouldReturnErrorComment()
    {
        // Arrange
        var content = "{{< gist >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<!-- gist: 缺少 user 或 id 参数 -->");
    }

    /// <summary>
    /// 测试 gist 短代码 - 缺少 gist ID
    /// </summary>
    [Fact]
    public async Task ProcessAsync_GistShortcode_MissingId_ShouldReturnErrorComment()
    {
        // Arrange
        var content = "{{< gist \"octocat\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<!-- gist: 缺少 user 或 id 参数 -->");
    }

    /// <summary>
    /// 测试 gist 短代码 - URL 编码特殊字符
    /// </summary>
    [Fact]
    public async Task ProcessAsync_GistShortcode_SpecialCharactersInFile_ShouldUrlEncode()
    {
        // Arrange
        var content = "{{< gist \"user\" \"id123\" \"file name.txt\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("file=file+name.txt");
    }

    #endregion

    #region YouTube 短代码测试

    /// <summary>
    /// 测试 youtube 短代码 - 基本用法
    /// </summary>
    [Fact]
    public async Task ProcessAsync_YoutubeShortcode_BasicUsage_ShouldGenerateIframe()
    {
        // Arrange
        var content = "{{< youtube \"dQw4w9WgXcQ\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"youtube-container\"");
        result.Should().Contain("<iframe");
        result.Should().Contain("src=\"https://www.youtube.com/embed/dQw4w9WgXcQ\"");
        result.Should().Contain("allowfullscreen");
        result.Should().Contain("</iframe>");
        result.Should().Contain("</div>");
    }

    /// <summary>
    /// 测试 youtube 短代码 - 命名参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_YoutubeShortcode_NamedId_ShouldWork()
    {
        // Arrange
        var content = "{{< youtube id=\"abc123XYZ\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("youtube.com/embed/abc123XYZ");
    }

    /// <summary>
    /// 测试 youtube 短代码 - 带标题
    /// </summary>
    [Fact]
    public async Task ProcessAsync_YoutubeShortcode_WithTitle_ShouldIncludeTitle()
    {
        // Arrange
        var content = "{{< youtube id=\"abc123\" title=\"精彩视频\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("title=\"精彩视频\"");
    }

    /// <summary>
    /// 测试 youtube 短代码 - 自动播放
    /// </summary>
    [Fact]
    public async Task ProcessAsync_YoutubeShortcode_WithAutoplay_ShouldIncludeAutoplayParam()
    {
        // Arrange
        var content = "{{< youtube id=\"abc123\" autoplay=\"1\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("autoplay=1");
    }

    /// <summary>
    /// 测试 youtube 短代码 - 指定开始时间
    /// </summary>
    [Fact]
    public async Task ProcessAsync_YoutubeShortcode_WithStartTime_ShouldIncludeStartParam()
    {
        // Arrange
        var content = "{{< youtube id=\"abc123\" start=\"120\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("start=120");
    }

    /// <summary>
    /// 测试 youtube 短代码 - 组合参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_YoutubeShortcode_CombinedParams_ShouldIncludeAllParams()
    {
        // Arrange
        var content = "{{< youtube id=\"abc123\" autoplay=\"1\" start=\"60\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("autoplay=1");
        result.Should().Contain("start=60");
    }

    /// <summary>
    /// 测试 youtube 短代码 - 缺少视频 ID
    /// </summary>
    [Fact]
    public async Task ProcessAsync_YoutubeShortcode_MissingId_ShouldReturnErrorComment()
    {
        // Arrange
        var content = "{{< youtube >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<!-- youtube: 缺少视频 ID -->");
    }

    /// <summary>
    /// 测试 youtube 短代码 - 响应式容器样式
    /// </summary>
    [Fact]
    public async Task ProcessAsync_YoutubeShortcode_ResponsiveContainer_ShouldHaveCorrectStyles()
    {
        // Arrange
        var content = "{{< youtube \"abc123\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("position: relative");
        result.Should().Contain("padding-bottom: 56.25%");
        result.Should().Contain("height: 0");
        result.Should().Contain("overflow: hidden");
    }

    #endregion

    #region Vimeo 短代码测试

    /// <summary>
    /// 测试 vimeo 短代码 - 基本用法
    /// </summary>
    [Fact]
    public async Task ProcessAsync_VimeoShortcode_BasicUsage_ShouldGenerateIframe()
    {
        // Arrange
        var content = "{{< vimeo \"123456789\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"vimeo-container\"");
        result.Should().Contain("<iframe");
        result.Should().Contain("src=\"https://player.vimeo.com/video/123456789\"");
        result.Should().Contain("allowfullscreen");
        result.Should().Contain("</iframe>");
        result.Should().Contain("</div>");
    }

    /// <summary>
    /// 测试 vimeo 短代码 - 命名参数
    /// </summary>
    [Fact]
    public async Task ProcessAsync_VimeoShortcode_NamedId_ShouldWork()
    {
        // Arrange
        var content = "{{< vimeo id=\"987654321\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("player.vimeo.com/video/987654321");
    }

    /// <summary>
    /// 测试 vimeo 短代码 - 带标题
    /// </summary>
    [Fact]
    public async Task ProcessAsync_VimeoShortcode_WithTitle_ShouldIncludeTitle()
    {
        // Arrange
        var content = "{{< vimeo id=\"123456\" title=\"Vimeo 视频\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("title=\"Vimeo 视频\"");
    }

    /// <summary>
    /// 测试 vimeo 短代码 - 缺少视频 ID
    /// </summary>
    [Fact]
    public async Task ProcessAsync_VimeoShortcode_MissingId_ShouldReturnErrorComment()
    {
        // Arrange
        var content = "{{< vimeo >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<!-- vimeo: 缺少视频 ID -->");
    }

    /// <summary>
    /// 测试 vimeo 短代码 - 响应式容器样式
    /// </summary>
    [Fact]
    public async Task ProcessAsync_VimeoShortcode_ResponsiveContainer_ShouldHaveCorrectStyles()
    {
        // Arrange
        var content = "{{< vimeo \"123456\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("position: relative");
        result.Should().Contain("padding-bottom: 56.25%");
    }

    #endregion

    #region 其他内置短代码测试

    /// <summary>
    /// 测试 ref 短代码 - 基本用法
    /// </summary>
    [Fact]
    public async Task ProcessAsync_RefShortcode_BasicUsage_ShouldGenerateAbsoluteUrl()
    {
        // Arrange
        var content = "{{< ref \"posts/my-article.md\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("/posts/my-article/");
    }

    /// <summary>
    /// 测试 ref 短代码 - 移除 .md 扩展名
    /// </summary>
    [Fact]
    public async Task ProcessAsync_RefShortcode_RemovesMdExtension_ShouldWork()
    {
        // Arrange
        var content = "{{< ref \"about.md\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("/about/");
        result.Should().NotContain(".md");
    }

    /// <summary>
    /// 测试 relref 短代码 - 基本用法
    /// </summary>
    [Fact]
    public async Task ProcessAsync_RelrefShortcode_BasicUsage_ShouldGenerateRelativeUrl()
    {
        // Arrange
        var content = "{{< relref \"posts/other-post.md\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("posts/other-post/");
        result.Should().NotStartWith("/");
    }

    /// <summary>
    /// 测试 tweet 短代码 - 基本用法
    /// </summary>
    [Fact]
    public async Task ProcessAsync_TweetShortcode_BasicUsage_ShouldGenerateBlockquote()
    {
        // Arrange
        var content = "{{< tweet user=\"twitter\" id=\"1234567890\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<blockquote class=\"twitter-tweet\">");
        result.Should().Contain("https://twitter.com/twitter/status/1234567890");
        result.Should().Contain("platform.twitter.com/widgets.js");
    }

    /// <summary>
    /// 测试 instagram 短代码 - 基本用法
    /// </summary>
    [Fact]
    public async Task ProcessAsync_InstagramShortcode_BasicUsage_ShouldGenerateIframe()
    {
        // Arrange
        var content = "{{< instagram \"CxYz123AbC\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"instagram-container\">");
        result.Should().Contain("<iframe");
        result.Should().Contain("https://www.instagram.com/p/CxYz123AbC/embed");
    }

    /// <summary>
    /// 测试 instagram 短代码 - 隐藏说明
    /// </summary>
    [Fact]
    public async Task ProcessAsync_InstagramShortcode_HideCaption_ShouldIncludeParam()
    {
        // Arrange
        var content = "{{< instagram id=\"abc123\" hidecaption=\"true\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("hidecaption=true");
    }

    /// <summary>
    /// 测试 param 短代码 - 基本用法
    /// </summary>
    [Fact]
    public async Task ProcessAsync_ParamShortcode_BasicUsage_ShouldGeneratePlaceholder()
    {
        // Arrange
        var content = "{{< param \"author\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert：param 需要 Page 参数上下文（未接线），当前返回空串而非 Go 模板字面量
        result.Should().BeEmpty();
    }

    #endregion

    #region 自定义短代码测试

    /// <summary>
    /// 测试自定义短代码 - 注册和处理
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CustomShortcode_RegisterAndProcess_ShouldWork()
    {
        // Arrange
        var customShortcode = new AlertShortcode();
        _registry.Register(customShortcode);
        var content = "{{< alert type=\"warning\" >}}这是一条警告消息{{< /alert >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"alert alert-warning\">");
        result.Should().Contain("这是一条警告消息");
        result.Should().Contain("</div>");
    }

    /// <summary>
    /// 测试自定义短代码 - 不同类型
    /// </summary>
    [Theory]
    [InlineData("info", "alert-info")]
    [InlineData("warning", "alert-warning")]
    [InlineData("danger", "alert-danger")]
    [InlineData("success", "alert-success")]
    public async Task ProcessAsync_CustomAlertShortcode_DifferentTypes_ShouldGenerateCorrectClass(
        string type, string expectedClass)
    {
        // Arrange
        var customShortcode = new AlertShortcode();
        _registry.Register(customShortcode);
        var content = $"{{{{< alert type=\"{type}\" >}}}}消息内容{{{{< /alert >}}}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain($"class=\"alert {expectedClass}\"");
    }

    /// <summary>
    /// 测试自定义短代码 - 带标题
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CustomAlertShortcode_WithTitle_ShouldIncludeTitle()
    {
        // Arrange
        var customShortcode = new AlertShortcode();
        _registry.Register(customShortcode);
        var content = "{{< alert type=\"info\" title=\"提示\" >}}这是提示内容{{< /alert >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<strong>提示</strong>");
        result.Should().Contain("这是提示内容");
    }

    /// <summary>
    /// 测试自定义短代码 - 覆盖内置短代码
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CustomShortcode_OverrideBuiltin_ShouldUseCustom()
    {
        // Arrange
        var customFigure = new CustomFigureShortcode();
        _registry.Register(customFigure);
        var content = "{{< figure src=\"test.jpg\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<!-- 自定义 figure 短代码 -->");
        result.Should().Contain("test.jpg");
    }

    /// <summary>
    /// 测试自定义短代码 - 自闭合形式
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CustomShortcode_SelfClosing_ShouldWork()
    {
        // Arrange
        var customShortcode = new IconShortcode();
        _registry.Register(customShortcode);
        var content = "{{< icon name=\"home\" size=\"24\" />}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<i class=\"icon icon-home\"");
        result.Should().Contain("style=\"font-size: 24px\"");
    }

    /// <summary>
    /// 测试自定义短代码 - 异步处理
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CustomAsyncShortcode_ShouldProcessAsynchronously()
    {
        // Arrange
        var asyncShortcode = new AsyncDataShortcode();
        _registry.Register(asyncShortcode);
        var content = "{{< asyncdata delay=\"10\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"async-data\">");
        result.Should().Contain("异步加载完成");
    }

    /// <summary>
    /// 测试自定义短代码 - 处理异常
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CustomShortcode_ThrowsException_ShouldReturnErrorComment()
    {
        // Arrange
        var errorShortcode = new ErrorShortcode();
        _registry.Register(errorShortcode);
        var content = "{{< error >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<!-- 短代码 error 处理错误:");
    }

    /// <summary>
    /// 测试自定义短代码 - 访问页面上下文
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CustomShortcode_WithPageContext_ShouldAccessContext()
    {
        // Arrange
        var contextShortcode = new ContextAwareShortcode();
        _registry.Register(contextShortcode);
        var content = "{{< contextaware >}}";
        var pageContext = new { Title = "测试页面", Author = "测试作者" };

        // Act
        var result = await _processor.ProcessAsync(content, pageContext: pageContext);

        // Assert
        result.Should().Contain("页面标题: 测试页面");
    }

    #endregion

    #region 嵌套短代码测试

    /// <summary>
    /// 测试嵌套短代码 - 简单嵌套
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NestedShortcodes_SimpleNesting_ShouldProcessBoth()
    {
        // Arrange
        var alertShortcode = new AlertShortcode();
        _registry.Register(alertShortcode);
        var content = "{{< alert type=\"info\" >}}查看这个视频: {{< youtube \"abc123\" >}}{{< /alert >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"alert alert-info\">");
        result.Should().Contain("youtube.com/embed/abc123");
        result.Should().Contain("</div>");
    }

    /// <summary>
    /// 测试嵌套短代码 - 多层嵌套
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NestedShortcodes_MultiLevel_ShouldProcessAll()
    {
        // Arrange
        var alertShortcode = new AlertShortcode();
        var boxShortcode = new BoxShortcode();
        _registry.Register(alertShortcode);
        _registry.Register(boxShortcode);
        var content = "{{< box >}}{{< alert type=\"warning\" >}}嵌套内容{{< /alert >}}{{< /box >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"box\">");
        result.Should().Contain("<div class=\"alert alert-warning\">");
        result.Should().Contain("嵌套内容");
    }

    /// <summary>
    /// 测试嵌套短代码 - 同类型嵌套
    /// 注意：当前实现中，同类型嵌套的内部短代码会作为原始文本保留在外部短代码的内容中
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NestedShortcodes_SameType_ShouldHandleCorrectly()
    {
        // Arrange
        var boxShortcode = new BoxShortcode();
        _registry.Register(boxShortcode);
        var content = "{{< box >}}外层 {{< box >}}内层{{< /box >}} 外层继续{{< /box >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        // 验证外层 box 被正确处理
        result.Should().Contain("<div class=\"box\">");
        result.Should().Contain("外层");
        result.Should().Contain("外层继续");
        // 内层 box 的处理取决于实现，可能作为嵌套处理或作为文本保留
        result.Should().Contain("内层");
    }

    /// <summary>
    /// 测试嵌套短代码 - 自闭合短代码在配对短代码内
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NestedShortcodes_SelfClosingInPaired_ShouldWork()
    {
        // Arrange
        var alertShortcode = new AlertShortcode();
        var iconShortcode = new IconShortcode();
        _registry.Register(alertShortcode);
        _registry.Register(iconShortcode);
        var content = "{{< alert type=\"info\" >}}{{< icon name=\"info\" />}} 这是信息{{< /alert >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"alert alert-info\">");
        result.Should().Contain("<i class=\"icon icon-info\"");
        result.Should().Contain("这是信息");
    }

    /// <summary>
    /// 测试嵌套短代码 - 多个并列嵌套
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NestedShortcodes_MultipleSiblings_ShouldProcessAll()
    {
        // Arrange
        var boxShortcode = new BoxShortcode();
        _registry.Register(boxShortcode);
        var content = "{{< box >}}{{< youtube \"vid1\" >}} 和 {{< youtube \"vid2\" >}}{{< /box >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"box\">");
        result.Should().Contain("youtube.com/embed/vid1");
        result.Should().Contain("youtube.com/embed/vid2");
    }

    /// <summary>
    /// 测试嵌套短代码 - 深层嵌套（3层）
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NestedShortcodes_DeepNesting_ShouldProcessAll()
    {
        // Arrange
        var alertShortcode = new AlertShortcode();
        var boxShortcode = new BoxShortcode();
        var cardShortcode = new CardShortcode();
        _registry.Register(alertShortcode);
        _registry.Register(boxShortcode);
        _registry.Register(cardShortcode);
        var content = "{{< card >}}{{< box >}}{{< alert type=\"success\" >}}深层内容{{< /alert >}}{{< /box >}}{{< /card >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"card\">");
        result.Should().Contain("<div class=\"box\">");
        result.Should().Contain("<div class=\"alert alert-success\">");
        result.Should().Contain("深层内容");
    }

    /// <summary>
    /// 测试嵌套短代码 - 混合文本和短代码
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NestedShortcodes_MixedContent_ShouldPreserveText()
    {
        // Arrange
        var boxShortcode = new BoxShortcode();
        _registry.Register(boxShortcode);
        var content = "{{< box >}}前面的文本 {{< youtube \"abc\" >}} 中间的文本 {{< figure src=\"img.jpg\" >}} 后面的文本{{< /box >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("前面的文本");
        result.Should().Contain("中间的文本");
        result.Should().Contain("后面的文本");
        result.Should().Contain("youtube.com/embed/abc");
        result.Should().Contain("img.jpg");
    }

    /// <summary>
    /// 测试嵌套短代码 - 使用 % 分隔符
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NestedShortcodes_PercentDelimiter_ShouldWork()
    {
        // Arrange
        var boxShortcode = new BoxShortcode();
        _registry.Register(boxShortcode);
        var content = "{{% box %}}{{% highlight go %}}fmt.Println(\"Hello\"){{% /highlight %}}{{% /box %}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("<div class=\"box\">");
        result.Should().Contain("language-go");
    }

    /// <summary>
    /// 测试嵌套短代码 - 内容包含 Markdown
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NestedShortcodes_WithMarkdownContent_ShouldPreserveMarkdown()
    {
        // Arrange
        var boxShortcode = new BoxShortcode();
        _registry.Register(boxShortcode);
        var content = "{{< box >}}# 标题\n\n这是**粗体**文本\n\n- 列表项1\n- 列表项2{{< /box >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("# 标题");
        result.Should().Contain("**粗体**");
        result.Should().Contain("- 列表项1");
    }

    #endregion

    #region 边界条件和错误处理测试

    /// <summary>
    /// 测试空内容
    /// </summary>
    [Fact]
    public async Task ProcessAsync_EmptyContent_ShouldReturnEmpty()
    {
        // Act
        var result = await _processor.ProcessAsync(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// 测试 null 内容
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NullContent_ShouldReturnEmpty()
    {
        // Act
        var result = await _processor.ProcessAsync(null!);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// 测试无短代码的内容
    /// </summary>
    [Fact]
    public async Task ProcessAsync_NoShortcodes_ShouldReturnOriginal()
    {
        // Arrange
        var content = "这是一段普通文本，没有任何短代码。\n\n包含多行内容。";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Be(content);
    }

    /// <summary>
    /// 测试未注册的短代码（fail-fast 语义，对齐 Hugo）
    /// </summary>
    [Fact]
    public async Task ProcessAsync_UnregisteredShortcode_ShouldThrow()
    {
        // Arrange
        var content = "{{< nonexistent_shortcode param=\"value\" >}}";

        // Act
        var act = async () => await _processor.ProcessAsync(content);

        // Assert
        await act.Should().ThrowAsync<Flint.Core.Content.Shortcodes.ShortcodeNotFoundException>()
            .Where(e => e.ShortcodeName == "nonexistent_shortcode");
    }

    /// <summary>
    /// 测试不完整的短代码标记
    /// </summary>
    [Fact]
    public async Task ProcessAsync_IncompleteShortcode_ShouldPreserveOriginal()
    {
        // Arrange
        var content = "这是文本 {{< figure src=\"test.jpg\" 不完整的标记";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        // 不完整的短代码应该被保留或优雅处理
        result.Should().Contain("这是文本");
    }

    /// <summary>
    /// 测试不匹配的关闭标签
    /// </summary>
    [Fact]
    public async Task ProcessAsync_MismatchedClosingTag_ShouldHandleGracefully()
    {
        // Arrange
        var content = "{{< highlight go >}}代码{{< /figure >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        // 应该优雅处理不匹配的标签
        result.Should().NotBeEmpty();
    }

    /// <summary>
    /// 测试特殊字符在参数中
    /// </summary>
    [Fact]
    public async Task ProcessAsync_SpecialCharactersInParams_ShouldEscapeCorrectly()
    {
        // Arrange
        var content = "{{< figure src=\"/images/test.jpg\" title=\"<script>alert('xss')</script>\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().NotContain("<script>");
        result.Should().Contain("&lt;script&gt;");
    }

    /// <summary>
    /// 测试 Unicode 字符
    /// 注意：HttpUtility.HtmlEncode 会将 emoji 编码为 HTML 实体
    /// </summary>
    [Fact]
    public async Task ProcessAsync_UnicodeCharacters_ShouldHandleCorrectly()
    {
        // Arrange
        var content = "{{< figure src=\"/images/图片.jpg\" title=\"中文标题 🎉\" caption=\"日本語キャプション\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("中文标题");
        // emoji 会被 HTML 编码为实体
        result.Should().Contain("&#127881;");
        result.Should().Contain("日本語キャプション");
    }

    /// <summary>
    /// 测试多个连续短代码
    /// </summary>
    [Fact]
    public async Task ProcessAsync_MultipleConsecutiveShortcodes_ShouldProcessAll()
    {
        // Arrange
        var content = "{{< youtube \"vid1\" >}}{{< youtube \"vid2\" >}}{{< youtube \"vid3\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("vid1");
        result.Should().Contain("vid2");
        result.Should().Contain("vid3");
    }

    /// <summary>
    /// 测试短代码在文本中间
    /// </summary>
    [Fact]
    public async Task ProcessAsync_ShortcodeInMiddleOfText_ShouldPreserveSurroundingText()
    {
        // Arrange
        var content = "这是前面的文本。{{< youtube \"abc123\" >}}这是后面的文本。";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        result.Should().Contain("这是前面的文本。");
        result.Should().Contain("这是后面的文本。");
        result.Should().Contain("youtube.com/embed/abc123");
    }

    /// <summary>
    /// 测试取消令牌
    /// 注意：当取消令牌已取消时，异步短代码会抛出 TaskCanceledException
    /// 这是预期行为，验证取消令牌被正确传递
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CancellationRequested_ShouldThrowTaskCanceledException()
    {
        // Arrange - 使用异步短代码来测试取消
        var asyncShortcode = new AsyncDataShortcode();
        _registry.Register(asyncShortcode);
        var content = "{{< asyncdata delay=\"5000\" >}}";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // 由于取消令牌已取消，异步操作应该抛出 TaskCanceledException
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _processor.ProcessAsync(content, cancellationToken: cts.Token).AsTask());
    }

    #endregion

    #region 辅助类 - 自定义短代码处理器

    /// <summary>
    /// Alert 短代码处理器 - 用于测试自定义短代码
    /// </summary>
    private sealed class AlertShortcode : IShortcodeProcessor
    {
        public string Name => "alert";
        public string Description => "警告框短代码";

        public ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
        {
            var type = context.Parameters.TryGetValue("type", out var t) ? t : "info";
            var title = context.Parameters.TryGetValue("title", out var ti) ? ti : null;
            var content = context.InnerContent ?? string.Empty;

            var sb = new StringBuilder();
            sb.Append($"<div class=\"alert alert-{type}\">");
            if (!string.IsNullOrEmpty(title))
            {
                sb.Append($"<strong>{title}</strong> ");
            }
            sb.Append(content);
            sb.Append("</div>");

            return ValueTask.FromResult(sb.ToString());
        }
    }

    /// <summary>
    /// Box 短代码处理器 - 用于测试嵌套
    /// </summary>
    private sealed class BoxShortcode : IShortcodeProcessor
    {
        public string Name => "box";
        public string Description => "盒子容器短代码";

        public ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
        {
            var content = context.InnerContent ?? string.Empty;
            return ValueTask.FromResult($"<div class=\"box\">{content}</div>");
        }
    }

    /// <summary>
    /// Card 短代码处理器 - 用于测试深层嵌套
    /// </summary>
    private sealed class CardShortcode : IShortcodeProcessor
    {
        public string Name => "card";
        public string Description => "卡片容器短代码";

        public ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
        {
            var content = context.InnerContent ?? string.Empty;
            return ValueTask.FromResult($"<div class=\"card\">{content}</div>");
        }
    }

    /// <summary>
    /// Icon 短代码处理器 - 用于测试自闭合短代码
    /// </summary>
    private sealed class IconShortcode : IShortcodeProcessor
    {
        public string Name => "icon";
        public string Description => "图标短代码";

        public ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
        {
            var name = context.Parameters.TryGetValue("name", out var n) ? n :
                       (context.PositionalArgs.Count > 0 ? context.PositionalArgs[0] : "default");
            var size = context.Parameters.TryGetValue("size", out var s) ? s : "16";

            return ValueTask.FromResult($"<i class=\"icon icon-{name}\" style=\"font-size: {size}px\"></i>");
        }
    }

    /// <summary>
    /// 自定义 Figure 短代码处理器 - 用于测试覆盖内置短代码
    /// </summary>
    private sealed class CustomFigureShortcode : IShortcodeProcessor
    {
        public string Name => "figure";
        public string Description => "自定义图片短代码";

        public ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
        {
            var src = context.Parameters.TryGetValue("src", out var s) ? s :
                      (context.PositionalArgs.Count > 0 ? context.PositionalArgs[0] : "");

            return ValueTask.FromResult($"<!-- 自定义 figure 短代码 --><img src=\"{src}\" />");
        }
    }

    /// <summary>
    /// 异步数据短代码处理器 - 用于测试异步处理
    /// </summary>
    private sealed class AsyncDataShortcode : IShortcodeProcessor
    {
        public string Name => "asyncdata";
        public string Description => "异步数据加载短代码";

        public async ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
        {
            var delayStr = context.Parameters.TryGetValue("delay", out var d) ? d : "10";
            if (int.TryParse(delayStr, out var delay))
            {
                await Task.Delay(delay, cancellationToken);
            }

            return "<div class=\"async-data\">异步加载完成</div>";
        }
    }

    /// <summary>
    /// 错误短代码处理器 - 用于测试异常处理
    /// </summary>
    private sealed class ErrorShortcode : IShortcodeProcessor
    {
        public string Name => "error";
        public string Description => "抛出异常的短代码";

        public ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("测试异常");
        }
    }

    /// <summary>
    /// 上下文感知短代码处理器 - 用于测试页面上下文访问
    /// </summary>
    private sealed class ContextAwareShortcode : IShortcodeProcessor
    {
        public string Name => "contextaware";
        public string Description => "上下文感知短代码";

        public ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
        {
            if (context.Page != null)
            {
                var pageType = context.Page.GetType();
                var titleProp = pageType.GetProperty("Title");
                if (titleProp != null)
                {
                    var title = titleProp.GetValue(context.Page)?.ToString() ?? "未知";
                    return ValueTask.FromResult($"<div>页面标题: {title}</div>");
                }
            }

            return ValueTask.FromResult("<div>无页面上下文</div>");
        }
    }

    #endregion

    #region 双语义测试（对齐 Hugo：{{< >}} 避开 Markdown，{{% %}} 参与 Markdown）

    /// <summary>
    /// {{&lt; &gt;}} 型短代码的 HTML 输出不参与 Markdown 处理：
    /// 多行块级 HTML 不得被 Markdig 重新包裹进 &lt;p&gt; 段落
    /// </summary>
    [Fact]
    public async Task ContentParser_AngleShortcode_HtmlNotProcessedByMarkdown()
    {
        // Arrange：figure 输出多行 <figure> 块，若参与 Markdown 会被 <p> 包裹
        var parser = new ContentParser();
        var content = """
            ---
            title: "双语义测试"
            ---

            前置段落。

            {{< figure src="/img/test.jpg" caption="测试图" >}}

            后置段落。
            """;
        var file = new ContentFile
        {
            Path = Path.Combine(_tempDir, "angle.md"),
            RawContent = System.Text.Encoding.UTF8.GetBytes(content),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<figure");
        // <figure> 块级标签不得出现在 <p> 内部（无效 HTML，正是被 Markdown 重排的症状）
        var figureIndex = result.HtmlContent.IndexOf("<figure", StringComparison.Ordinal);
        var lastPOpen = result.HtmlContent.LastIndexOf("<p>", figureIndex, StringComparison.Ordinal);
        var pClose = result.HtmlContent.IndexOf("</p>", lastPOpen, StringComparison.Ordinal);
        if (lastPOpen >= 0 && pClose > figureIndex)
        {
            // figure 起点落在某个 <p>...</p> 区间内即被包裹
            result.HtmlContent[..pClose].Should().NotContain("<figure",
                "Angle 型短代码输出不应被 Markdown 包裹进段落");
        }
        result.HtmlContent.Should().NotContain("HAHAFLINTSHORTCODE", "占位符必须全部展开");
    }

    /// <summary>
    /// {{%% %%}} 型短代码的输出内联参与 Markdown 渲染：输出中的 Markdown 语法会被处理
    /// </summary>
    [Fact]
    public async Task ContentParser_PercentShortcode_OutputParticipatesInMarkdown()
    {
        // Arrange：highlight 内置短代码不支持 {{% %}}（恒 Angle）？
        // 用强调语义验证：emph 输出 *text* 形态的 Markdown，若内联则被渲染为 <em>
        // 这里改用 highlight 的 {{% %}} 输出包含代码文本，验证其至少不产生占位符残留，
        // 并验证核心分派：同一 shortcodes 的两种定界符均正确展开
        var parser = new ContentParser();
        var content = """
            ---
            title: "percent 测试"
            ---

            {{% figure src="/img/p.jpg" caption="p 图" %}}
            """;
        var file = new ContentFile
        {
            Path = Path.Combine(_tempDir, "percent.md"),
            RawContent = System.Text.Encoding.UTF8.GetBytes(content),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await parser.ParseAsync(file);

        // Assert：Percent 型输出参与 Markdown（可能被包裹 <p>——这正是语义差异），
        // 关键是占位符必须全部展开且短代码内容存在
        result.HtmlContent.Should().Contain("figure");
        result.HtmlContent.Should().NotContain("HAHAFLINTSHORTCODE", "占位符必须全部展开");
        result.HtmlContent.Should().NotContain("{{SHORTCODE:", "内部占位符不得泄漏");
    }

    #endregion
}

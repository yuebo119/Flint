// Flint 静态站点生成器
// 短代码处理器单元测试

using Flint.Core.Abstractions;
using Flint.Core.Content.Shortcodes;
using Xunit;

namespace Flint.Core.Tests.Content;

/// <summary>
/// 短代码处理器单元测试
/// </summary>
public class ShortcodeProcessorTests
{
    private readonly ShortcodeProcessor _processor;

    public ShortcodeProcessorTests()
    {
        _processor = new ShortcodeProcessor();
    }

    #region 短代码处理测试

    [Fact]
    public async Task ProcessAsync_FigureShortcode_GeneratesHtml()
    {
        // Arrange
        var content = "{{< figure src=\"test.jpg\" title=\"测试图片\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        Assert.Contains("<figure>", result);
        Assert.Contains("<img src=\"test.jpg\"", result);
        Assert.Contains("测试图片", result);
        Assert.Contains("</figure>", result);
    }

    [Fact]
    public async Task ProcessAsync_YoutubeShortcode_GeneratesIframe()
    {
        // Arrange
        var content = "{{< youtube \"dQw4w9WgXcQ\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        Assert.Contains("<iframe", result);
        Assert.Contains("youtube.com/embed/dQw4w9WgXcQ", result);
        Assert.Contains("allowfullscreen", result);
    }

    [Fact]
    public async Task ProcessAsync_HighlightShortcode_GeneratesCodeBlock()
    {
        // Arrange
        var content = "{{< highlight go >}}fmt.Println(\"Hello\"){{< /highlight >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        Assert.Contains("<div class=\"highlight\">", result);
        Assert.Contains("<pre class=\"chroma\">", result);
        Assert.Contains("language-go", result);
        Assert.Contains("fmt.Println", result);
    }

    [Fact]
    public async Task ProcessAsync_RefShortcode_GeneratesUrl()
    {
        // Arrange
        var content = "{{< ref \"posts/my-post.md\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        Assert.Contains("/posts/my-post/", result);
    }

    [Fact]
    public async Task ProcessAsync_GistShortcode_GeneratesScript()
    {
        // Arrange
        var content = "{{< gist \"octocat\" \"abc123\" >}}";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        Assert.Contains("<script src=\"https://gist.github.com/octocat/abc123.js\"", result);
    }

    [Fact]
    public async Task ProcessAsync_UnknownShortcode_ThrowsByDefault()
    {
        // Arrange：fail-fast 语义（对齐 Hugo），未注册短代码必须让构建失败
        var content = "{{< unknown_shortcode >}}";

        // Act
        var act = async () => await _processor.ProcessAsync(content).ConfigureAwait(false);

        // Assert
        var ex = await Assert.ThrowsAsync<Flint.Core.Content.Shortcodes.ShortcodeNotFoundException>(act);
        Assert.Equal("unknown_shortcode", ex.ShortcodeName);
    }

    [Fact]
    public async Task ProcessAsync_UnknownShortcode_CommentWhenLenient()
    {
        // Arrange：宽容模式保持旧行为
        var lenient = new Flint.Core.Content.Shortcodes.ShortcodeProcessor(new Flint.Core.Content.Shortcodes.ShortcodeRegistry())
        {
            ThrowOnUnregistered = false
        };
        var content = "{{< unknown_shortcode >}}";

        // Act
        var result = await lenient.ProcessAsync(content);

        // Assert
        Assert.Contains("<!-- 未知短代码: unknown_shortcode -->", result);
    }

    [Fact]
    public async Task ProcessAsync_PreservesTextAroundShortcodes()
    {
        // Arrange
        var content = "前面的文本 {{< youtube \"abc\" >}} 后面的文本";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        Assert.Contains("前面的文本", result);
        Assert.Contains("后面的文本", result);
        Assert.Contains("youtube.com/embed/abc", result);
    }

    [Fact]
    public async Task ProcessAsync_EmptyContent_ReturnsEmpty()
    {
        // Act
        var result = await _processor.ProcessAsync(string.Empty);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ProcessAsync_NoShortcodes_ReturnsOriginal()
    {
        // Arrange
        var content = "这是一段没有短代码的普通文本";

        // Act
        var result = await _processor.ProcessAsync(content);

        // Assert
        Assert.Equal(content, result);
    }

    #endregion

    #region 注册表测试

    [Fact]
    public void Registry_ContainsBuiltinShortcodes()
    {
        // Assert
        Assert.True(_processor.Registry.Contains("figure"));
        Assert.True(_processor.Registry.Contains("highlight"));
        Assert.True(_processor.Registry.Contains("ref"));
        Assert.True(_processor.Registry.Contains("relref"));
        Assert.True(_processor.Registry.Contains("gist"));
        Assert.True(_processor.Registry.Contains("youtube"));
        Assert.True(_processor.Registry.Contains("tweet"));
        Assert.True(_processor.Registry.Contains("vimeo"));
        Assert.True(_processor.Registry.Contains("instagram"));
        Assert.True(_processor.Registry.Contains("param"));
    }

    [Fact]
    public void Registry_RegisterCustomShortcode_Works()
    {
        // Arrange
        var customProcessor = new TestShortcodeProcessor();

        // Act
        _processor.Registry.Register(customProcessor);

        // Assert
        Assert.True(_processor.Registry.Contains("test"));
        Assert.Equal(customProcessor, _processor.Registry.Get("test"));
    }

    #endregion

    /// <summary>
    /// 测试用短代码处理器
    /// </summary>
    private sealed class TestShortcodeProcessor : IShortcodeProcessor
    {
        public string Name => "test";
        public string Description => "测试短代码";

        public ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult("<div>测试输出</div>");
        }
    }
}

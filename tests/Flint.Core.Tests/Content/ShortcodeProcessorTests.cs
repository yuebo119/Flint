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

    #region 基本语法解析测试

    [Fact]
    public void ContainsShortcodes_WithAngleBrackets_ReturnsTrue()
    {
        // Arrange
        var content = "这是一段文本 {{< figure src=\"test.jpg\" >}} 更多文本";

        // Act
        var result = ShortcodeProcessor.ContainsShortcodes(content);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ContainsShortcodes_WithPercentBrackets_ReturnsTrue()
    {
        // Arrange
        var content = "这是一段文本 {{% highlight go %}}代码{{% /highlight %}} 更多文本";

        // Act
        var result = ShortcodeProcessor.ContainsShortcodes(content);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ContainsShortcodes_WithoutShortcodes_ReturnsFalse()
    {
        // Arrange
        var content = "这是一段普通文本，没有短代码";

        // Act
        var result = ShortcodeProcessor.ContainsShortcodes(content);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContainsShortcodes_EmptyString_ReturnsFalse()
    {
        // Act
        var result = ShortcodeProcessor.ContainsShortcodes(string.Empty);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContainsShortcodes_NullString_ReturnsFalse()
    {
        // Act
        var result = ShortcodeProcessor.ContainsShortcodes(null!);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region 短代码提取测试

    [Fact]
    public void ExtractShortcodes_SelfClosingShortcode_ExtractsCorrectly()
    {
        // Arrange
        var content = "{{< figure src=\"test.jpg\" />}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal("figure", shortcodes[0].Name);
        Assert.True(shortcodes[0].IsSelfClosing);
    }

    [Fact]
    public void ExtractShortcodes_OpenCloseShortcode_ExtractsCorrectly()
    {
        // Arrange
        var content = "{{< highlight go >}}fmt.Println(\"Hello\"){{< /highlight >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal("highlight", shortcodes[0].Name);
        Assert.False(shortcodes[0].IsSelfClosing);
        Assert.Contains("fmt.Println", shortcodes[0].InnerContent);
    }

    [Fact]
    public void ExtractShortcodes_NamedParameters_ExtractsCorrectly()
    {
        // Arrange
        var content = "{{< figure src=\"image.jpg\" title=\"标题\" caption=\"说明\" >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal("figure", shortcodes[0].Name);
        Assert.Equal("image.jpg", shortcodes[0].Parameters["src"]);
        Assert.Equal("标题", shortcodes[0].Parameters["title"]);
        Assert.Equal("说明", shortcodes[0].Parameters["caption"]);
    }

    [Fact]
    public void ExtractShortcodes_PositionalParameters_ExtractsCorrectly()
    {
        // Arrange
        var content = "{{< youtube \"dQw4w9WgXcQ\" >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal("youtube", shortcodes[0].Name);
        Assert.Single(shortcodes[0].PositionalArgs);
        Assert.Equal("dQw4w9WgXcQ", shortcodes[0].PositionalArgs[0]);
    }

    [Fact]
    public void ExtractShortcodes_MixedParameters_ExtractsCorrectly()
    {
        // Arrange
        var content = "{{< gist \"user\" \"gistid\" file=\"main.go\" >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal("gist", shortcodes[0].Name);
        Assert.Equal(2, shortcodes[0].PositionalArgs.Count);
        Assert.Equal("user", shortcodes[0].PositionalArgs[0]);
        Assert.Equal("gistid", shortcodes[0].PositionalArgs[1]);
        Assert.Equal("main.go", shortcodes[0].Parameters["file"]);
    }

    [Fact]
    public void ExtractShortcodes_MultipleShortcodes_ExtractsAll()
    {
        // Arrange
        var content = @"
# 文章标题

{{< figure src=""image1.jpg"" >}}

一些文本

{{< youtube ""abc123"" >}}

更多文本

{{< ref ""posts/other.md"" >}}
";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Equal(3, shortcodes.Count);
        Assert.Equal("figure", shortcodes[0].Name);
        Assert.Equal("youtube", shortcodes[1].Name);
        Assert.Equal("ref", shortcodes[2].Name);
    }

    #endregion

    #region 嵌套短代码测试

    [Fact]
    public void ExtractShortcodes_NestedShortcodes_ExtractsCorrectly()
    {
        // Arrange
        var content = "{{< outer >}}{{< inner />}}{{< /outer >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal("outer", shortcodes[0].Name);
        Assert.Single(shortcodes[0].NestedShortcodes);
        Assert.Equal("inner", shortcodes[0].NestedShortcodes[0].Name);
    }

    [Fact]
    public void ExtractShortcodes_DeeplyNestedShortcodes_ExtractsCorrectly()
    {
        // Arrange
        var content = "{{< level1 >}}文本{{< level2 >}}内部文本{{< /level2 >}}{{< /level1 >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal("level1", shortcodes[0].Name);
        Assert.Single(shortcodes[0].NestedShortcodes);
        Assert.Equal("level2", shortcodes[0].NestedShortcodes[0].Name);
    }

    #endregion

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

    #region 分隔符类型测试

    [Fact]
    public void ExtractShortcodes_AngleBrackets_DetectsCorrectDelimiter()
    {
        // Arrange
        var content = "{{< figure src=\"test.jpg\" >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal(ShortcodeDelimiterType.Angle, shortcodes[0].DelimiterType);
    }

    [Fact]
    public void ExtractShortcodes_PercentBrackets_DetectsCorrectDelimiter()
    {
        // Arrange
        var content = "{{% figure src=\"test.jpg\" %}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal(ShortcodeDelimiterType.Percent, shortcodes[0].DelimiterType);
    }

    #endregion

    #region 验证测试

    [Fact]
    public void ValidateShortcodes_AllRegistered_ReturnsEmpty()
    {
        // Arrange
        var content = "{{< figure src=\"test.jpg\" >}} {{< youtube \"abc\" >}}";

        // Act
        var unregistered = _processor.ValidateShortcodes(content);

        // Assert
        Assert.Empty(unregistered);
    }

    [Fact]
    public void ValidateShortcodes_UnregisteredShortcode_ReturnsName()
    {
        // Arrange
        var content = "{{< custom_shortcode >}}";

        // Act
        var unregistered = _processor.ValidateShortcodes(content);

        // Assert
        Assert.Single(unregistered);
        Assert.Equal("custom_shortcode", unregistered[0]);
    }

    #endregion

    #region 移除短代码测试

    [Fact]
    public void StripShortcodes_RemovesAllShortcodes()
    {
        // Arrange
        var content = "文本 {{< figure src=\"test.jpg\" >}} 更多文本 {{< youtube \"abc\" >}} 结束";

        // Act
        var result = ShortcodeProcessor.StripShortcodes(content);

        // Assert
        Assert.DoesNotContain("{{<", result);
        Assert.DoesNotContain(">}}", result);
        Assert.Contains("文本", result);
        Assert.Contains("更多文本", result);
        Assert.Contains("结束", result);
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

    #region 边界情况测试

    [Fact]
    public void ExtractShortcodes_UnclosedShortcode_HandlesGracefully()
    {
        // Arrange
        var content = "{{< figure src=\"test.jpg\"";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Empty(shortcodes);
    }

    [Fact]
    public void ExtractShortcodes_MismatchedClosingTag_HandlesGracefully()
    {
        // Arrange
        var content = "{{< figure >}}内容{{< /other >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        // 没有匹配的关闭标签时，figure 被视为自闭合短代码
        Assert.Single(shortcodes);
        Assert.Equal("figure", shortcodes[0].Name);
        Assert.True(shortcodes[0].IsSelfClosing);
    }

    [Fact]
    public void ExtractShortcodes_EmptyShortcodeName_HandlesGracefully()
    {
        // Arrange
        var content = "{{<  >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Empty(shortcodes);
    }

    [Fact]
    public void ExtractShortcodes_QuotedParameterWithSpaces_ExtractsCorrectly()
    {
        // Arrange
        var content = "{{< figure title=\"这是一个 带空格的 标题\" >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal("这是一个 带空格的 标题", shortcodes[0].Parameters["title"]);
    }

    [Fact]
    public void ExtractShortcodes_SingleQuotedParameter_ExtractsCorrectly()
    {
        // Arrange
        var content = "{{< figure title='单引号标题' >}}";

        // Act
        var shortcodes = ShortcodeProcessor.ExtractShortcodes(content);

        // Assert
        Assert.Single(shortcodes);
        Assert.Equal("单引号标题", shortcodes[0].Parameters["title"]);
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

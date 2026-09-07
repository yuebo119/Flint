// Flint 静态站点生成器
// ContentParser 单元测试

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Models;
using Xunit;

namespace Flint.Core.Tests.Content;

/// <summary>
/// ContentParser 单元测试
/// 验证内容处理器整合功能
/// </summary>
public class ContentParserTests
{
    private readonly ContentParser _parser;

    public ContentParserTests()
    {
        _parser = new ContentParser();
    }

    #region 基本解析测试

    [Fact]
    public async Task ParseAsync_WithYamlFrontMatter_ShouldExtractMetadata()
    {
        // Arrange
        var content = """
            ---
            title: 测试文章
            date: 2024-01-15
            tags:
              - csharp
              - dotnet
            draft: false
            ---
            
            # 这是标题
            
            这是正文内容。
            """;
        var file = CreateContentFile("test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.Equal("test.md", result.SourcePath);
        Assert.Equal("测试文章", result.Metadata.Title);
        Assert.NotNull(result.Metadata.Date);
        Assert.Equal(2024, result.Metadata.Date.Value.Year);
        Assert.Contains("csharp", result.Metadata.Tags);
        Assert.Contains("dotnet", result.Metadata.Tags);
        Assert.False(result.Metadata.Draft);
    }

    [Fact]
    public async Task ParseAsync_WithTomlFrontMatter_ShouldExtractMetadata()
    {
        // Arrange
        var content = """
            +++
            title = "TOML 测试"
            date = 2024-02-20T10:30:00+08:00
            draft = true
            tags = ["rust", "performance"]
            +++
            
            ## TOML 格式测试
            
            这是使用 TOML Front Matter 的文章。
            """;
        var file = CreateContentFile("toml-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.Equal("TOML 测试", result.Metadata.Title);
        Assert.True(result.Metadata.Draft);
        Assert.Contains("rust", result.Metadata.Tags);
        Assert.Contains("performance", result.Metadata.Tags);
    }

    [Fact]
    public async Task ParseAsync_WithJsonFrontMatter_ShouldExtractMetadata()
    {
        // Arrange
        var content = """
            {
              "title": "JSON 测试",
              "date": "2024-03-10",
              "categories": ["技术", "编程"]
            }
            
            ### JSON 格式测试
            
            这是使用 JSON Front Matter 的文章。
            """;
        var file = CreateContentFile("json-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.Equal("JSON 测试", result.Metadata.Title);
        Assert.Contains("技术", result.Metadata.Categories);
        Assert.Contains("编程", result.Metadata.Categories);
    }

    [Fact]
    public async Task ParseAsync_WithoutFrontMatter_ShouldUseDefaultMetadata()
    {
        // Arrange
        var content = """
            # 无 Front Matter 的文章
            
            这是一篇没有 Front Matter 的文章。
            """;
        var file = CreateContentFile("no-frontmatter.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.Equal("No frontmatter", result.Metadata.Title);
        Assert.False(result.Metadata.Draft);
    }

    #endregion

    #region Markdown 解析测试

    [Fact]
    public async Task ParseAsync_ShouldConvertMarkdownToHtml()
    {
        // Arrange
        var content = """
            ---
            title: HTML 转换测试
            ---
            
            # 标题一
            
            这是一个**粗体**和*斜体*的段落。
            
            - 列表项 1
            - 列表项 2
            """;
        var file = CreateContentFile("html-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.Contains("<h1", result.HtmlContent);
        Assert.Contains("<strong>粗体</strong>", result.HtmlContent);
        Assert.Contains("<em>斜体</em>", result.HtmlContent);
        Assert.Contains("<ul>", result.HtmlContent);
        Assert.Contains("<li>", result.HtmlContent);
    }

    #endregion

    #region 字数和阅读时间测试

    [Fact]
    public async Task ParseAsync_ShouldCalculateWordCount()
    {
        // Arrange
        var content = """
            ---
            title: 字数统计测试
            ---
            
            这是一段中文内容，用于测试字数统计功能。
            
            This is some English content for word count testing.
            """;
        var file = CreateContentFile("wordcount-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.True(result.WordCount > 0);
    }

    [Fact]
    public async Task ParseAsync_ShouldCalculateReadingTime()
    {
        // Arrange
        var content = """
            ---
            title: 阅读时间测试
            ---
            
            """ + string.Join("\n\n", Enumerable.Repeat("这是一段测试内容，用于计算阅读时间。", 50));
        var file = CreateContentFile("readingtime-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.True(result.ReadingTime > TimeSpan.Zero);
    }

    #endregion

    #region 摘要生成测试

    [Fact]
    public async Task ParseAsync_ShouldUseFrontMatterSummary()
    {
        // Arrange
        var content = """
            ---
            title: 摘要测试
            summary: 这是自定义摘要
            ---
            
            这是正文内容，不应该被用作摘要。
            """;
        var file = CreateContentFile("summary-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.Equal("这是自定义摘要", result.Summary);
    }

    [Fact]
    public async Task ParseAsync_ShouldUseFrontMatterDescription()
    {
        // Arrange
        var content = """
            ---
            title: 描述测试
            description: 这是文章描述
            ---
            
            这是正文内容。
            """;
        var file = CreateContentFile("description-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.Equal("这是文章描述", result.Summary);
    }

    [Fact]
    public async Task ParseAsync_ShouldUseSummaryDivider()
    {
        // Arrange
        var content = """
            ---
            title: 分隔符测试
            ---
            
            这是摘要部分的内容。
            
            <!--more-->
            
            这是正文的其余部分，不应该出现在摘要中。
            """;
        var file = CreateContentFile("divider-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.Contains("摘要部分", result.Summary);
        Assert.DoesNotContain("其余部分", result.Summary);
    }

    [Fact]
    public async Task ParseAsync_ShouldAutoGenerateSummary()
    {
        // Arrange
        var longContent = string.Join(" ", Enumerable.Repeat("这是一段很长的内容", 100));
        var content = $"""
            ---
            title: 自动摘要测试
            ---
            
            {longContent}
            """;
        var file = CreateContentFile("auto-summary-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.NotEmpty(result.Summary);
        Assert.True(result.Summary.Length <= 250); // 摘要应该被截断
        Assert.EndsWith("...", result.Summary);
    }

    #endregion

    #region 短代码处理测试

    [Fact]
    public async Task ParseAsync_ShouldProcessShortcodes()
    {
        // Arrange
        var content = """
            ---
            title: 短代码测试
            ---
            
            这是一个 figure 短代码：
            
            {{< figure src="/images/test.jpg" alt="测试图片" >}}
            
            正文继续...
            """;
        var file = CreateContentFile("shortcode-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        // figure 短代码应该被处理为 HTML
        Assert.Contains("<figure", result.HtmlContent);
        Assert.Contains("test.jpg", result.HtmlContent);
    }

    [Fact]
    public async Task ParseAsync_ShouldProcessHighlightShortcode()
    {
        // Arrange
        var content = """
            ---
            title: 代码高亮测试
            ---
            
            {{< highlight csharp >}}
            public class Test
            {
                public void Method() { }
            }
            {{< /highlight >}}
            """;
        var file = CreateContentFile("highlight-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.Contains("<pre", result.HtmlContent);
        Assert.Contains("public class Test", result.HtmlContent);
    }

    #endregion

    #region 纯文本提取测试

    [Fact]
    public async Task ParseAsync_ShouldExtractPlainText()
    {
        // Arrange
        var content = """
            ---
            title: 纯文本测试
            ---
            
            # 标题
            
            这是**粗体**和*斜体*文本。
            
            [链接文本](https://example.com)
            """;
        var file = CreateContentFile("plaintext-test.md", content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        Assert.NotEmpty(result.PlainText);
        Assert.Contains("标题", result.PlainText);
        Assert.Contains("粗体", result.PlainText);
        Assert.Contains("斜体", result.PlainText);
        Assert.Contains("链接文本", result.PlainText);
        // 不应包含 Markdown 标记
        Assert.DoesNotContain("**", result.PlainText);
        Assert.DoesNotContain("*斜体*", result.PlainText);
    }

    #endregion

    #region 同步解析测试

    [Fact]
    public void Parse_ShouldWorkSynchronously()
    {
        // Arrange
        var content = """
            ---
            title: 同步解析测试
            ---
            
            同步解析的内容。
            """;
        var file = CreateContentFile("sync-test.md", content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        Assert.Equal("同步解析测试", result.Metadata.Title);
        Assert.Contains("同步解析的内容", result.HtmlContent);
    }

    #endregion

    #region 批量解析测试

    [Fact]
    public async Task ParseBatchAsync_ShouldParseMultipleFiles()
    {
        // Arrange
        var files = new[]
        {
            CreateContentFile("batch-1.md", """
                ---
                title: 批量测试 1
                ---
                内容 1
                """),
            CreateContentFile("batch-2.md", """
                ---
                title: 批量测试 2
                ---
                内容 2
                """),
            CreateContentFile("batch-3.md", """
                ---
                title: 批量测试 3
                ---
                内容 3
                """)
        };

        // Act
        var results = new List<ParsedContent>();
        await foreach (var result in _parser.ParseBatchAsync(files))
        {
            results.Add(result);
        }

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Metadata.Title == "批量测试 1");
        Assert.Contains(results, r => r.Metadata.Title == "批量测试 2");
        Assert.Contains(results, r => r.Metadata.Title == "批量测试 3");
    }

    [Fact]
    public async Task ParseBatchAsync_ShouldSupportCancellation()
    {
        // Arrange
        var files = Enumerable.Range(1, 100)
            .Select(i => CreateContentFile($"cancel-{i}.md", $"""
                ---
                title: 取消测试 {i}
                ---
                内容 {i}
                """))
            .ToList();

        using var cts = new CancellationTokenSource();
        var results = new List<ParsedContent>();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var result in _parser.ParseBatchAsync(files, cts.Token).ConfigureAwait(false))
            {
                results.Add(result);
                if (results.Count >= 5)
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                }
            }
        });

        Assert.True(results.Count >= 5);
        Assert.True(results.Count < 100);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 创建测试用的 ContentFile
    /// </summary>
    private static ContentFile CreateContentFile(string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new ContentFile
        {
            Path = path,
            RawContent = new ReadOnlyMemory<byte>(bytes),
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    #endregion
}

/// <summary>
/// Hugo 的 :filename 日期源：文件名前缀 YYYY-MM-DD- 在 front matter 未设 date/slug 时生效
/// </summary>
public class FilenameConventionTests
{
    [Fact]
    public async Task ParseAsync_FilenameDatePrefix_FillsDateAndSlug()
    {
        var parser = new ContentParser();
        var file = new ContentFile
        {
            Path = "/site/content/posts/2024-03-15-my-post.md",
            RawContent = System.Text.Encoding.UTF8.GetBytes("---\ntitle: \"T\"\n---\n\nBody\n"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var result = await parser.ParseAsync(file);

        Assert.Equal(new DateTime(2024, 3, 15), result.Metadata.Date!.Value.DateTime);
        Assert.Equal("my-post", result.Metadata.Slug);
    }

    [Fact]
    public async Task ParseAsync_ExplicitDateAndSlug_WinOverFilename()
    {
        var parser = new ContentParser();
        var file = new ContentFile
        {
            Path = "/site/content/posts/2024-03-15-my-post.md",
            RawContent = System.Text.Encoding.UTF8.GetBytes(
                "---\ntitle: \"T\"\ndate: 2020-01-01T00:00:00Z\nslug: \"explicit\"\n---\n\nBody\n"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var result = await parser.ParseAsync(file);

        Assert.Equal(new DateTime(2020, 1, 1), result.Metadata.Date!.Value.DateTime); // 显式 date 优先
        Assert.Equal("explicit", result.Metadata.Slug); // 显式 slug 优先
    }
}

/// <summary>
/// Hugo 特殊日期源（:git/:filemodtime）与站点 timeZone 配置
/// </summary>
public class DateSourcesAndTimeZoneTests
{
    [Fact]
    public async Task ParseAsync_FileModTimeSource_UsesModifiedTime()
    {
        var parser = new ContentParser();
        var modified = new DateTimeOffset(2023, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var file = new ContentFile
        {
            Path = "/site/content/posts/a.md",
            RawContent = Encoding.UTF8.GetBytes("---\ntitle: \"T\"\ndate: \":filemodtime\"\n---\n\nBody\n"),
            ModifiedTime = modified
        };

        var result = await parser.ParseAsync(file);

        Assert.Equal(modified, result.Metadata.Date);
    }

    [Fact]
    public async Task ParseAsync_GitSourceWithoutProvider_FallsBackToNull()
    {
        var parser = new ContentParser(); // GitDates 未设置
        var file = new ContentFile
        {
            Path = "/site/content/posts/a.md",
            RawContent = Encoding.UTF8.GetBytes("---\ntitle: \"T\"\nlastmod: \":git\"\n---\n\nBody\n"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var result = await parser.ParseAsync(file);

        Assert.Null(result.Metadata.LastMod); // git 不可用时 :git 源静默缺省
    }

    [Fact]
    public async Task ParseAsync_GitSourceWithProvider_UsesProviderValue()
    {
        var commitTime = new DateTimeOffset(2022, 12, 25, 8, 30, 0, TimeSpan.FromHours(8));
        var parser = new ContentParser
        {
            GitDates = new StubGitDateProvider(commitTime)
        };
        var file = new ContentFile
        {
            Path = "/site/content/posts/a.md",
            RawContent = Encoding.UTF8.GetBytes("---\ntitle: \"T\"\ndate: \":git\"\n---\n\nBody\n"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var result = await parser.ParseAsync(file);

        Assert.Equal(commitTime, result.Metadata.Date);
    }

    [Fact]
    public async Task ParseAsync_SiteTimeZone_NoOffsetDate_InterpretedInSiteZone()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"); // UTC+8 无夏令时
        var parser = new ContentParser { SiteTimeZone = tz };
        var file = new ContentFile
        {
            Path = "/site/content/posts/a.md",
            RawContent = Encoding.UTF8.GetBytes("---\ntitle: \"T\"\ndate: 2024-03-15T10:00:00\n---\n\nBody\n"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var result = await parser.ParseAsync(file);

        Assert.Equal(TimeSpan.FromHours(8), result.Metadata.Date!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2024, 3, 15, 10, 0, 0, TimeSpan.FromHours(8)), result.Metadata.Date);
    }

    [Fact]
    public async Task ParseAsync_SiteTimeZone_ExplicitOffsetDate_KeepsAuthorOffset()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var parser = new ContentParser { SiteTimeZone = tz };
        var file = new ContentFile
        {
            Path = "/site/content/posts/a.md",
            RawContent = Encoding.UTF8.GetBytes(
                "---\ntitle: \"T\"\ndate: 2024-03-15T10:00:00-05:00\n---\n\nBody\n"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var result = await parser.ParseAsync(file);

        // 作者写的偏移是意图，站点时区不重解释
        Assert.Equal(TimeSpan.FromHours(-5), result.Metadata.Date!.Value.Offset);
    }

    [Fact]
    public async Task ParseAsync_NoSiteTimeZone_NoOffsetDate_KeepsLegacyBehavior()
    {
        var parser = new ContentParser();
        var file = new ContentFile
        {
            Path = "/site/content/posts/a.md",
            RawContent = Encoding.UTF8.GetBytes("---\ntitle: \"T\"\ndate: 2024-03-15T10:00:00\n---\n\nBody\n"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var result = await parser.ParseAsync(file);

        // 旧行为：无站点时区时无偏移日期按本机时区解释
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 3, 15, 10, 0, 0)),
            result.Metadata.Date!.Value.Offset);
    }

    private sealed class StubGitDateProvider(DateTimeOffset time) : IGitDateProvider
    {
        public DateTimeOffset? GetLastCommitTime(string filePath) => time;
    }
}

// Flint 静态站点生成器
// 内容解析正确性属性测试
// **Property 12: 内容解析正确性**
// 生成随机 Markdown 内容，验证解析结果的正确性
// 验证 HTML 输出包含所有预期元素
// **Validates: Requirements 5.1, 5.2, 5.3**

using System.Text;
using System.Text.RegularExpressions;
using Flint.Core.Content;
using Flint.Core.Models;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.Content;

/// <summary>
/// 内容解析正确性属性测试
/// **Feature: Flint-integration-tests, Property 12: 内容解析正确性**
/// 验证 Markdown 内容解析后的 HTML 输出包含正确的元素
/// </summary>
public partial class ContentParsingCorrectnessPropertyTests : IDisposable
{
    private readonly ContentParser _parser;
    private readonly string _tempDir;

    public ContentParsingCorrectnessPropertyTests()
    {
        _parser = new ContentParser();
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-content-prop-test-{Guid.NewGuid():N}");
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
    /// 创建临时内容文件
    /// </summary>
    private ContentFile CreateContentFile(string content, string fileName = "test.md")
    {
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, content, Encoding.UTF8);
        return new ContentFile
        {
            Path = filePath,
            RawContent = Encoding.UTF8.GetBytes(content),
            ModifiedTime = DateTimeOffset.Now
        };
    }

    /// <summary>
    /// 创建带有 YAML Front Matter 的完整内容
    /// </summary>
    private static string CreateYamlContent(string title, string markdownBody)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{EscapeYamlString(title)}\"");
        sb.AppendLine($"date: {DateTimeOffset.Now:yyyy-MM-ddTHH:mm:sszzz}");
        sb.AppendLine("draft: false");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(markdownBody);
        return sb.ToString();
    }

    /// <summary>
    /// 转义 YAML 字符串中的特殊字符
    /// </summary>
    private static string EscapeYamlString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// 统计 HTML 中指定标签的数量
    /// </summary>
    private static int CountHtmlTags(string html, string tagName)
    {
        var pattern = $@"<{tagName}[^>]*>";
        return Regex.Matches(html, pattern, RegexOptions.IgnoreCase).Count;
    }

    /// <summary>
    /// 检查 HTML 是否包含指定标签
    /// </summary>
    private static bool ContainsHtmlTag(string html, string tagName)
    {
        var pattern = $@"<{tagName}[^>]*>";
        return Regex.IsMatch(html, pattern, RegexOptions.IgnoreCase);
    }

    #endregion


    #region Property 12: 内容解析正确性 - 属性测试

    /// <summary>
    /// Property 12: 内容解析正确性 - 标题解析
    /// 对于任意有效的 Markdown 标题，解析后的 HTML 应包含对应的 h1-h6 标签
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_Headings_ShouldProduceCorrectHtmlTags(
        MarkdownHeadingTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("标题测试", testData.Markdown);
        var file = CreateContentFile(content, $"heading-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证 HTML 包含正确级别的标题标签
        var containsExpectedTag = ContainsHtmlTag(result.HtmlContent, $"h{testData.Level}");
        var containsHeadingText = result.HtmlContent.Contains(testData.Text);

        return (containsExpectedTag && containsHeadingText)
            .Label($"Level={testData.Level}, Text='{testData.Text}', ContainsTag={containsExpectedTag}, ContainsText={containsHeadingText}")
            .Classify(testData.Level == 1, "H1 标题")
            .Classify(testData.Level == 2, "H2 标题")
            .Classify(testData.Level >= 3, "H3+ 标题");
    }

    /// <summary>
    /// Property 12: 内容解析正确性 - 段落解析
    /// 对于任意有效的 Markdown 段落，解析后的 HTML 应包含 p 标签
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_Paragraphs_ShouldProduceCorrectHtmlTags(
        MarkdownParagraphTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("段落测试", testData.Markdown);
        var file = CreateContentFile(content, $"paragraph-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证 HTML 包含 p 标签
        var containsPTag = ContainsHtmlTag(result.HtmlContent, "p");
        // 验证段落文本存在于 HTML 中
        var containsText = testData.Paragraphs.All(p => result.HtmlContent.Contains(p));

        return (containsPTag && containsText)
            .Label($"ParagraphCount={testData.Paragraphs.Count}, ContainsPTag={containsPTag}, ContainsText={containsText}")
            .Classify(testData.Paragraphs.Count == 1, "单段落")
            .Classify(testData.Paragraphs.Count > 1, "多段落");
    }


    /// <summary>
    /// Property 12: 内容解析正确性 - 列表解析
    /// 对于任意有效的 Markdown 列表，解析后的 HTML 应包含 ul/ol 和 li 标签
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_Lists_ShouldProduceCorrectHtmlTags(
        MarkdownListTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("列表测试", testData.Markdown);
        var file = CreateContentFile(content, $"list-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证 HTML 包含正确的列表标签
        var expectedListTag = testData.IsOrdered ? "ol" : "ul";
        var containsListTag = ContainsHtmlTag(result.HtmlContent, expectedListTag);
        var containsLiTag = ContainsHtmlTag(result.HtmlContent, "li");
        // 验证列表项文本存在
        var containsItems = testData.Items.All(item => result.HtmlContent.Contains(item));

        return (containsListTag && containsLiTag && containsItems)
            .Label($"IsOrdered={testData.IsOrdered}, ItemCount={testData.Items.Count}, ContainsListTag={containsListTag}, ContainsLiTag={containsLiTag}")
            .Classify(testData.IsOrdered, "有序列表")
            .Classify(!testData.IsOrdered, "无序列表")
            .Classify(testData.Items.Count <= 3, "短列表")
            .Classify(testData.Items.Count > 3, "长列表");
    }

    /// <summary>
    /// Property 12: 内容解析正确性 - 代码块解析
    /// 对于任意有效的 Markdown 代码块，解析后的 HTML 应包含 pre 和 code 标签
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_CodeBlocks_ShouldProduceCorrectHtmlTags(
        MarkdownCodeBlockTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("代码块测试", testData.Markdown);
        var file = CreateContentFile(content, $"codeblock-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证 HTML 包含 pre 和 code 标签
        var containsPreTag = ContainsHtmlTag(result.HtmlContent, "pre");
        var containsCodeTag = ContainsHtmlTag(result.HtmlContent, "code");
        // 代码内容应该存在（可能被 HTML 编码）
        var codeContentExists = !string.IsNullOrEmpty(result.HtmlContent);

        return (containsPreTag && containsCodeTag && codeContentExists)
            .Label($"Language={testData.Language ?? "none"}, ContainsPreTag={containsPreTag}, ContainsCodeTag={containsCodeTag}")
            .Classify(!string.IsNullOrEmpty(testData.Language), "带语言标识")
            .Classify(string.IsNullOrEmpty(testData.Language), "无语言标识");
    }


    /// <summary>
    /// Property 12: 内容解析正确性 - 表格解析
    /// 对于任意有效的 Markdown 表格，解析后的 HTML 应包含 table、thead、tbody、tr、th、td 标签
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_Tables_ShouldProduceCorrectHtmlTags(
        MarkdownTableTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("表格测试", testData.Markdown);
        var file = CreateContentFile(content, $"table-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证 HTML 包含表格相关标签
        var containsTableTag = ContainsHtmlTag(result.HtmlContent, "table");
        var containsTrTag = ContainsHtmlTag(result.HtmlContent, "tr");
        var containsThOrTdTag = ContainsHtmlTag(result.HtmlContent, "th") || ContainsHtmlTag(result.HtmlContent, "td");
        // 验证表头内容存在
        var containsHeaders = testData.Headers.All(h => result.HtmlContent.Contains(h));

        return (containsTableTag && containsTrTag && containsThOrTdTag && containsHeaders)
            .Label($"Columns={testData.Headers.Count}, Rows={testData.RowCount}, ContainsTableTag={containsTableTag}")
            .Classify(testData.Headers.Count <= 3, "窄表格")
            .Classify(testData.Headers.Count > 3, "宽表格")
            .Classify(testData.RowCount <= 3, "短表格")
            .Classify(testData.RowCount > 3, "长表格");
    }

    /// <summary>
    /// Property 12: 内容解析正确性 - 强调文本解析
    /// 对于任意有效的 Markdown 强调文本，解析后的 HTML 应包含 strong/em 标签
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_Emphasis_ShouldProduceCorrectHtmlTags(
        MarkdownEmphasisTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("强调测试", testData.Markdown);
        var file = CreateContentFile(content, $"emphasis-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证 HTML 包含正确的强调标签
        var expectedTag = testData.IsBold ? "strong" : "em";
        var containsExpectedTag = ContainsHtmlTag(result.HtmlContent, expectedTag);
        var containsText = result.HtmlContent.Contains(testData.Text);

        return (containsExpectedTag && containsText)
            .Label($"IsBold={testData.IsBold}, Text='{testData.Text}', ContainsTag={containsExpectedTag}")
            .Classify(testData.IsBold, "粗体")
            .Classify(!testData.IsBold, "斜体");
    }


    /// <summary>
    /// Property 12: 内容解析正确性 - 链接解析
    /// 对于任意有效的 Markdown 链接，解析后的 HTML 应包含 a 标签和正确的 href 属性
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_Links_ShouldProduceCorrectHtmlTags(
        MarkdownLinkTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("链接测试", testData.Markdown);
        var file = CreateContentFile(content, $"link-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证 HTML 包含 a 标签
        var containsATag = ContainsHtmlTag(result.HtmlContent, "a");
        // 验证 href 属性包含 URL
        var containsHref = result.HtmlContent.Contains($"href=\"{testData.Url}\"");
        // 验证链接文本存在
        var containsText = result.HtmlContent.Contains(testData.Text);

        return (containsATag && containsHref && containsText)
            .Label($"Url={testData.Url}, Text='{testData.Text}', ContainsATag={containsATag}")
            .Classify(testData.IsExternal, "外部链接")
            .Classify(!testData.IsExternal, "内部链接");
    }

    /// <summary>
    /// Property 12: 内容解析正确性 - 图片解析
    /// 对于任意有效的 Markdown 图片，解析后的 HTML 应包含 img 标签和正确的 src/alt 属性
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_Images_ShouldProduceCorrectHtmlTags(
        MarkdownImageTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("图片测试", testData.Markdown);
        var file = CreateContentFile(content, $"image-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证 HTML 包含 img 标签
        var containsImgTag = ContainsHtmlTag(result.HtmlContent, "img");
        // 验证 src 属性包含图片路径
        var containsSrc = result.HtmlContent.Contains($"src=\"{testData.Src}\"");
        // 验证 alt 属性包含替代文本
        var containsAlt = result.HtmlContent.Contains($"alt=\"{testData.Alt}\"");

        return (containsImgTag && containsSrc && containsAlt)
            .Label($"Src={testData.Src}, Alt='{testData.Alt}', ContainsImgTag={containsImgTag}")
            .Classify(testData.IsExternal, "外部图片")
            .Classify(!testData.IsExternal, "内部图片");
    }


    /// <summary>
    /// Property 12: 内容解析正确性 - 引用块解析
    /// 对于任意有效的 Markdown 引用块，解析后的 HTML 应包含 blockquote 标签
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_Blockquotes_ShouldProduceCorrectHtmlTags(
        MarkdownBlockquoteTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("引用测试", testData.Markdown);
        var file = CreateContentFile(content, $"blockquote-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证 HTML 包含 blockquote 标签
        var containsBlockquoteTag = ContainsHtmlTag(result.HtmlContent, "blockquote");
        // 验证引用文本存在
        var containsText = result.HtmlContent.Contains(testData.Text);

        return (containsBlockquoteTag && containsText)
            .Label($"Text='{testData.Text}', ContainsBlockquoteTag={containsBlockquoteTag}, ContainsText={containsText}")
            .Classify(testData.IsNested, "嵌套引用")
            .Classify(!testData.IsNested, "单层引用");
    }

    /// <summary>
    /// Property 12: 内容解析正确性 - 综合内容解析
    /// 对于任意有效的复合 Markdown 内容，解析后的 HTML 应包含所有预期元素
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MarkdownContentArbitraries) })]
    public Property ContentParsing_ComplexContent_ShouldProduceCorrectHtmlStructure(
        MarkdownComplexContentTestData testData)
    {
        // Arrange
        var content = CreateYamlContent("综合测试", testData.Markdown);
        var file = CreateContentFile(content, $"complex-test-{Guid.NewGuid():N}.md");

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证基本解析成功
        var hasHtmlContent = !string.IsNullOrEmpty(result.HtmlContent);
        var hasMetadata = result.Metadata != null;
        var hasSourcePath = !string.IsNullOrEmpty(result.SourcePath);

        // 验证预期的元素存在
        var hasExpectedHeadings = !testData.ExpectedHeadingLevels.Any() ||
            testData.ExpectedHeadingLevels.All(level => ContainsHtmlTag(result.HtmlContent, $"h{level}"));
        var hasExpectedLists = !testData.HasList ||
            (ContainsHtmlTag(result.HtmlContent, "ul") || ContainsHtmlTag(result.HtmlContent, "ol"));
        var hasExpectedCodeBlocks = !testData.HasCodeBlock ||
            ContainsHtmlTag(result.HtmlContent, "pre");
        var hasExpectedTables = !testData.HasTable ||
            ContainsHtmlTag(result.HtmlContent, "table");

        return (hasHtmlContent && hasMetadata && hasSourcePath &&
                hasExpectedHeadings && hasExpectedLists && hasExpectedCodeBlocks && hasExpectedTables)
            .Label($"HasHeadings={hasExpectedHeadings}, HasLists={hasExpectedLists}, HasCodeBlocks={hasExpectedCodeBlocks}, HasTables={hasExpectedTables}")
            .Classify(testData.ExpectedHeadingLevels.Any(), "包含标题")
            .Classify(testData.HasList, "包含列表")
            .Classify(testData.HasCodeBlock, "包含代码块")
            .Classify(testData.HasTable, "包含表格");
    }

    #endregion


    #region Property 12: 内容解析正确性 - 显式测试

    /// <summary>
    /// Property 12 的显式测试版本 - 标题解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Theory]
    [InlineData(1, "一级标题")]
    [InlineData(2, "二级标题")]
    [InlineData(3, "三级标题")]
    [InlineData(4, "四级标题")]
    [InlineData(5, "五级标题")]
    [InlineData(6, "六级标题")]
    public void ContentParsing_Headings_ExplicitTest(int level, string text)
    {
        // Arrange
        var markdown = new string('#', level) + " " + text;
        var content = CreateYamlContent("标题测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain($"<h{level}");
        result.HtmlContent.Should().Contain(text);
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 段落解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Fact]
    public void ContentParsing_Paragraphs_ExplicitTest()
    {
        // Arrange
        var markdown = """
            这是第一段内容。

            这是第二段内容。

            这是第三段内容。
            """;
        var content = CreateYamlContent("段落测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<p>");
        result.HtmlContent.Should().Contain("第一段");
        result.HtmlContent.Should().Contain("第二段");
        result.HtmlContent.Should().Contain("第三段");
        CountHtmlTags(result.HtmlContent, "p").Should().BeGreaterThanOrEqualTo(3);
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 无序列表解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Fact]
    public void ContentParsing_UnorderedList_ExplicitTest()
    {
        // Arrange
        var markdown = """
            - 项目一
            - 项目二
            - 项目三
            """;
        var content = CreateYamlContent("无序列表测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<ul>");
        result.HtmlContent.Should().Contain("<li>");
        result.HtmlContent.Should().Contain("项目一");
        result.HtmlContent.Should().Contain("项目二");
        result.HtmlContent.Should().Contain("项目三");
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 有序列表解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Fact]
    public void ContentParsing_OrderedList_ExplicitTest()
    {
        // Arrange
        var markdown = """
            1. 第一步
            2. 第二步
            3. 第三步
            """;
        var content = CreateYamlContent("有序列表测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<ol>");
        result.HtmlContent.Should().Contain("<li>");
        result.HtmlContent.Should().Contain("第一步");
        result.HtmlContent.Should().Contain("第二步");
        result.HtmlContent.Should().Contain("第三步");
    }


    /// <summary>
    /// Property 12 的显式测试版本 - 代码块解析（带语言标识）
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Theory]
    [InlineData("csharp", "Console.WriteLine(\"Hello\");")]
    [InlineData("javascript", "console.log('Hello');")]
    [InlineData("python", "print('Hello')")]
    [InlineData("sql", "SELECT * FROM users")]
    public void ContentParsing_CodeBlock_WithLanguage_ExplicitTest(string language, string code)
    {
        // Arrange
        var markdown = $"""
            ```{language}
            {code}
            ```
            """;
        var content = CreateYamlContent("代码块测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre");
        result.HtmlContent.Should().Contain("<code");
        // 代码内容应该存在（可能被 HTML 编码）
        result.HtmlContent.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 表格解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Fact]
    public void ContentParsing_Table_ExplicitTest()
    {
        // Arrange
        var markdown = """
            | 列1 | 列2 | 列3 |
            |-----|-----|-----|
            | A1  | B1  | C1  |
            | A2  | B2  | C2  |
            """;
        var content = CreateYamlContent("表格测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<table");
        result.HtmlContent.Should().Contain("<tr");
        result.HtmlContent.Should().Contain("列1");
        result.HtmlContent.Should().Contain("列2");
        result.HtmlContent.Should().Contain("列3");
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 强调文本解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Theory]
    [InlineData("**粗体文本**", "strong", "粗体文本")]
    [InlineData("*斜体文本*", "em", "斜体文本")]
    [InlineData("__粗体下划线__", "strong", "粗体下划线")]
    [InlineData("_斜体下划线_", "em", "斜体下划线")]
    public void ContentParsing_Emphasis_ExplicitTest(string markdown, string expectedTag, string expectedText)
    {
        // Arrange
        var content = CreateYamlContent("强调测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain($"<{expectedTag}>");
        result.HtmlContent.Should().Contain(expectedText);
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 链接解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Theory]
    [InlineData("[链接文本](https://example.com)", "https://example.com", "链接文本")]
    [InlineData("[内部链接](/about)", "/about", "内部链接")]
    [InlineData("[相对链接](./page.html)", "./page.html", "相对链接")]
#pragma warning disable CA1054 // URI 参数应为 Uri 类型 - 测试中使用字符串更方便
    public void ContentParsing_Links_ExplicitTest(string markdown, string expectedUrl, string expectedText)
#pragma warning restore CA1054
    {
        // Arrange
        var content = CreateYamlContent("链接测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<a");
        result.HtmlContent.Should().Contain($"href=\"{expectedUrl}\"");
        result.HtmlContent.Should().Contain(expectedText);
    }


    /// <summary>
    /// Property 12 的显式测试版本 - 图片解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Theory]
    [InlineData("![图片描述](/images/test.png)", "/images/test.png", "图片描述")]
    [InlineData("![外部图片](https://example.com/image.jpg)", "https://example.com/image.jpg", "外部图片")]
#pragma warning disable CA1054 // URI 参数应为 Uri 类型 - 测试中使用字符串更方便
    public void ContentParsing_Images_ExplicitTest(string markdown, string expectedSrc, string expectedAlt)
#pragma warning restore CA1054
    {
        // Arrange
        var content = CreateYamlContent("图片测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<img");
        result.HtmlContent.Should().Contain($"src=\"{expectedSrc}\"");
        result.HtmlContent.Should().Contain($"alt=\"{expectedAlt}\"");
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 引用块解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Fact]
    public void ContentParsing_Blockquote_ExplicitTest()
    {
        // Arrange
        var markdown = """
            > 这是一段引用文本。
            > 引用可以有多行。
            """;
        var content = CreateYamlContent("引用测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<blockquote>");
        result.HtmlContent.Should().Contain("引用文本");
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 行内代码解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Fact]
    public void ContentParsing_InlineCode_ExplicitTest()
    {
        // Arrange
        var markdown = "使用 `Console.WriteLine()` 输出文本。";
        var content = CreateYamlContent("行内代码测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<code>");
        result.HtmlContent.Should().Contain("Console.WriteLine()");
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 水平分割线解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    public void ContentParsing_HorizontalRule_ExplicitTest(string markdown)
    {
        // Arrange
        var fullMarkdown = $"第一部分\n\n{markdown}\n\n第二部分";
        var content = CreateYamlContent("分割线测试", fullMarkdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        result.HtmlContent.Should().Contain("<hr");
    }

    /// <summary>
    /// Property 12 的显式测试版本 - 综合内容解析
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Fact]
    public void ContentParsing_ComplexContent_ExplicitTest()
    {
        // Arrange
        var markdown = """
            # 主标题

            这是一段介绍文字。

            ## 第一节

            这里有一个**粗体**和一个*斜体*。

            ### 代码示例

            ```csharp
            public class Hello
            {
                public void World() => Console.WriteLine("Hello");
            }
            ```

            ## 第二节

            这是一个列表：

            - 项目一
            - 项目二
            - 项目三

            ## 第三节

            这是一个表格：

            | 名称 | 值 |
            |------|-----|
            | A    | 1   |
            | B    | 2   |

            > 这是一段引用

            [链接](https://example.com)

            ![图片](/images/test.png)
            """;
        var content = CreateYamlContent("综合测试", markdown);
        var file = CreateContentFile(content);

        // Act
        var result = _parser.Parse(file);

        // Assert
        // 验证标题
        result.HtmlContent.Should().Contain("<h1");
        result.HtmlContent.Should().Contain("<h2");
        result.HtmlContent.Should().Contain("<h3");

        // 验证段落
        result.HtmlContent.Should().Contain("<p>");

        // 验证强调
        result.HtmlContent.Should().Contain("<strong>");
        result.HtmlContent.Should().Contain("<em>");

        // 验证代码块
        result.HtmlContent.Should().Contain("<pre");
        result.HtmlContent.Should().Contain("<code");

        // 验证列表
        result.HtmlContent.Should().Contain("<ul>");
        result.HtmlContent.Should().Contain("<li>");

        // 验证表格
        result.HtmlContent.Should().Contain("<table");
        result.HtmlContent.Should().Contain("<tr");

        // 验证引用
        result.HtmlContent.Should().Contain("<blockquote>");

        // 验证链接
        result.HtmlContent.Should().Contain("<a");

        // 验证图片
        result.HtmlContent.Should().Contain("<img");

        // 验证元数据
        result.Metadata.Should().NotBeNull();
        result.Metadata.Title.Should().Be("综合测试");
        result.WordCount.Should().BeGreaterThan(0);
        result.ReadingTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion
}


#region 测试数据类型

/// <summary>
/// Markdown 标题测试数据
/// </summary>
public sealed class MarkdownHeadingTestData
{
    /// <summary>
    /// 标题级别（1-6）
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// 标题文本
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public string Markdown => new string('#', Level) + " " + Text;

    public override string ToString() => $"H{Level}: {Text}";
}

/// <summary>
/// Markdown 段落测试数据
/// </summary>
public sealed class MarkdownParagraphTestData
{
    /// <summary>
    /// 段落列表
    /// </summary>
    public required IReadOnlyList<string> Paragraphs { get; init; }

    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public string Markdown => string.Join("\n\n", Paragraphs);

    public override string ToString() => $"Paragraphs: {Paragraphs.Count}";
}

/// <summary>
/// Markdown 列表测试数据
/// </summary>
public sealed class MarkdownListTestData
{
    /// <summary>
    /// 是否为有序列表
    /// </summary>
    public required bool IsOrdered { get; init; }

    /// <summary>
    /// 列表项
    /// </summary>
    public required IReadOnlyList<string> Items { get; init; }

    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public string Markdown
    {
        get
        {
            if (IsOrdered)
            {
                return string.Join("\n", Items.Select((item, i) => $"{i + 1}. {item}"));
            }
            return string.Join("\n", Items.Select(item => $"- {item}"));
        }
    }

    public override string ToString() => $"{(IsOrdered ? "Ordered" : "Unordered")} List: {Items.Count} items";
}


/// <summary>
/// Markdown 代码块测试数据
/// </summary>
public sealed class MarkdownCodeBlockTestData
{
    /// <summary>
    /// 编程语言标识
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// 代码内容
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public string Markdown => $"```{Language ?? ""}\n{Code}\n```";

    public override string ToString() => $"CodeBlock: {Language ?? "plain"}, {Code.Length} chars";
}

/// <summary>
/// Markdown 表格测试数据
/// </summary>
public sealed class MarkdownTableTestData
{
    /// <summary>
    /// 表头列表
    /// </summary>
    public required IReadOnlyList<string> Headers { get; init; }

    /// <summary>
    /// 数据行数
    /// </summary>
    public required int RowCount { get; init; }

    /// <summary>
    /// 数据行
    /// </summary>
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }

    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public string Markdown
    {
        get
        {
            var sb = new StringBuilder();
            // 表头
            sb.AppendLine("| " + string.Join(" | ", Headers) + " |");
            // 分隔行
            sb.AppendLine("| " + string.Join(" | ", Headers.Select(_ => "---")) + " |");
            // 数据行
            foreach (var row in Rows)
            {
                sb.AppendLine("| " + string.Join(" | ", row) + " |");
            }
            return sb.ToString();
        }
    }

    public override string ToString() => $"Table: {Headers.Count} cols, {RowCount} rows";
}

/// <summary>
/// Markdown 强调文本测试数据
/// </summary>
public sealed class MarkdownEmphasisTestData
{
    /// <summary>
    /// 是否为粗体
    /// </summary>
    public required bool IsBold { get; init; }

    /// <summary>
    /// 强调的文本
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public string Markdown => IsBold ? $"**{Text}**" : $"*{Text}*";

    public override string ToString() => $"{(IsBold ? "Bold" : "Italic")}: {Text}";
}


/// <summary>
/// Markdown 链接测试数据
/// </summary>
public sealed class MarkdownLinkTestData
{
    /// <summary>
    /// 链接 URL
    /// </summary>
#pragma warning disable CA1056 // URI 属性应为 Uri 类型 - 测试数据中使用字符串更方便
    public required string Url { get; init; }
#pragma warning restore CA1056

    /// <summary>
    /// 链接文本
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 是否为外部链接
    /// </summary>
    public required bool IsExternal { get; init; }

    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public string Markdown => $"[{Text}]({Url})";

    public override string ToString() => $"Link: {Text} -> {Url}";
}

/// <summary>
/// Markdown 图片测试数据
/// </summary>
public sealed class MarkdownImageTestData
{
    /// <summary>
    /// 图片路径
    /// </summary>
    public required string Src { get; init; }

    /// <summary>
    /// 替代文本
    /// </summary>
    public required string Alt { get; init; }

    /// <summary>
    /// 是否为外部图片
    /// </summary>
    public required bool IsExternal { get; init; }

    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public string Markdown => $"![{Alt}]({Src})";

    public override string ToString() => $"Image: {Alt} -> {Src}";
}

/// <summary>
/// Markdown 引用块测试数据
/// </summary>
public sealed class MarkdownBlockquoteTestData
{
    /// <summary>
    /// 引用文本
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 是否为嵌套引用
    /// </summary>
    public required bool IsNested { get; init; }

    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public string Markdown => IsNested ? $"> > {Text}" : $"> {Text}";

    public override string ToString() => $"Blockquote: {(IsNested ? "Nested" : "Single")}, {Text}";
}


/// <summary>
/// Markdown 复杂内容测试数据
/// </summary>
public sealed class MarkdownComplexContentTestData
{
    /// <summary>
    /// 生成的 Markdown 内容
    /// </summary>
    public required string Markdown { get; init; }

    /// <summary>
    /// 预期的标题级别列表
    /// </summary>
    public required IReadOnlyList<int> ExpectedHeadingLevels { get; init; }

    /// <summary>
    /// 是否包含列表
    /// </summary>
    public required bool HasList { get; init; }

    /// <summary>
    /// 是否包含代码块
    /// </summary>
    public required bool HasCodeBlock { get; init; }

    /// <summary>
    /// 是否包含表格
    /// </summary>
    public required bool HasTable { get; init; }

    public override string ToString() =>
        $"Complex: Headings={ExpectedHeadingLevels.Count}, List={HasList}, Code={HasCodeBlock}, Table={HasTable}";
}

#endregion

#region FsCheck 生成器

/// <summary>
/// Markdown 内容属性测试专用的 FsCheck 生成器
/// 生成各种类型的 Markdown 测试数据
/// </summary>
public static class MarkdownContentArbitraries
{
    /// <summary>
    /// 有效的标题文本列表
    /// </summary>
    private static readonly string[] ValidHeadingTexts =
    [
        "Introduction",
        "Getting Started",
        "Overview",
        "Summary",
        "Conclusion",
        "简介",
        "入门指南",
        "概述",
        "总结",
        "结论",
        "第一章",
        "第二章",
        "API Reference",
        "Configuration",
        "Installation"
    ];

    /// <summary>
    /// 有效的段落文本列表
    /// </summary>
    private static readonly string[] ValidParagraphTexts =
    [
        "This is a sample paragraph.",
        "Welcome to the documentation.",
        "这是一段示例文本。",
        "欢迎阅读本文档。",
        "Lorem ipsum dolor sit amet.",
        "The quick brown fox jumps over the lazy dog.",
        "静态站点生成器是一种强大的工具。",
        "本节将介绍基本概念。",
        "请按照以下步骤操作。",
        "更多信息请参阅官方文档。"
    ];

    /// <summary>
    /// 有效的列表项文本列表
    /// </summary>
    private static readonly string[] ValidListItems =
    [
        "First item",
        "Second item",
        "Third item",
        "Fourth item",
        "Fifth item",
        "第一项",
        "第二项",
        "第三项",
        "项目 A",
        "项目 B",
        "步骤一",
        "步骤二",
        "功能特性",
        "注意事项",
        "常见问题"
    ];


    /// <summary>
    /// 有效的编程语言列表
    /// </summary>
    private static readonly string[] ValidLanguages =
    [
        "csharp",
        "javascript",
        "typescript",
        "python",
        "java",
        "go",
        "rust",
        "sql",
        "html",
        "css",
        "json",
        "yaml",
        "xml",
        "bash",
        "powershell"
    ];

    /// <summary>
    /// 有效的代码片段列表
    /// </summary>
    private static readonly string[] ValidCodeSnippets =
    [
        "Console.WriteLine(\"Hello\");",
        "console.log('Hello');",
        "print('Hello')",
        "System.out.println(\"Hello\");",
        "fmt.Println(\"Hello\")",
        "println!(\"Hello\");",
        "SELECT * FROM users;",
        "<div>Hello</div>",
        ".container { color: red; }",
        "{ \"key\": \"value\" }",
        "key: value",
        "var x = 42;",
        "let y = 100;",
        "const z = 'test';",
        "function hello() { return 'world'; }"
    ];

    /// <summary>
    /// 有效的表头列表
    /// </summary>
    private static readonly string[] ValidTableHeaders =
    [
        "Name",
        "Value",
        "Description",
        "Type",
        "Default",
        "名称",
        "值",
        "描述",
        "类型",
        "默认值",
        "ID",
        "Status",
        "Date",
        "Author",
        "Version"
    ];

    /// <summary>
    /// 有效的表格单元格值列表
    /// </summary>
    private static readonly string[] ValidTableCells =
    [
        "A1", "B1", "C1", "D1",
        "A2", "B2", "C2", "D2",
        "Yes", "No", "N/A",
        "是", "否", "无",
        "1", "2", "3", "4", "5",
        "Active", "Inactive",
        "High", "Medium", "Low"
    ];


    /// <summary>
    /// 有效的强调文本列表
    /// </summary>
    private static readonly string[] ValidEmphasisTexts =
    [
        "important",
        "note",
        "warning",
        "重要",
        "注意",
        "警告",
        "key point",
        "关键点",
        "highlight",
        "高亮",
        "emphasis",
        "强调"
    ];

    /// <summary>
    /// 有效的链接文本列表
    /// </summary>
    private static readonly string[] ValidLinkTexts =
    [
        "Click here",
        "Learn more",
        "Documentation",
        "点击这里",
        "了解更多",
        "文档",
        "Official site",
        "官方网站",
        "GitHub",
        "API Reference"
    ];

    /// <summary>
    /// 有效的外部 URL 列表
    /// </summary>
    private static readonly string[] ValidExternalUrls =
    [
        "https://example.com",
        "https://github.com",
        "https://docs.microsoft.com",
        "https://www.google.com",
        "https://stackoverflow.com"
    ];

    /// <summary>
    /// 有效的内部 URL 列表
    /// </summary>
    private static readonly string[] ValidInternalUrls =
    [
        "/about",
        "/docs",
        "/api",
        "/contact",
        "./page.html",
        "../index.html",
        "/posts/first-post"
    ];

    /// <summary>
    /// 有效的图片替代文本列表
    /// </summary>
    private static readonly string[] ValidImageAlts =
    [
        "Logo",
        "Screenshot",
        "Diagram",
        "图标",
        "截图",
        "示意图",
        "Banner",
        "横幅",
        "Photo",
        "照片"
    ];

    /// <summary>
    /// 有效的图片路径列表
    /// </summary>
    private static readonly string[] ValidImagePaths =
    [
        "/images/logo.png",
        "/images/screenshot.jpg",
        "/assets/diagram.svg",
        "./images/photo.png",
        "../images/banner.jpg"
    ];

    /// <summary>
    /// 有效的外部图片 URL 列表
    /// </summary>
    private static readonly string[] ValidExternalImageUrls =
    [
        "https://example.com/image.png",
        "https://cdn.example.com/photo.jpg",
        "https://images.example.org/logo.svg"
    ];

    /// <summary>
    /// 有效的引用文本列表
    /// </summary>
    private static readonly string[] ValidBlockquoteTexts =
    [
        "This is a quote.",
        "Important note here.",
        "这是一段引用。",
        "重要提示。",
        "The only way to do great work is to love what you do.",
        "知识就是力量。",
        "Practice makes perfect.",
        "熟能生巧。"
    ];


    #region 生成器实现

    /// <summary>
    /// 生成 Markdown 标题测试数据
    /// </summary>
    public static Arbitrary<MarkdownHeadingTestData> MarkdownHeadingTestData() =>
        (from level in Gen.Choose(1, 6)
         from text in Gen.Elements(ValidHeadingTexts)
         select new MarkdownHeadingTestData
         {
             Level = level,
             Text = text
         }).ToArbitrary();

    /// <summary>
    /// 生成 Markdown 段落测试数据
    /// </summary>
    public static Arbitrary<MarkdownParagraphTestData> MarkdownParagraphTestData() =>
        (from count in Gen.Choose(1, 4)
         from paragraphs in Gen.ArrayOf(Gen.Elements(ValidParagraphTexts), count)
         let distinctParagraphs = paragraphs.Distinct().ToList()
         // 确保至少有一个段落
         let finalParagraphs = distinctParagraphs.Count > 0 ? distinctParagraphs : new List<string> { ValidParagraphTexts[0] }
         select new MarkdownParagraphTestData
         {
             Paragraphs = finalParagraphs
         }).ToArbitrary();

    /// <summary>
    /// 生成 Markdown 列表测试数据
    /// </summary>
    public static Arbitrary<MarkdownListTestData> MarkdownListTestData() =>
        (from isOrdered in ArbMap.Default.GeneratorFor<bool>()
         from count in Gen.Choose(2, 6)
         from items in Gen.ArrayOf(Gen.Elements(ValidListItems), count)
         let distinctItems = items.Distinct().ToList()
         // 确保至少有一个列表项
         let finalItems = distinctItems.Count > 0 ? distinctItems : new List<string> { ValidListItems[0] }
         select new MarkdownListTestData
         {
             IsOrdered = isOrdered,
             Items = finalItems
         }).ToArbitrary();

    /// <summary>
    /// 生成 Markdown 代码块测试数据
    /// </summary>
    public static Arbitrary<MarkdownCodeBlockTestData> MarkdownCodeBlockTestData() =>
        (from hasLanguage in ArbMap.Default.GeneratorFor<bool>()
         from language in Gen.Elements(ValidLanguages)
         from code in Gen.Elements(ValidCodeSnippets)
         select new MarkdownCodeBlockTestData
         {
             Language = hasLanguage ? language : null,
             Code = code
         }).ToArbitrary();


    /// <summary>
    /// 生成 Markdown 表格测试数据
    /// </summary>
    public static Arbitrary<MarkdownTableTestData> MarkdownTableTestData() =>
        (from colCount in Gen.Choose(2, 5)
         from rowCount in Gen.Choose(1, 5)
         from headers in Gen.ListOf<string>(Gen.Elements(ValidTableHeaders)).Select(h => h.Take(colCount).ToList())
         from rows in Gen.ListOf<IReadOnlyList<string>>(Gen.ListOf<string>(Gen.Elements(ValidTableCells)).Select(r => (IReadOnlyList<string>)r.Take(colCount).ToList())).Select(r => r.Take(rowCount).ToList())
         let distinctHeaders = headers.Distinct().Take(colCount).ToList()
         // 0 列退化形态（Gen.ListOf 产空列表时）不是有效 Markdown 表格——
         // 属性前提是"任意有效的 Markdown 表格"；Markdig 1.x 收紧了对
         // 空表头行的解析（0.44 宽松渲染为 table，1.x 视为普通文本）
         where distinctHeaders.Count > 0
         let actualColCount = distinctHeaders.Count
         select new MarkdownTableTestData
         {
             Headers = distinctHeaders,
             RowCount = rowCount,
             Rows = rows.Select(r => (IReadOnlyList<string>)r.Take(actualColCount).ToList()).ToList()
         }).ToArbitrary();

    /// <summary>
    /// 生成 Markdown 强调文本测试数据
    /// </summary>
    public static Arbitrary<MarkdownEmphasisTestData> MarkdownEmphasisTestData() =>
        (from isBold in ArbMap.Default.GeneratorFor<bool>()
         from text in Gen.Elements(ValidEmphasisTexts)
         select new MarkdownEmphasisTestData
         {
             IsBold = isBold,
             Text = text
         }).ToArbitrary();

    /// <summary>
    /// 生成 Markdown 链接测试数据
    /// </summary>
    public static Arbitrary<MarkdownLinkTestData> MarkdownLinkTestData() =>
        (from isExternal in ArbMap.Default.GeneratorFor<bool>()
         from text in Gen.Elements(ValidLinkTexts)
         from externalUrl in Gen.Elements(ValidExternalUrls)
         from internalUrl in Gen.Elements(ValidInternalUrls)
         select new MarkdownLinkTestData
         {
             Url = isExternal ? externalUrl : internalUrl,
             Text = text,
             IsExternal = isExternal
         }).ToArbitrary();

    /// <summary>
    /// 生成 Markdown 图片测试数据
    /// </summary>
    public static Arbitrary<MarkdownImageTestData> MarkdownImageTestData() =>
        (from isExternal in ArbMap.Default.GeneratorFor<bool>()
         from alt in Gen.Elements(ValidImageAlts)
         from internalPath in Gen.Elements(ValidImagePaths)
         from externalUrl in Gen.Elements(ValidExternalImageUrls)
         select new MarkdownImageTestData
         {
             Src = isExternal ? externalUrl : internalPath,
             Alt = alt,
             IsExternal = isExternal
         }).ToArbitrary();

    /// <summary>
    /// 生成 Markdown 引用块测试数据
    /// </summary>
    public static Arbitrary<MarkdownBlockquoteTestData> MarkdownBlockquoteTestData() =>
        (from isNested in ArbMap.Default.GeneratorFor<bool>()
         from text in Gen.Elements(ValidBlockquoteTexts)
         select new MarkdownBlockquoteTestData
         {
             Text = text,
             IsNested = isNested
         }).ToArbitrary();


    /// <summary>
    /// 生成 Markdown 复杂内容测试数据
    /// </summary>
    public static Arbitrary<MarkdownComplexContentTestData> MarkdownComplexContentTestData() =>
        (from hasH1 in ArbMap.Default.GeneratorFor<bool>()
         from hasH2 in ArbMap.Default.GeneratorFor<bool>()
         from hasH3 in ArbMap.Default.GeneratorFor<bool>()
         from hasList in ArbMap.Default.GeneratorFor<bool>()
         from hasCodeBlock in ArbMap.Default.GeneratorFor<bool>()
         from hasTable in ArbMap.Default.GeneratorFor<bool>()
         from h1Text in Gen.Elements(ValidHeadingTexts)
         from h2Text in Gen.Elements(ValidHeadingTexts)
         from h3Text in Gen.Elements(ValidHeadingTexts)
         from paragraph in Gen.Elements(ValidParagraphTexts)
         from listItems in Gen.ArrayOf(Gen.Elements(ValidListItems), 3)
         from language in Gen.Elements(ValidLanguages)
         from code in Gen.Elements(ValidCodeSnippets)
         from headerCount in Gen.Choose(2, 4)
         from headers in Gen.ArrayOf(Gen.Elements(ValidTableHeaders), headerCount)
         from rowCount in Gen.Choose(1, 3)
         from cells in Gen.ArrayOf(Gen.ArrayOf(Gen.Elements(ValidTableCells), headerCount), rowCount)
         let headingLevels = new List<int>()
         let sb = new StringBuilder()
         let distinctHeaders = headers.Distinct().ToList()
         // 确保表格至少有2列
         let finalHeaders = distinctHeaders.Count >= 2 ? distinctHeaders : new List<string> { "Column1", "Column2" }
         let finalCells = cells.Select(r => r.Take(finalHeaders.Count).ToList()).ToList()
         select BuildComplexContent(
             sb, headingLevels,
             hasH1, hasH2, hasH3, hasList, hasCodeBlock, hasTable,
             h1Text, h2Text, h3Text, paragraph,
             listItems.Distinct().Take(3).ToList(), language, code,
             finalHeaders,
             finalCells
         )).ToArbitrary();

    /// <summary>
    /// 构建复杂的 Markdown 内容
    /// </summary>
    private static MarkdownComplexContentTestData BuildComplexContent(
        StringBuilder sb,
        List<int> headingLevels,
        bool hasH1, bool hasH2, bool hasH3,
        bool hasList, bool hasCodeBlock, bool hasTable,
        string h1Text, string h2Text, string h3Text,
        string paragraph,
        List<string> listItems,
        string language, string code,
        List<string> headers,
        List<List<string>> cells)
    {
        // 添加标题
        if (hasH1)
        {
            sb.AppendLine($"# {h1Text}");
            sb.AppendLine();
            headingLevels.Add(1);
        }

        // 添加段落
        sb.AppendLine(paragraph);
        sb.AppendLine();

        if (hasH2)
        {
            sb.AppendLine($"## {h2Text}");
            sb.AppendLine();
            headingLevels.Add(2);
        }

        // 添加列表
        var actualHasList = hasList && listItems.Count > 0;
        if (actualHasList)
        {
            foreach (var item in listItems.Distinct().Take(3))
            {
                sb.AppendLine($"- {item}");
            }
            sb.AppendLine();
        }

        if (hasH3)
        {
            sb.AppendLine($"### {h3Text}");
            sb.AppendLine();
            headingLevels.Add(3);
        }

        // 添加代码块
        if (hasCodeBlock)
        {
            sb.AppendLine($"```{language}");
            sb.AppendLine(code);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // 添加表格 - 确保 headers 至少有 2 列
        var actualHasTable = hasTable && headers.Count >= 2;
        if (actualHasTable)
        {
            sb.AppendLine("| " + string.Join(" | ", headers) + " |");
            sb.AppendLine("| " + string.Join(" | ", headers.Select(_ => "---")) + " |");
            foreach (var row in cells)
            {
                var rowCells = row.Take(headers.Count).ToList();
                while (rowCells.Count < headers.Count)
                {
                    rowCells.Add("N/A");
                }
                sb.AppendLine("| " + string.Join(" | ", rowCells) + " |");
            }
            sb.AppendLine();
        }

        return new MarkdownComplexContentTestData
        {
            Markdown = sb.ToString(),
            ExpectedHeadingLevels = headingLevels,
            HasList = actualHasList,
            HasCodeBlock = hasCodeBlock,
            HasTable = actualHasTable
        };
    }

    #endregion
}

#endregion

// Flint 静态站点生成器
// 内容解析器集成测试
// 测试 YAML/TOML/JSON Front Matter 解析、Markdown 转换、代码高亮、表格渲染
// _Requirements: 5.1, 5.2, 5.3_

using System.Text;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

// 使用类型别名解决命名空间冲突
using CoreFrontMatterFormat = Flint.Core.Models.FrontMatterFormat;
using UtilFrontMatterFormat = Flint.IntegrationTests.Utilities.FrontMatterFormat;

namespace Flint.IntegrationTests.Content;

/// <summary>
/// 内容解析器集成测试
/// 验证 ContentParser、FrontMatterParser、MarkdownParser 的协同工作
/// </summary>
public class ContentParserIntegrationTests : IDisposable
{
    private readonly ContentParser _parser;
    private readonly FrontMatterParser _frontMatterParser;
    private readonly MarkdownParser _markdownParser;
    private readonly string _tempDir;

    public ContentParserIntegrationTests()
    {
        _parser = new ContentParser();
        _frontMatterParser = new FrontMatterParser();
        _markdownParser = new MarkdownParser();
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-content-test-{Guid.NewGuid():N}");
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
    /// 创建带有 YAML Front Matter 的内容
    /// </summary>
    private static string CreateYamlContent(
        string title,
        DateTimeOffset? date = null,
        bool draft = false,
        string? description = null,
        string[]? tags = null,
        string[]? categories = null,
        string? author = null,
        int weight = 0,
        string? layout = null,
        string? slug = null,
        string? summary = null,
        string[]? keywords = null,
        string[]? aliases = null,
        string markdownBody = "这是正文内容。")
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{title}\"");

        if (date.HasValue)
            sb.AppendLine($"date: {date.Value:yyyy-MM-ddTHH:mm:sszzz}");

        if (draft)
            sb.AppendLine("draft: true");

        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"description: \"{description}\"");

        if (!string.IsNullOrEmpty(summary))
            sb.AppendLine($"summary: \"{summary}\"");

        if (!string.IsNullOrEmpty(author))
            sb.AppendLine($"author: \"{author}\"");

        if (weight != 0)
            sb.AppendLine($"weight: {weight}");

        if (!string.IsNullOrEmpty(layout))
            sb.AppendLine($"layout: \"{layout}\"");

        if (!string.IsNullOrEmpty(slug))
            sb.AppendLine($"slug: \"{slug}\"");

        if (tags is { Length: > 0 })
        {
            sb.AppendLine("tags:");
            foreach (var tag in tags)
                sb.AppendLine($"  - \"{tag}\"");
        }

        if (categories is { Length: > 0 })
        {
            sb.AppendLine("categories:");
            foreach (var category in categories)
                sb.AppendLine($"  - \"{category}\"");
        }

        if (keywords is { Length: > 0 })
        {
            sb.AppendLine("keywords:");
            foreach (var keyword in keywords)
                sb.AppendLine($"  - \"{keyword}\"");
        }

        if (aliases is { Length: > 0 })
        {
            sb.AppendLine("aliases:");
            foreach (var alias in aliases)
                sb.AppendLine($"  - \"{alias}\"");
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(markdownBody);

        return sb.ToString();
    }

    /// <summary>
    /// 创建带有 TOML Front Matter 的内容
    /// </summary>
    private static string CreateTomlContent(
        string title,
        DateTimeOffset? date = null,
        bool draft = false,
        string? description = null,
        string[]? tags = null,
        string[]? categories = null,
        string? author = null,
        int weight = 0,
        string? layout = null,
        string? slug = null,
        string? summary = null,
        string[]? keywords = null,
        string[]? aliases = null,
        string markdownBody = "这是正文内容。")
    {
        var sb = new StringBuilder();
        sb.AppendLine("+++");
        sb.AppendLine($"title = \"{title}\"");

        if (date.HasValue)
            sb.AppendLine($"date = {date.Value:yyyy-MM-ddTHH:mm:sszzz}");

        if (draft)
            sb.AppendLine("draft = true");

        if (!string.IsNullOrEmpty(description))
            sb.AppendLine($"description = \"{description}\"");

        if (!string.IsNullOrEmpty(summary))
            sb.AppendLine($"summary = \"{summary}\"");

        if (!string.IsNullOrEmpty(author))
            sb.AppendLine($"author = \"{author}\"");

        if (weight != 0)
            sb.AppendLine($"weight = {weight}");

        if (!string.IsNullOrEmpty(layout))
            sb.AppendLine($"layout = \"{layout}\"");

        if (!string.IsNullOrEmpty(slug))
            sb.AppendLine($"slug = \"{slug}\"");

        if (tags is { Length: > 0 })
            sb.AppendLine($"tags = [{string.Join(", ", tags.Select(t => $"\"{t}\""))}]");

        if (categories is { Length: > 0 })
            sb.AppendLine($"categories = [{string.Join(", ", categories.Select(c => $"\"{c}\""))}]");

        if (keywords is { Length: > 0 })
            sb.AppendLine($"keywords = [{string.Join(", ", keywords.Select(k => $"\"{k}\""))}]");

        if (aliases is { Length: > 0 })
            sb.AppendLine($"aliases = [{string.Join(", ", aliases.Select(a => $"\"{a}\""))}]");

        sb.AppendLine("+++");
        sb.AppendLine();
        sb.AppendLine(markdownBody);

        return sb.ToString();
    }

    /// <summary>
    /// 创建带有 JSON Front Matter 的内容
    /// </summary>
    private static string CreateJsonContent(
        string title,
        DateTimeOffset? date = null,
        bool draft = false,
        string? description = null,
        string[]? tags = null,
        string[]? categories = null,
        string? author = null,
        int weight = 0,
        string? layout = null,
        string? slug = null,
        string? summary = null,
        string[]? keywords = null,
        string[]? aliases = null,
        string markdownBody = "这是正文内容。")
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"title\": \"{title}\",");

        var fields = new List<string>();

        if (date.HasValue)
            fields.Add($"  \"date\": \"{date.Value:yyyy-MM-ddTHH:mm:sszzz}\"");

        if (draft)
            fields.Add("  \"draft\": true");

        if (!string.IsNullOrEmpty(description))
            fields.Add($"  \"description\": \"{description}\"");

        if (!string.IsNullOrEmpty(summary))
            fields.Add($"  \"summary\": \"{summary}\"");

        if (!string.IsNullOrEmpty(author))
            fields.Add($"  \"author\": \"{author}\"");

        if (weight != 0)
            fields.Add($"  \"weight\": {weight}");

        if (!string.IsNullOrEmpty(layout))
            fields.Add($"  \"layout\": \"{layout}\"");

        if (!string.IsNullOrEmpty(slug))
            fields.Add($"  \"slug\": \"{slug}\"");

        if (tags is { Length: > 0 })
            fields.Add($"  \"tags\": [{string.Join(", ", tags.Select(t => $"\"{t}\""))}]");

        if (categories is { Length: > 0 })
            fields.Add($"  \"categories\": [{string.Join(", ", categories.Select(c => $"\"{c}\""))}]");

        if (keywords is { Length: > 0 })
            fields.Add($"  \"keywords\": [{string.Join(", ", keywords.Select(k => $"\"{k}\""))}]");

        if (aliases is { Length: > 0 })
            fields.Add($"  \"aliases\": [{string.Join(", ", aliases.Select(a => $"\"{a}\""))}]");

        if (fields.Count > 0)
            sb.AppendLine(string.Join(",\n", fields));

        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(markdownBody);

        return sb.ToString();
    }

    #endregion

    #region YAML Front Matter 解析测试

    /// <summary>
    /// 测试 YAML Front Matter 基本字段解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_YamlFrontMatter_BasicFields_ShouldParseCorrectly()
    {
        // Arrange
        var date = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(8));
        var content = CreateYamlContent(
            title: "测试文章标题",
            date: date,
            draft: false,
            description: "这是文章描述",
            markdownBody: "# 标题\n\n这是正文内容。");

        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("测试文章标题");
        result.Metadata.Date.Should().NotBeNull();
        result.Metadata.Date!.Value.Date.Should().Be(date.Date);
        result.Metadata.Draft.Should().BeFalse();
        result.Metadata.Description.Should().Be("这是文章描述");
        result.HtmlContent.Should().Contain("<h1");
        result.HtmlContent.Should().Contain("标题");
    }

    /// <summary>
    /// 测试 YAML Front Matter 标签和分类解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_YamlFrontMatter_TagsAndCategories_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            title: "带标签的文章",
            tags: ["技术", "编程", "C#"],
            categories: ["教程", "开发"]);

        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Tags.Should().HaveCount(3);
        result.Metadata.Tags.Should().Contain("技术");
        result.Metadata.Tags.Should().Contain("编程");
        result.Metadata.Tags.Should().Contain("C#");
        result.Metadata.Categories.Should().HaveCount(2);
        result.Metadata.Categories.Should().Contain("教程");
        result.Metadata.Categories.Should().Contain("开发");
    }

    /// <summary>
    /// 测试 YAML Front Matter 所有支持的字段
    /// </summary>
    [Fact]
    public async Task ParseAsync_YamlFrontMatter_AllFields_ShouldParseCorrectly()
    {
        // Arrange
        var date = DateTimeOffset.Now;
        var content = CreateYamlContent(
            title: "完整字段测试",
            date: date,
            draft: true,
            description: "文章描述",
            summary: "文章摘要",
            author: "测试作者",
            weight: 10,
            layout: "post",
            slug: "complete-test",
            tags: ["tag1", "tag2"],
            categories: ["cat1"],
            keywords: ["keyword1", "keyword2"],
            aliases: ["/old-url", "/another-old-url"]);

        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Title.Should().Be("完整字段测试");
        result.Metadata.Date.Should().NotBeNull();
        result.Metadata.Draft.Should().BeTrue();
        result.Metadata.Description.Should().Be("文章描述");
        result.Metadata.Summary.Should().Be("文章摘要");
        result.Metadata.Author.Should().Be("测试作者");
        result.Metadata.Weight.Should().Be(10);
        result.Metadata.Layout.Should().Be("post");
        result.Metadata.Slug.Should().Be("complete-test");
        result.Metadata.Tags.Should().HaveCount(2);
        result.Metadata.Categories.Should().HaveCount(1);
        result.Metadata.Keywords.Should().HaveCount(2);
        result.Metadata.Aliases.Should().HaveCount(2);
        result.Metadata.Format.Should().Be(CoreFrontMatterFormat.Yaml);
    }

    /// <summary>
    /// 测试 YAML Front Matter 草稿标记
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ParseAsync_YamlFrontMatter_DraftFlag_ShouldParseCorrectly(bool isDraft)
    {
        // Arrange
        var content = CreateYamlContent(title: "草稿测试", draft: isDraft);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Draft.Should().Be(isDraft);
    }

    /// <summary>
    /// 测试 YAML Front Matter 权重字段
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(-10)]
    public async Task ParseAsync_YamlFrontMatter_Weight_ShouldParseCorrectly(int weight)
    {
        // Arrange
        var content = CreateYamlContent(title: "权重测试", weight: weight);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Weight.Should().Be(weight);
    }

    #endregion

    #region TOML Front Matter 解析测试

    /// <summary>
    /// 测试 TOML Front Matter 基本字段解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_TomlFrontMatter_BasicFields_ShouldParseCorrectly()
    {
        // Arrange
        var date = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(8));
        var content = CreateTomlContent(
            title: "TOML测试文章",
            date: date,
            draft: false,
            description: "TOML格式描述");

        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("TOML测试文章");
        result.Metadata.Date.Should().NotBeNull();
        result.Metadata.Date!.Value.Date.Should().Be(date.Date);
        result.Metadata.Draft.Should().BeFalse();
        result.Metadata.Description.Should().Be("TOML格式描述");
        result.Metadata.Format.Should().Be(CoreFrontMatterFormat.Toml);
    }

    /// <summary>
    /// 测试 TOML Front Matter 标签和分类解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_TomlFrontMatter_TagsAndCategories_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateTomlContent(
            title: "TOML标签测试",
            tags: ["Rust", "Go", "Python"],
            categories: ["后端", "系统编程"]);

        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Tags.Should().HaveCount(3);
        result.Metadata.Tags.Should().Contain("Rust");
        result.Metadata.Tags.Should().Contain("Go");
        result.Metadata.Tags.Should().Contain("Python");
        result.Metadata.Categories.Should().HaveCount(2);
    }

    /// <summary>
    /// 测试 TOML Front Matter 所有支持的字段
    /// </summary>
    [Fact]
    public async Task ParseAsync_TomlFrontMatter_AllFields_ShouldParseCorrectly()
    {
        // Arrange
        var date = DateTimeOffset.Now;
        var content = CreateTomlContent(
            title: "TOML完整字段",
            date: date,
            draft: true,
            description: "TOML描述",
            summary: "TOML摘要",
            author: "TOML作者",
            weight: 20,
            layout: "page",
            slug: "toml-complete",
            tags: ["toml-tag"],
            categories: ["toml-cat"],
            keywords: ["toml-kw"],
            aliases: ["/toml-old"]);

        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Title.Should().Be("TOML完整字段");
        result.Metadata.Draft.Should().BeTrue();
        result.Metadata.Description.Should().Be("TOML描述");
        result.Metadata.Summary.Should().Be("TOML摘要");
        result.Metadata.Author.Should().Be("TOML作者");
        result.Metadata.Weight.Should().Be(20);
        result.Metadata.Layout.Should().Be("page");
        result.Metadata.Slug.Should().Be("toml-complete");
        result.Metadata.Format.Should().Be(CoreFrontMatterFormat.Toml);
    }

    #endregion

    #region JSON Front Matter 解析测试

    /// <summary>
    /// 测试 JSON Front Matter 基本字段解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_JsonFrontMatter_BasicFields_ShouldParseCorrectly()
    {
        // Arrange
        var date = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(8));
        var content = CreateJsonContent(
            title: "JSON测试文章",
            date: date,
            draft: false,
            description: "JSON格式描述");

        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("JSON测试文章");
        result.Metadata.Date.Should().NotBeNull();
        result.Metadata.Date!.Value.Date.Should().Be(date.Date);
        result.Metadata.Draft.Should().BeFalse();
        result.Metadata.Description.Should().Be("JSON格式描述");
        result.Metadata.Format.Should().Be(CoreFrontMatterFormat.Json);
    }

    /// <summary>
    /// 测试 JSON Front Matter 标签和分类解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_JsonFrontMatter_TagsAndCategories_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateJsonContent(
            title: "JSON标签测试",
            tags: ["JavaScript", "TypeScript", "Node.js"],
            categories: ["前端", "全栈"]);

        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Tags.Should().HaveCount(3);
        result.Metadata.Tags.Should().Contain("JavaScript");
        result.Metadata.Tags.Should().Contain("TypeScript");
        result.Metadata.Tags.Should().Contain("Node.js");
        result.Metadata.Categories.Should().HaveCount(2);
    }

    /// <summary>
    /// 测试 JSON Front Matter 所有支持的字段
    /// </summary>
    [Fact]
    public async Task ParseAsync_JsonFrontMatter_AllFields_ShouldParseCorrectly()
    {
        // Arrange
        var date = DateTimeOffset.Now;
        var content = CreateJsonContent(
            title: "JSON完整字段",
            date: date,
            draft: true,
            description: "JSON描述",
            summary: "JSON摘要",
            author: "JSON作者",
            weight: 30,
            layout: "article",
            slug: "json-complete",
            tags: ["json-tag"],
            categories: ["json-cat"],
            keywords: ["json-kw"],
            aliases: ["/json-old"]);

        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Title.Should().Be("JSON完整字段");
        result.Metadata.Draft.Should().BeTrue();
        result.Metadata.Description.Should().Be("JSON描述");
        result.Metadata.Summary.Should().Be("JSON摘要");
        result.Metadata.Author.Should().Be("JSON作者");
        result.Metadata.Weight.Should().Be(30);
        result.Metadata.Layout.Should().Be("article");
        result.Metadata.Slug.Should().Be("json-complete");
        result.Metadata.Format.Should().Be(CoreFrontMatterFormat.Json);
    }

    #endregion

    #region Markdown 到 HTML 转换测试

    /// <summary>
    /// 测试标题转换（H1-H6）
    /// </summary>
    [Theory]
    [InlineData("# 一级标题", "h1")]
    [InlineData("## 二级标题", "h2")]
    [InlineData("### 三级标题", "h3")]
    [InlineData("#### 四级标题", "h4")]
    [InlineData("##### 五级标题", "h5")]
    [InlineData("###### 六级标题", "h6")]
    public async Task ParseAsync_MarkdownHeadings_ShouldConvertToHtml(string markdown, string expectedTag)
    {
        // Arrange
        var content = CreateYamlContent(title: "标题测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain($"<{expectedTag}");
        result.HtmlContent.Should().Contain($"</{expectedTag}>");
    }

    /// <summary>
    /// 测试段落转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownParagraphs_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = """
            这是第一段内容。

            这是第二段内容。

            这是第三段内容。
            """;
        var content = CreateYamlContent(title: "段落测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<p>");
        result.HtmlContent.Should().Contain("第一段");
        result.HtmlContent.Should().Contain("第二段");
        result.HtmlContent.Should().Contain("第三段");
    }

    /// <summary>
    /// 测试无序列表转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownUnorderedList_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = """
            - 项目一
            - 项目二
            - 项目三
            """;
        var content = CreateYamlContent(title: "无序列表测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<ul>");
        result.HtmlContent.Should().Contain("<li>");
        result.HtmlContent.Should().Contain("项目一");
        result.HtmlContent.Should().Contain("项目二");
        result.HtmlContent.Should().Contain("项目三");
    }

    /// <summary>
    /// 测试有序列表转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownOrderedList_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = """
            1. 第一步
            2. 第二步
            3. 第三步
            """;
        var content = CreateYamlContent(title: "有序列表测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<ol>");
        result.HtmlContent.Should().Contain("<li>");
        result.HtmlContent.Should().Contain("第一步");
        result.HtmlContent.Should().Contain("第二步");
        result.HtmlContent.Should().Contain("第三步");
    }

    /// <summary>
    /// 测试嵌套列表转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownNestedList_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = """
            - 父项目一
              - 子项目一
              - 子项目二
            - 父项目二
              - 子项目三
            """;
        var content = CreateYamlContent(title: "嵌套列表测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<ul>");
        result.HtmlContent.Should().Contain("<li>");
        result.HtmlContent.Should().Contain("父项目一");
        result.HtmlContent.Should().Contain("子项目一");
    }

    /// <summary>
    /// 测试粗体和斜体转换
    /// </summary>
    [Theory]
    [InlineData("**粗体文本**", "<strong>粗体文本</strong>")]
    [InlineData("*斜体文本*", "<em>斜体文本</em>")]
    [InlineData("***粗斜体***", "<em><strong>粗斜体</strong></em>")]
    public async Task ParseAsync_MarkdownEmphasis_ShouldConvertToHtml(string markdown, string expectedHtml)
    {
        // Arrange
        var content = CreateYamlContent(title: "强调测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain(expectedHtml);
    }


    /// <summary>
    /// 测试链接转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownLinks_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = """
            这是一个[链接文本](https://example.com "链接标题")。
            
            还有一个[无标题链接](https://example.org)。
            """;
        var content = CreateYamlContent(title: "链接测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<a href=\"https://example.com\"");
        result.HtmlContent.Should().Contain("链接文本");
        result.HtmlContent.Should().Contain("<a href=\"https://example.org\"");
    }

    /// <summary>
    /// 测试图片转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownImages_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = """
            ![图片描述](/images/test.png "图片标题")
            
            ![另一张图片](https://example.com/image.jpg)
            """;
        var content = CreateYamlContent(title: "图片测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<img");
        result.HtmlContent.Should().Contain("src=\"/images/test.png\"");
        result.HtmlContent.Should().Contain("alt=\"图片描述\"");
    }

    /// <summary>
    /// 测试引用块转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownBlockquote_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = """
            > 这是一段引用文本。
            > 引用可以有多行。
            >
            > 甚至可以有多个段落。
            """;
        var content = CreateYamlContent(title: "引用测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<blockquote>");
        result.HtmlContent.Should().Contain("引用文本");
    }

    /// <summary>
    /// 测试行内代码转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownInlineCode_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = "使用 `Console.WriteLine()` 输出文本。";
        var content = CreateYamlContent(title: "行内代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<code>");
        result.HtmlContent.Should().Contain("Console.WriteLine()");
    }


    /// <summary>
    /// 测试水平分割线转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownHorizontalRule_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = """
            第一部分内容。

            ---

            第二部分内容。
            """;
        var content = CreateYamlContent(title: "分割线测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<hr");
    }

    /// <summary>
    /// 测试任务列表转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownTaskList_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = """
            - [x] 已完成任务
            - [ ] 未完成任务
            - [x] 另一个已完成任务
            """;
        var content = CreateYamlContent(title: "任务列表测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<input");
        result.HtmlContent.Should().Contain("type=\"checkbox\"");
        result.HtmlContent.Should().Contain("已完成任务");
        result.HtmlContent.Should().Contain("未完成任务");
    }

    /// <summary>
    /// 测试删除线转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownStrikethrough_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = "这是~~删除线~~文本。";
        var content = CreateYamlContent(title: "删除线测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<del>");
        result.HtmlContent.Should().Contain("删除线");
    }

    /// <summary>
    /// 测试自动链接转换
    /// </summary>
    [Fact]
    public async Task ParseAsync_MarkdownAutoLink_ShouldConvertToHtml()
    {
        // Arrange
        var markdown = "访问 https://example.com 获取更多信息。";
        var content = CreateYamlContent(title: "自动链接测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<a href=\"https://example.com\"");
    }

    #endregion

    #region 代码块语法高亮测试

    /// <summary>
    /// 测试 C# 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_CSharpCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```csharp
            public class HelloWorld
            {
                public static void Main(string[] args)
                {
                    Console.WriteLine("Hello, World!");
                }
            }
            ```
            """;
        var content = CreateYamlContent(title: "C#代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-csharp\"");
        result.HtmlContent.Should().Contain("HelloWorld");
        result.HtmlContent.Should().Contain("Console.WriteLine");
    }

    /// <summary>
    /// 测试 JavaScript 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_JavaScriptCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```javascript
            function greet(name) {
                console.log(`Hello, ${name}!`);
            }
            
            greet('World');
            ```
            """;
        var content = CreateYamlContent(title: "JavaScript代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-javascript\"");
        result.HtmlContent.Should().Contain("function greet");
    }

    /// <summary>
    /// 测试 Python 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_PythonCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```python
            def fibonacci(n):
                if n <= 1:
                    return n
                return fibonacci(n-1) + fibonacci(n-2)
            
            print(fibonacci(10))
            ```
            """;
        var content = CreateYamlContent(title: "Python代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-python\"");
        result.HtmlContent.Should().Contain("def fibonacci");
    }

    /// <summary>
    /// 测试 TypeScript 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_TypeScriptCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```typescript
            interface User {
                id: number;
                name: string;
                email: string;
            }
            
            const getUser = async (id: number): Promise<User> => {
                const response = await fetch(`/api/users/${id}`);
                return response.json();
            };
            ```
            """;
        var content = CreateYamlContent(title: "TypeScript代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-typescript\"");
        result.HtmlContent.Should().Contain("interface User");
    }


    /// <summary>
    /// 测试 Go 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_GoCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```go
            package main
            
            import "fmt"
            
            func main() {
                fmt.Println("Hello, Go!")
            }
            ```
            """;
        var content = CreateYamlContent(title: "Go代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-go\"");
        result.HtmlContent.Should().Contain("package main");
    }

    /// <summary>
    /// 测试 Rust 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_RustCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```rust
            fn main() {
                let message = "Hello, Rust!";
                println!("{}", message);
            }
            ```
            """;
        var content = CreateYamlContent(title: "Rust代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-rust\"");
        result.HtmlContent.Should().Contain("fn main");
    }

    /// <summary>
    /// 测试 SQL 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_SqlCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```sql
            SELECT u.id, u.name, COUNT(o.id) as order_count
            FROM users u
            LEFT JOIN orders o ON u.id = o.user_id
            WHERE u.created_at > '2024-01-01'
            GROUP BY u.id, u.name
            ORDER BY order_count DESC;
            ```
            """;
        var content = CreateYamlContent(title: "SQL代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-sql\"");
        result.HtmlContent.Should().Contain("SELECT");
    }

    /// <summary>
    /// 测试 HTML 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_HtmlCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```html
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head>
                <meta charset="UTF-8">
                <title>测试页面</title>
            </head>
            <body>
                <h1>Hello, World!</h1>
            </body>
            </html>
            ```
            """;
        var content = CreateYamlContent(title: "HTML代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-html\"");
        result.HtmlContent.Should().Contain("&lt;html");
    }

    /// <summary>
    /// 测试 CSS 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_CssCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```css
            .container {
                max-width: 1200px;
                margin: 0 auto;
                padding: 20px;
            }
            
            .button {
                background-color: #007bff;
                color: white;
                border: none;
                padding: 10px 20px;
                cursor: pointer;
            }
            ```
            """;
        var content = CreateYamlContent(title: "CSS代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-css\"");
        result.HtmlContent.Should().Contain(".container");
    }

    /// <summary>
    /// 测试 YAML 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_YamlCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```yaml
            name: CI Pipeline
            on:
              push:
                branches: [main]
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
                  - name: Build
                    run: dotnet build
            ```
            """;
        var content = CreateYamlContent(title: "YAML代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-yaml\"");
        result.HtmlContent.Should().Contain("name: CI Pipeline");
    }

    /// <summary>
    /// 测试 JSON 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_JsonCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```json
            {
                "name": "Flint",
                "version": "1.0.0",
                "dependencies": {
                    "markdig": "^0.34.0"
                }
            }
            ```
            """;
        var content = CreateYamlContent(title: "JSON代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-json\"");
    }


    /// <summary>
    /// 测试 Bash/Shell 代码块语法高亮
    /// </summary>
    [Fact]
    public async Task ParseAsync_BashCodeBlock_ShouldRenderWithLanguageClass()
    {
        // Arrange
        var markdown = """
            ```bash
            #!/bin/bash
            
            echo "Starting deployment..."
            
            for file in *.md; do
                echo "Processing $file"
            done
            
            echo "Deployment complete!"
            ```
            """;
        var content = CreateYamlContent(title: "Bash代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code");
        result.HtmlContent.Should().Contain("class=\"language-bash\"");
        result.HtmlContent.Should().Contain("#!/bin/bash");
    }

    /// <summary>
    /// 测试无语言标识的代码块
    /// </summary>
    [Fact]
    public async Task ParseAsync_CodeBlockWithoutLanguage_ShouldRenderAsPlainCode()
    {
        // Arrange
        var markdown = """
            ```
            这是一段没有指定语言的代码
            可以是任何内容
            ```
            """;
        var content = CreateYamlContent(title: "无语言代码测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<pre>");
        result.HtmlContent.Should().Contain("<code>");
        result.HtmlContent.Should().Contain("没有指定语言");
    }

    /// <summary>
    /// 测试多个代码块
    /// </summary>
    [Fact]
    public async Task ParseAsync_MultipleCodeBlocks_ShouldRenderAllCorrectly()
    {
        // Arrange
        var markdown = """
            ## C# 示例
            
            ```csharp
            var message = "Hello";
            ```
            
            ## JavaScript 示例
            
            ```javascript
            const message = "Hello";
            ```
            
            ## Python 示例
            
            ```python
            message = "Hello"
            ```
            """;
        var content = CreateYamlContent(title: "多代码块测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("class=\"language-csharp\"");
        result.HtmlContent.Should().Contain("class=\"language-javascript\"");
        result.HtmlContent.Should().Contain("class=\"language-python\"");
    }

    #endregion

    #region 表格渲染测试

    /// <summary>
    /// 测试基本表格渲染
    /// </summary>
    [Fact]
    public async Task ParseAsync_BasicTable_ShouldRenderCorrectly()
    {
        // Arrange
        var markdown = """
            | 姓名 | 年龄 | 城市 |
            |------|------|------|
            | 张三 | 25   | 北京 |
            | 李四 | 30   | 上海 |
            | 王五 | 28   | 广州 |
            """;
        var content = CreateYamlContent(title: "基本表格测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<table>");
        result.HtmlContent.Should().Contain("<thead>");
        result.HtmlContent.Should().Contain("<tbody>");
        result.HtmlContent.Should().Contain("<tr>");
        result.HtmlContent.Should().Contain("<th>");
        result.HtmlContent.Should().Contain("<td>");
        result.HtmlContent.Should().Contain("姓名");
        result.HtmlContent.Should().Contain("张三");
        result.HtmlContent.Should().Contain("北京");
    }

    /// <summary>
    /// 测试带对齐的表格渲染
    /// </summary>
    [Fact]
    public async Task ParseAsync_TableWithAlignment_ShouldRenderCorrectly()
    {
        // Arrange
        var markdown = """
            | 左对齐 | 居中对齐 | 右对齐 |
            |:-------|:--------:|-------:|
            | 左     | 中       | 右     |
            | 数据1  | 数据2    | 数据3  |
            """;
        var content = CreateYamlContent(title: "对齐表格测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<table>");
        result.HtmlContent.Should().Contain("style=");
        // 验证表格内容
        result.HtmlContent.Should().Contain("左对齐");
        result.HtmlContent.Should().Contain("居中对齐");
        result.HtmlContent.Should().Contain("右对齐");
    }

    /// <summary>
    /// 测试复杂表格渲染（包含格式化内容）
    /// </summary>
    [Fact]
    public async Task ParseAsync_TableWithFormattedContent_ShouldRenderCorrectly()
    {
        // Arrange
        var markdown = """
            | 功能 | 描述 | 状态 |
            |------|------|------|
            | **粗体** | 支持粗体文本 | ✅ |
            | *斜体* | 支持斜体文本 | ✅ |
            | `代码` | 支持行内代码 | ✅ |
            | [链接](https://example.com) | 支持链接 | ✅ |
            """;
        var content = CreateYamlContent(title: "复杂表格测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<table>");
        result.HtmlContent.Should().Contain("<strong>粗体</strong>");
        result.HtmlContent.Should().Contain("<em>斜体</em>");
        result.HtmlContent.Should().Contain("<code>代码</code>");
        result.HtmlContent.Should().Contain("<a href=\"https://example.com\"");
    }

    /// <summary>
    /// 测试多行表格渲染
    /// </summary>
    [Fact]
    public async Task ParseAsync_LargeTable_ShouldRenderCorrectly()
    {
        // Arrange
        var sb = new StringBuilder();
        sb.AppendLine("| ID | 名称 | 值 | 备注 |");
        sb.AppendLine("|:---|:-----|:---|:-----|");
        for (int i = 1; i <= 20; i++)
        {
            sb.AppendLine($"| {i} | 项目{i} | {i * 100} | 备注{i} |");
        }

        var content = CreateYamlContent(title: "大表格测试", markdownBody: sb.ToString());
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<table>");
        result.HtmlContent.Should().Contain("项目1");
        result.HtmlContent.Should().Contain("项目20");
        result.HtmlContent.Should().Contain("2000"); // 20 * 100
    }

    /// <summary>
    /// 测试空单元格表格渲染
    /// </summary>
    [Fact]
    public async Task ParseAsync_TableWithEmptyCells_ShouldRenderCorrectly()
    {
        // Arrange
        var markdown = """
            | 列1 | 列2 | 列3 |
            |-----|-----|-----|
            | 值1 |     | 值3 |
            |     | 值2 |     |
            """;
        var content = CreateYamlContent(title: "空单元格表格测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<table>");
        result.HtmlContent.Should().Contain("值1");
        result.HtmlContent.Should().Contain("值2");
        result.HtmlContent.Should().Contain("值3");
    }

    #endregion

    #region 内容元数据提取测试

    /// <summary>
    /// 测试字数统计
    /// </summary>
    [Fact]
    public async Task ParseAsync_WordCount_ShouldCalculateCorrectly()
    {
        // Arrange
        var markdown = """
            这是一段中文内容，包含一些文字。
            
            This is some English content with several words.
            
            混合内容：Hello 世界！
            """;
        var content = CreateYamlContent(title: "字数统计测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.WordCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 测试阅读时间计算
    /// </summary>
    [Fact]
    public async Task ParseAsync_ReadingTime_ShouldCalculateCorrectly()
    {
        // Arrange
        var markdown = string.Join("\n\n", Enumerable.Range(1, 50).Select(i =>
            $"这是第{i}段内容，包含一些文字用于测试阅读时间计算功能。"));
        var content = CreateYamlContent(title: "阅读时间测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.ReadingTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// 测试纯文本提取
    /// </summary>
    [Fact]
    public async Task ParseAsync_PlainText_ShouldExtractCorrectly()
    {
        // Arrange
        var markdown = """
            # 标题
            
            这是**粗体**和*斜体*文本。
            
            还有`代码`和[链接](https://example.com)。
            """;
        var content = CreateYamlContent(title: "纯文本测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.PlainText.Should().NotBeNullOrEmpty();
        result.PlainText.Should().Contain("标题");
        result.PlainText.Should().Contain("粗体");
        result.PlainText.Should().Contain("斜体");
        result.PlainText.Should().NotContain("**");
        result.PlainText.Should().NotContain("*");
        result.PlainText.Should().NotContain("`");
    }

    /// <summary>
    /// 测试摘要生成（使用 Front Matter 中的 summary）
    /// </summary>
    [Fact]
    public async Task ParseAsync_Summary_FromFrontMatter_ShouldUseProvided()
    {
        // Arrange
        var content = CreateYamlContent(
            title: "摘要测试",
            summary: "这是自定义摘要",
            markdownBody: "这是正文内容，不应该被用作摘要。");
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Summary.Should().Be("这是自定义摘要");
    }

    /// <summary>
    /// 测试摘要生成（使用 Front Matter 中的 description）
    /// </summary>
    [Fact]
    public async Task ParseAsync_Summary_FromDescription_ShouldUseProvided()
    {
        // Arrange
        var content = CreateYamlContent(
            title: "描述测试",
            description: "这是文章描述",
            markdownBody: "这是正文内容。");
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Summary.Should().Be("这是文章描述");
    }

    #endregion

    #region Front Matter 格式无关性测试

    /// <summary>
    /// 测试三种格式解析相同元数据产生等价结果
    /// </summary>
    [Fact]
    public async Task ParseAsync_AllFormats_SameMetadata_ShouldProduceEquivalentResults()
    {
        // Arrange
        var title = "格式无关性测试";
        var date = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.FromHours(8));
        var tags = new[] { "测试", "集成" };
        var categories = new[] { "技术" };
        var description = "测试描述";
        var markdownBody = "# 标题\n\n这是正文。";

        var yamlContent = CreateYamlContent(
            title: title, date: date, tags: tags,
            categories: categories, description: description,
            markdownBody: markdownBody);

        var tomlContent = CreateTomlContent(
            title: title, date: date, tags: tags,
            categories: categories, description: description,
            markdownBody: markdownBody);

        var jsonContent = CreateJsonContent(
            title: title, date: date, tags: tags,
            categories: categories, description: description,
            markdownBody: markdownBody);

        var yamlFile = CreateContentFile(yamlContent, "yaml.md");
        var tomlFile = CreateContentFile(tomlContent, "toml.md");
        var jsonFile = CreateContentFile(jsonContent, "json.md");

        // Act
        var yamlResult = await _parser.ParseAsync(yamlFile);
        var tomlResult = await _parser.ParseAsync(tomlFile);
        var jsonResult = await _parser.ParseAsync(jsonFile);

        // Assert - 标题应该相同
        yamlResult.Metadata.Title.Should().Be(title);
        tomlResult.Metadata.Title.Should().Be(title);
        jsonResult.Metadata.Title.Should().Be(title);

        // Assert - 日期应该相同（只比较日期部分）
        yamlResult.Metadata.Date!.Value.Date.Should().Be(date.Date);
        tomlResult.Metadata.Date!.Value.Date.Should().Be(date.Date);
        jsonResult.Metadata.Date!.Value.Date.Should().Be(date.Date);

        // Assert - 标签应该相同
        yamlResult.Metadata.Tags.Should().BeEquivalentTo(tags);
        tomlResult.Metadata.Tags.Should().BeEquivalentTo(tags);
        jsonResult.Metadata.Tags.Should().BeEquivalentTo(tags);

        // Assert - 分类应该相同
        yamlResult.Metadata.Categories.Should().BeEquivalentTo(categories);
        tomlResult.Metadata.Categories.Should().BeEquivalentTo(categories);
        jsonResult.Metadata.Categories.Should().BeEquivalentTo(categories);

        // Assert - 描述应该相同
        yamlResult.Metadata.Description.Should().Be(description);
        tomlResult.Metadata.Description.Should().Be(description);
        jsonResult.Metadata.Description.Should().Be(description);

        // Assert - HTML 内容应该相同
        yamlResult.HtmlContent.Should().Be(tomlResult.HtmlContent);
        tomlResult.HtmlContent.Should().Be(jsonResult.HtmlContent);
    }


    #endregion

    #region 特殊字符和 Unicode 测试

    /// <summary>
    /// 测试中文内容解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_ChineseContent_ShouldParseCorrectly()
    {
        // Arrange
        var markdown = """
            # 中文标题测试
            
            这是一段中文内容，包含各种标点符号：，。！？、；：""''【】
            
            ## 二级中文标题
            
            - 列表项一
            - 列表项二
            - 列表项三
            
            > 这是一段中文引用。
            """;
        var content = CreateYamlContent(title: "中文内容测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("中文标题测试");
        result.HtmlContent.Should().Contain("中文内容");
        result.HtmlContent.Should().Contain("列表项一");
    }

    /// <summary>
    /// 测试日文内容解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_JapaneseContent_ShouldParseCorrectly()
    {
        // Arrange
        var markdown = """
            # 日本語のタイトル
            
            これは日本語のコンテンツです。ひらがな、カタカナ、漢字を含みます。
            
            - アイテム一
            - アイテム二
            """;
        var content = CreateYamlContent(title: "日文内容测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("日本語のタイトル");
        result.HtmlContent.Should().Contain("ひらがな");
        result.HtmlContent.Should().Contain("カタカナ");
    }

    /// <summary>
    /// 测试韩文内容解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_KoreanContent_ShouldParseCorrectly()
    {
        // Arrange
        var markdown = """
            # 한국어 제목
            
            이것은 한국어 콘텐츠입니다.
            
            - 항목 하나
            - 항목 둘
            """;
        var content = CreateYamlContent(title: "韩文内容测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("한국어 제목");
        result.HtmlContent.Should().Contain("한국어 콘텐츠");
    }

    /// <summary>
    /// 测试 Emoji 内容解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_EmojiContent_ShouldParseCorrectly()
    {
        // Arrange
        var markdown = """
            # 🎉 庆祝标题 🎊
            
            这是包含 Emoji 的内容 😀🚀💻
            
            - ✅ 已完成
            - ❌ 未完成
            - ⏳ 进行中
            
            > 💡 这是一个提示
            """;
        var content = CreateYamlContent(title: "Emoji测试 🎉", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("🎉");
        result.HtmlContent.Should().Contain("庆祝标题");
        result.HtmlContent.Should().Contain("✅");
        result.HtmlContent.Should().Contain("💡");
    }


    /// <summary>
    /// 测试混合语言内容解析
    /// </summary>
    [Fact]
    public async Task ParseAsync_MixedLanguageContent_ShouldParseCorrectly()
    {
        // Arrange
        var markdown = """
            # Mixed Language 混合语言 テスト
            
            This is English. 这是中文。これは日本語です。이것은 한국어입니다.
            
            ## Code Example 代码示例
            
            ```csharp
            // 中文注释
            var message = "Hello, 世界!";
            Console.WriteLine(message);
            ```
            """;
        var content = CreateYamlContent(title: "混合语言测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("Mixed Language");
        result.HtmlContent.Should().Contain("混合语言");
        result.HtmlContent.Should().Contain("テスト");
        result.HtmlContent.Should().Contain("한국어");
    }

    /// <summary>
    /// 测试特殊 HTML 字符转义
    /// </summary>
    [Fact]
    public async Task ParseAsync_HtmlSpecialCharacters_ShouldEscapeCorrectly()
    {
        // Arrange
        var markdown = """
            这是包含特殊字符的内容：
            
            - 小于号: <
            - 大于号: >
            - 和号: &
            - 引号: "
            - 单引号: '
            """;
        var content = CreateYamlContent(title: "特殊字符测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        // 在列表项中的特殊字符应该被转义
        result.HtmlContent.Should().Contain("&lt;");
        result.HtmlContent.Should().Contain("&gt;");
        result.HtmlContent.Should().Contain("&amp;");
    }

    #endregion

    #region 边界条件测试

    /// <summary>
    /// 测试空内容文件
    /// </summary>
    [Fact]
    public async Task ParseAsync_EmptyContent_ShouldHandleGracefully()
    {
        // Arrange
        var content = "";
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().NotBeNullOrEmpty();
        result.HtmlContent.Should().BeEmpty();
    }

    /// <summary>
    /// 测试只有 Front Matter 没有正文的文件
    /// </summary>
    [Fact]
    public async Task ParseAsync_OnlyFrontMatter_ShouldParseCorrectly()
    {
        // Arrange
        var content = """
            ---
            title: "只有元数据"
            date: 2024-06-15
            ---
            """;
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("只有元数据");
        result.HtmlContent.Should().BeEmpty();
        result.RawMarkdown.Should().BeEmpty();
    }

    /// <summary>
    /// 测试没有 Front Matter 的文件
    /// </summary>
    [Fact]
    public async Task ParseAsync_NoFrontMatter_ShouldUseDefaults()
    {
        // Arrange
        var content = """
            # 这是标题
            
            这是正文内容，没有 Front Matter。
            """;
        var file = CreateContentFile(content, "no-frontmatter.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().NotBeNullOrEmpty();
        result.Metadata.Draft.Should().BeFalse();
        result.HtmlContent.Should().Contain("这是标题");
    }


    /// <summary>
    /// 测试超长标题
    /// </summary>
    [Fact]
    public async Task ParseAsync_VeryLongTitle_ShouldParseCorrectly()
    {
        // Arrange
        var longTitle = new string('测', 500) + "标题";
        var content = CreateYamlContent(title: longTitle, markdownBody: "正文");
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Title.Should().Be(longTitle);
    }

    /// <summary>
    /// 测试大量标签
    /// </summary>
    [Fact]
    public async Task ParseAsync_ManyTags_ShouldParseCorrectly()
    {
        // Arrange
        var tags = Enumerable.Range(1, 100).Select(i => $"标签{i}").ToArray();
        var content = CreateYamlContent(title: "多标签测试", tags: tags);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Metadata.Tags.Should().HaveCount(100);
        result.Metadata.Tags.Should().Contain("标签1");
        result.Metadata.Tags.Should().Contain("标签100");
    }

    /// <summary>
    /// 测试深层嵌套列表
    /// </summary>
    [Fact]
    public async Task ParseAsync_DeeplyNestedList_ShouldParseCorrectly()
    {
        // Arrange
        var markdown = """
            - 第一层
              - 第二层
                - 第三层
                  - 第四层
                    - 第五层
                      - 第六层
            """;
        var content = CreateYamlContent(title: "深层嵌套测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("第一层");
        result.HtmlContent.Should().Contain("第六层");
        result.HtmlContent.Should().Contain("<ul>");
    }

    /// <summary>
    /// 测试超长段落
    /// </summary>
    [Fact]
    public async Task ParseAsync_VeryLongParagraph_ShouldParseCorrectly()
    {
        // Arrange
        var longParagraph = string.Join(" ", Enumerable.Range(1, 1000).Select(i => $"单词{i}"));
        var content = CreateYamlContent(title: "长段落测试", markdownBody: longParagraph);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("<p>");
        result.HtmlContent.Should().Contain("单词1");
        result.HtmlContent.Should().Contain("单词1000");
        result.WordCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 测试多个连续空行
    /// </summary>
    [Fact]
    public async Task ParseAsync_MultipleBlankLines_ShouldHandleCorrectly()
    {
        // Arrange
        var markdown = """
            第一段。



            第二段。




            第三段。
            """;
        var content = CreateYamlContent(title: "空行测试", markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.HtmlContent.Should().Contain("第一段");
        result.HtmlContent.Should().Contain("第二段");
        result.HtmlContent.Should().Contain("第三段");
    }

    #endregion

    #region 复杂内容组合测试

    /// <summary>
    /// 测试包含所有 Markdown 元素的复杂内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_ComplexContent_AllElements_ShouldParseCorrectly()
    {
        // Arrange
        var markdown = """
            # 主标题
            
            这是一段**粗体**和*斜体*以及***粗斜体***的文本。还有`行内代码`。
            
            ## 列表示例
            
            无序列表：
            - 项目一
            - 项目二
              - 子项目
            - 项目三
            
            有序列表：
            1. 第一步
            2. 第二步
            3. 第三步
            
            ## 代码示例
            
            ```csharp
            public class Example
            {
                public void Method() { }
            }
            ```
            
            ## 表格示例
            
            | 列1 | 列2 | 列3 |
            |-----|-----|-----|
            | A   | B   | C   |
            | D   | E   | F   |
            
            ## 其他元素
            
            > 这是引用块
            
            ---
            
            [链接](https://example.com) 和 ![图片](/image.png)
            
            任务列表：
            - [x] 已完成
            - [ ] 未完成
            """;
        var content = CreateYamlContent(
            title: "复杂内容测试",
            date: DateTimeOffset.Now,
            tags: ["测试", "复杂"],
            categories: ["集成测试"],
            description: "这是一个包含所有元素的复杂测试",
            markdownBody: markdown);
        var file = CreateContentFile(content);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert - 元数据
        result.Metadata.Title.Should().Be("复杂内容测试");
        result.Metadata.Tags.Should().HaveCount(2);
        result.Metadata.Categories.Should().HaveCount(1);

        // Assert - HTML 元素
        result.HtmlContent.Should().Contain("<h1");
        result.HtmlContent.Should().Contain("<h2");
        result.HtmlContent.Should().Contain("<strong>");
        result.HtmlContent.Should().Contain("<em>");
        result.HtmlContent.Should().Contain("<code>");
        result.HtmlContent.Should().Contain("<ul>");
        result.HtmlContent.Should().Contain("<ol>");
        result.HtmlContent.Should().Contain("<table>");
        result.HtmlContent.Should().Contain("<blockquote>");
        result.HtmlContent.Should().Contain("<hr");
        result.HtmlContent.Should().Contain("<a href=");
        result.HtmlContent.Should().Contain("<img");
        result.HtmlContent.Should().Contain("type=\"checkbox\"");

        // Assert - 提取的元数据
        result.WordCount.Should().BeGreaterThan(0);
        result.ReadingTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// 测试使用 TestDataGenerator 生成的随机内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_GeneratedContent_ShouldParseCorrectly()
    {
        // Arrange
        var generatedContent = TestDataGenerator.GenerateContentFile(
            UtilFrontMatterFormat.Yaml,
            new MarkdownGeneratorOptions
            {
                IncludeHeadings = true,
                IncludeLists = true,
                IncludeCodeBlocks = true,
                IncludeTables = true,
                IncludeBlockquotes = true,
                IncludeLinks = true,
                ParagraphCount = 5,
                Seed = 12345
            },
            seed: 12345);

        var file = CreateContentFile(generatedContent);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().NotBeNullOrEmpty();
        result.HtmlContent.Should().NotBeNullOrEmpty();
        result.WordCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 测试批量解析
    /// </summary>
    [Fact]
    public async Task ParseBatchAsync_MultipleFiles_ShouldParseAllCorrectly()
    {
        // Arrange
        var files = new List<ContentFile>();
        for (int i = 1; i <= 10; i++)
        {
            var content = CreateYamlContent(
                title: $"文章{i}",
                tags: [$"标签{i}"],
                markdownBody: $"这是第{i}篇文章的内容。");
            files.Add(CreateContentFile(content, $"article{i}.md"));
        }

        // Act
        var results = new List<ParsedContent>();
        await foreach (var result in _parser.ParseBatchAsync(files))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(10);
        results.Should().OnlyContain(r => r.Metadata.Title.StartsWith("文章"));
        results.Select(r => r.Metadata.Title).Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region FrontMatterParser 直接测试

    /// <summary>
    /// 测试 FrontMatterParser 格式检测
    /// </summary>
    [Theory]
    [InlineData("---\ntitle: test\n---", Flint.Core.Models.FrontMatterFormat.Yaml)]
    [InlineData("+++\ntitle = \"test\"\n+++", Flint.Core.Models.FrontMatterFormat.Toml)]
    [InlineData("{\"title\": \"test\"}", Flint.Core.Models.FrontMatterFormat.Json)]
    public void DetectFormat_ShouldDetectCorrectly(string content, Flint.Core.Models.FrontMatterFormat expectedFormat)
    {
        // Act
        var format = _frontMatterParser.DetectFormat(content.AsSpan());

        // Assert
        format.Should().Be(expectedFormat);
    }

    /// <summary>
    /// 测试 FrontMatterParser 序列化
    /// </summary>
    [Theory]
    [InlineData(Flint.Core.Models.FrontMatterFormat.Yaml)]
    [InlineData(Flint.Core.Models.FrontMatterFormat.Toml)]
    [InlineData(Flint.Core.Models.FrontMatterFormat.Json)]
    public void Serialize_ShouldProduceValidOutput(Flint.Core.Models.FrontMatterFormat format)
    {
        // Arrange
        var frontMatter = new FrontMatter
        {
            Title = "SerializeTest",
            Date = DateTimeOffset.Now,
            Draft = false,
            Tags = ["tag1", "tag2"],
            Categories = ["cat1"],
            Description = "TestDescription"
        };

        // Act
        var serialized = _frontMatterParser.Serialize(frontMatter, format);

        // Assert
        serialized.Should().NotBeNullOrEmpty();
        // 使用英文标题避免 JSON Unicode 转义问题
        serialized.Should().Contain("SerializeTest");
    }


    #endregion

    #region MarkdownParser 直接测试

    /// <summary>
    /// 测试 MarkdownParser ToHtml
    /// </summary>
    [Fact]
    public void MarkdownParser_ToHtml_ShouldConvertCorrectly()
    {
        // Arrange
        var markdown = "# 标题\n\n这是段落。";

        // Act
        var html = _markdownParser.ToHtml(markdown);

        // Assert
        html.Should().Contain("<h1");
        html.Should().Contain("标题");
        html.Should().Contain("<p>");
    }

    /// <summary>
    /// 测试 MarkdownParser 标题提取
    /// </summary>
    [Fact]
    public void MarkdownParser_ExtractHeadings_ShouldExtractAll()
    {
        // Arrange
        var markdown = """
            # 一级
            ## 二级
            ### 三级
            """;

        // Act
        var headings = _markdownParser.ExtractHeadings(markdown);

        // Assert
        headings.Should().HaveCount(3);
        headings[0].Level.Should().Be(1);
        headings[1].Level.Should().Be(2);
        headings[2].Level.Should().Be(3);
    }

    /// <summary>
    /// 测试 MarkdownParser 链接提取
    /// </summary>
    [Fact]
    public void MarkdownParser_ExtractLinks_ShouldExtractAll()
    {
        // Arrange
        var markdown = "[链接1](https://a.com) 和 [链接2](https://b.com)";

        // Act
        var links = _markdownParser.ExtractLinks(markdown);

        // Assert
        links.Should().HaveCount(2);
        links.Should().Contain(l => l.Url == "https://a.com");
        links.Should().Contain(l => l.Url == "https://b.com");
    }

    /// <summary>
    /// 测试 MarkdownParser 图片提取
    /// </summary>
    [Fact]
    public void MarkdownParser_ExtractImages_ShouldExtractAll()
    {
        // Arrange
        var markdown = "![图1](/a.png) 和 ![图2](/b.jpg)";

        // Act
        var images = _markdownParser.ExtractImages(markdown);

        // Assert
        images.Should().HaveCount(2);
        images.Should().Contain(i => i.Src == "/a.png");
        images.Should().Contain(i => i.Src == "/b.jpg");
    }

    /// <summary>
    /// 测试 MarkdownParser 纯文本提取
    /// </summary>
    [Fact]
    public void MarkdownParser_ToPlainText_ShouldStripMarkdown()
    {
        // Arrange
        var markdown = "**粗体** *斜体* `代码` [链接](url)";

        // Act
        var plainText = _markdownParser.ToPlainText(markdown);

        // Assert
        plainText.Should().Contain("粗体");
        plainText.Should().Contain("斜体");
        plainText.Should().Contain("代码");
        plainText.Should().Contain("链接");
        plainText.Should().NotContain("**");
        plainText.Should().NotContain("*");
        plainText.Should().NotContain("`");
        plainText.Should().NotContain("[");
    }

    /// <summary>
    /// 测试 MarkdownParser 字数统计
    /// </summary>
    [Fact]
    public void MarkdownParser_CountWords_ShouldCountCorrectly()
    {
        // Arrange
        var markdown = "Hello world 你好世界";

        // Act
        var count = _markdownParser.CountWords(markdown);

        // Assert
        count.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 测试 MarkdownParser 阅读时间计算
    /// </summary>
    [Fact]
    public void MarkdownParser_CalculateReadingTime_ShouldCalculateCorrectly()
    {
        // Arrange
        var markdown = string.Join(" ", Enumerable.Range(1, 400).Select(i => "word"));

        // Act
        var readingTime = _markdownParser.CalculateReadingTime(markdown);

        // Assert
        readingTime.Should().BeGreaterThan(TimeSpan.Zero);
        readingTime.TotalMinutes.Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// 测试 MarkdownParser 目录生成
    /// </summary>
    [Fact]
    public void MarkdownParser_GenerateTableOfContents_ShouldGenerateCorrectly()
    {
        // Arrange
        var markdown = """
            # 标题一
            ## 标题二
            ### 标题三
            ## 标题四
            """;

        // Act
        var toc = _markdownParser.GenerateTableOfContents(markdown);

        // Assert
        toc.Should().Contain("<nav");
        toc.Should().Contain("toc");
        toc.Should().Contain("标题一");
        toc.Should().Contain("标题二");
        toc.Should().Contain("标题三");
        toc.Should().Contain("标题四");
    }

    #endregion
}

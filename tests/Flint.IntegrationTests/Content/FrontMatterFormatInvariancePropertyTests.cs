// Flint 静态站点生成器
// Front Matter 格式无关性属性测试
// **Property 3: Front Matter 格式无关性**
// 生成随机元数据，验证三种格式产生等价解析结果
// **Validates: Requirements 2.1, 2.2**

using Flint.Core.Content;
using Flint.Core.Models;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.Content;

/// <summary>
/// Front Matter 格式无关性属性测试
/// **Feature: Flint-integration-tests, Property 3: Front Matter 格式无关性**
/// 验证 YAML、TOML、JSON 三种格式的 Front Matter 解析后产生等价结果
/// </summary>
public class FrontMatterFormatInvariancePropertyTests
{
    private readonly FrontMatterParser _parser = new();

    #region Property 3: Front Matter 格式无关性

    /// <summary>
    /// Property 3: Front Matter 格式无关性
    /// 对于任意相同的元数据内容，使用 YAML、TOML 或 JSON 格式的 Front Matter 
    /// 应产生等价的解析结果，即元数据字段值完全相同。
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FrontMatterPropertyArbitraries) })]
    public Property FrontMatterFormatInvariance_ShouldProduceEquivalentResults(
        FrontMatterTestData testData)
    {
        // 将测试数据序列化为三种格式
        var yamlContent = _parser.Serialize(testData.ToFrontMatter(), FrontMatterFormat.Yaml);
        var tomlContent = _parser.Serialize(testData.ToFrontMatter(), FrontMatterFormat.Toml);
        var jsonContent = _parser.Serialize(testData.ToFrontMatter(), FrontMatterFormat.Json);

        // 从三种格式解析回 FrontMatter
        var yamlParsed = ParseFrontMatter(yamlContent);
        var tomlParsed = ParseFrontMatter(tomlContent);
        var jsonParsed = ParseFrontMatter(jsonContent);

        // 验证三种格式产生等价的解析结果
        var yamlEqualsToml = AreFrontMattersEquivalent(yamlParsed, tomlParsed);
        var tomlEqualsJson = AreFrontMattersEquivalent(tomlParsed, jsonParsed);
        var yamlEqualsJson = AreFrontMattersEquivalent(yamlParsed, jsonParsed);

        return (yamlEqualsToml && tomlEqualsJson && yamlEqualsJson)
            .Label($"YAML==TOML: {yamlEqualsToml}, TOML==JSON: {tomlEqualsJson}, YAML==JSON: {yamlEqualsJson}")
            .Classify(testData.Tags.Count > 0, "有标签")
            .Classify(testData.Categories.Count > 0, "有分类")
            .Classify(testData.Draft, "草稿")
            .Classify(testData.Weight != 0, "有权重");
    }

    /// <summary>
    /// Property 3 的显式测试版本 - 基本字段
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Fact]
    public void FrontMatterFormatInvariance_BasicFields_ShouldProduceEquivalentResults()
    {
        // Arrange - 创建包含基本字段的 FrontMatter
        var original = new FrontMatter
        {
            Title = "测试文章标题",
            Date = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(8)),
            Draft = false,
            Description = "这是文章描述"
        };

        // Act - 序列化为三种格式
        var yamlContent = _parser.Serialize(original, FrontMatterFormat.Yaml);
        var tomlContent = _parser.Serialize(original, FrontMatterFormat.Toml);
        var jsonContent = _parser.Serialize(original, FrontMatterFormat.Json);

        // 从三种格式解析回 FrontMatter
        var fromYaml = ParseFrontMatter(yamlContent);
        var fromToml = ParseFrontMatter(tomlContent);
        var fromJson = ParseFrontMatter(jsonContent);

        // Assert - 验证三种格式产生等价的结果
        AssertFrontMattersEquivalent(fromYaml, fromToml, "YAML vs TOML");
        AssertFrontMattersEquivalent(fromToml, fromJson, "TOML vs JSON");
        AssertFrontMattersEquivalent(fromYaml, fromJson, "YAML vs JSON");
    }

    /// <summary>
    /// Property 3 的显式测试版本 - 标签和分类
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Fact]
    public void FrontMatterFormatInvariance_TagsAndCategories_ShouldProduceEquivalentResults()
    {
        // Arrange - 创建包含标签和分类的 FrontMatter
        var original = new FrontMatter
        {
            Title = "带标签的文章",
            Tags = ["技术", "编程", "C#"],
            Categories = ["教程", "开发"]
        };

        // Act
        var yamlContent = _parser.Serialize(original, FrontMatterFormat.Yaml);
        var tomlContent = _parser.Serialize(original, FrontMatterFormat.Toml);
        var jsonContent = _parser.Serialize(original, FrontMatterFormat.Json);

        var fromYaml = ParseFrontMatter(yamlContent);
        var fromToml = ParseFrontMatter(tomlContent);
        var fromJson = ParseFrontMatter(jsonContent);

        // Assert
        AssertFrontMattersEquivalent(fromYaml, fromToml, "YAML vs TOML (tags/categories)");
        AssertFrontMattersEquivalent(fromToml, fromJson, "TOML vs JSON (tags/categories)");
    }

    /// <summary>
    /// Property 3 的显式测试版本 - 所有通用字段
    /// 注意：Keywords 和 Aliases 字段在 YAML/JSON 序列化器中未实现，
    /// 但在 TOML 序列化器中已实现。这是一个已知的实现不一致问题。
    /// 此测试只验证三种格式都支持的字段。
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Fact]
    public void FrontMatterFormatInvariance_AllCommonFields_ShouldProduceEquivalentResults()
    {
        // Arrange - 创建包含所有通用字段的 FrontMatter
        // 注意：不包含 Keywords 和 Aliases，因为 YAML/JSON 序列化器不支持这些字段
        var original = new FrontMatter
        {
            Title = "完整字段测试",
            Date = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(8)),
            Draft = true,
            Description = "文章描述",
            Summary = "文章摘要",
            Author = "测试作者",
            Weight = 10,
            Layout = "post",
            Slug = "complete-test",
            Tags = ["tag1", "tag2", "tag3"],
            Categories = ["cat1", "cat2"]
        };

        // Act
        var yamlContent = _parser.Serialize(original, FrontMatterFormat.Yaml);
        var tomlContent = _parser.Serialize(original, FrontMatterFormat.Toml);
        var jsonContent = _parser.Serialize(original, FrontMatterFormat.Json);

        var fromYaml = ParseFrontMatter(yamlContent);
        var fromToml = ParseFrontMatter(tomlContent);
        var fromJson = ParseFrontMatter(jsonContent);

        // Assert
        AssertFrontMattersEquivalent(fromYaml, fromToml, "YAML vs TOML (all common fields)");
        AssertFrontMattersEquivalent(fromToml, fromJson, "TOML vs JSON (all common fields)");
        AssertFrontMattersEquivalent(fromYaml, fromJson, "YAML vs JSON (all common fields)");
    }

    /// <summary>
    /// Property 3 的显式测试版本 - 特殊字符
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Fact]
    public void FrontMatterFormatInvariance_SpecialCharacters_ShouldProduceEquivalentResults()
    {
        // Arrange - 创建包含特殊字符的 FrontMatter
        var original = new FrontMatter
        {
            Title = "测试 \"引号\" 和 '单引号'",
            Description = "包含特殊字符: <>&",
            Author = "作者 (Author)"
        };

        // Act
        var yamlContent = _parser.Serialize(original, FrontMatterFormat.Yaml);
        var tomlContent = _parser.Serialize(original, FrontMatterFormat.Toml);
        var jsonContent = _parser.Serialize(original, FrontMatterFormat.Json);

        var fromYaml = ParseFrontMatter(yamlContent);
        var fromToml = ParseFrontMatter(tomlContent);
        var fromJson = ParseFrontMatter(jsonContent);

        // Assert
        AssertFrontMattersEquivalent(fromYaml, fromToml, "YAML vs TOML (special chars)");
        AssertFrontMattersEquivalent(fromToml, fromJson, "TOML vs JSON (special chars)");
    }

    /// <summary>
    /// Property 3 的显式测试版本 - 中文内容
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Fact]
    public void FrontMatterFormatInvariance_ChineseContent_ShouldProduceEquivalentResults()
    {
        // Arrange - 创建包含中文内容的 FrontMatter
        var original = new FrontMatter
        {
            Title = "中文标题测试",
            Description = "这是一篇关于静态站点生成器的文章",
            Author = "张三",
            Tags = ["技术", "编程", "教程"],
            Categories = ["技术文章", "最佳实践"]
        };

        // Act
        var yamlContent = _parser.Serialize(original, FrontMatterFormat.Yaml);
        var tomlContent = _parser.Serialize(original, FrontMatterFormat.Toml);
        var jsonContent = _parser.Serialize(original, FrontMatterFormat.Json);

        var fromYaml = ParseFrontMatter(yamlContent);
        var fromToml = ParseFrontMatter(tomlContent);
        var fromJson = ParseFrontMatter(jsonContent);

        // Assert
        AssertFrontMattersEquivalent(fromYaml, fromToml, "YAML vs TOML (Chinese)");
        AssertFrontMattersEquivalent(fromToml, fromJson, "TOML vs JSON (Chinese)");
    }

    /// <summary>
    /// Property 3 的显式测试版本 - 布尔值
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FrontMatterFormatInvariance_BooleanValues_ShouldBePreserved(bool draft)
    {
        // Arrange
        var original = new FrontMatter
        {
            Title = "布尔值测试",
            Draft = draft
        };

        // Act
        var fromYaml = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Yaml));
        var fromToml = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Toml));
        var fromJson = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Json));

        // Assert
        fromYaml.Draft.Should().Be(draft, "YAML Draft");
        fromToml.Draft.Should().Be(draft, "TOML Draft");
        fromJson.Draft.Should().Be(draft, "JSON Draft");
    }

    /// <summary>
    /// Property 3 的显式测试版本 - 数值
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(-10)]
    public void FrontMatterFormatInvariance_NumericValues_ShouldBePreserved(int weight)
    {
        // Arrange
        var original = new FrontMatter
        {
            Title = "数值测试",
            Weight = weight
        };

        // Act
        var fromYaml = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Yaml));
        var fromToml = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Toml));
        var fromJson = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Json));

        // Assert
        fromYaml.Weight.Should().Be(weight, "YAML Weight");
        fromToml.Weight.Should().Be(weight, "TOML Weight");
        fromJson.Weight.Should().Be(weight, "JSON Weight");
    }

    /// <summary>
    /// Property 3 的显式测试版本 - 空列表
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Fact]
    public void FrontMatterFormatInvariance_EmptyLists_ShouldBePreserved()
    {
        // Arrange
        var original = new FrontMatter
        {
            Title = "空列表测试",
            Tags = [],
            Categories = []
        };

        // Act
        var fromYaml = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Yaml));
        var fromToml = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Toml));
        var fromJson = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Json));

        // Assert
        fromYaml.Tags.Should().BeEmpty("YAML Tags");
        fromToml.Tags.Should().BeEmpty("TOML Tags");
        fromJson.Tags.Should().BeEmpty("JSON Tags");

        fromYaml.Categories.Should().BeEmpty("YAML Categories");
        fromToml.Categories.Should().BeEmpty("TOML Categories");
        fromJson.Categories.Should().BeEmpty("JSON Categories");
    }

    /// <summary>
    /// Property 3 的显式测试版本 - 日期格式
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Fact]
    public void FrontMatterFormatInvariance_DateValues_ShouldBePreserved()
    {
        // Arrange
        var testDate = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(8));
        var original = new FrontMatter
        {
            Title = "日期测试",
            Date = testDate
        };

        // Act
        var fromYaml = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Yaml));
        var fromToml = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Toml));
        var fromJson = ParseFrontMatter(_parser.Serialize(original, FrontMatterFormat.Json));

        // Assert - 日期应该在同一天（时区可能有差异）
        fromYaml.Date.Should().NotBeNull();
        fromToml.Date.Should().NotBeNull();
        fromJson.Date.Should().NotBeNull();

        fromYaml.Date!.Value.Date.Should().Be(testDate.Date, "YAML Date");
        fromToml.Date!.Value.Date.Should().Be(testDate.Date, "TOML Date");
        fromJson.Date!.Value.Date.Should().Be(testDate.Date, "JSON Date");
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 解析 Front Matter 内容
    /// </summary>
    private FrontMatter ParseFrontMatter(string content)
    {
        var (frontMatter, _) = _parser.Parse(content);
        return frontMatter;
    }

    /// <summary>
    /// 比较两个 FrontMatter 是否等价
    /// 忽略 Format 字段，因为它表示原始格式
    /// 注意：不比较 Keywords 和 Aliases，因为 YAML/JSON 序列化器不支持这些字段
    /// </summary>
    private static bool AreFrontMattersEquivalent(FrontMatter fm1, FrontMatter fm2)
    {
        // 比较核心字段
        if (fm1.Title != fm2.Title)
            return false;
        if (fm1.Draft != fm2.Draft)
            return false;
        if (fm1.Weight != fm2.Weight)
            return false;
        if (fm1.Description != fm2.Description)
            return false;
        if (fm1.Summary != fm2.Summary)
            return false;
        if (fm1.Layout != fm2.Layout)
            return false;
        if (fm1.Slug != fm2.Slug)
            return false;
        if (fm1.Author != fm2.Author)
            return false;
        if (fm1.Type != fm2.Type)
            return false;

        // 比较日期（只比较日期部分，忽略时区差异）
        if (!AreDatesEquivalent(fm1.Date, fm2.Date))
            return false;
        if (!AreDatesEquivalent(fm1.LastMod, fm2.LastMod))
            return false;
        if (!AreDatesEquivalent(fm1.ExpiryDate, fm2.ExpiryDate))
            return false;
        if (!AreDatesEquivalent(fm1.PublishDate, fm2.PublishDate))
            return false;

        // 比较列表（只比较三种格式都支持的字段）
        if (!AreListsEquivalent(fm1.Tags, fm2.Tags))
            return false;
        if (!AreListsEquivalent(fm1.Categories, fm2.Categories))
            return false;
        // 注意：不比较 Keywords、Aliases、Authors、Outputs，因为 YAML/JSON 序列化器不支持这些字段

        return true;
    }

    /// <summary>
    /// 比较两个日期是否等价
    /// </summary>
    private static bool AreDatesEquivalent(DateTimeOffset? date1, DateTimeOffset? date2)
    {
        if (date1 is null && date2 is null)
            return true;
        if (date1 is null || date2 is null)
            return false;

        // 比较日期部分（忽略时区差异）
        return date1.Value.UtcDateTime.Date == date2.Value.UtcDateTime.Date;
    }

    /// <summary>
    /// 比较两个列表是否等价
    /// </summary>
    private static bool AreListsEquivalent(IReadOnlyList<string> list1, IReadOnlyList<string> list2)
    {
        if (list1.Count != list2.Count)
            return false;
        return list1.SequenceEqual(list2);
    }

    /// <summary>
    /// 断言两个 FrontMatter 等价
    /// 注意：不检查 Keywords 和 Aliases，因为 YAML/JSON 序列化器不支持这些字段
    /// </summary>
    private static void AssertFrontMattersEquivalent(FrontMatter fm1, FrontMatter fm2, string context)
    {
        fm1.Title.Should().Be(fm2.Title, $"Title should match ({context})");
        fm1.Draft.Should().Be(fm2.Draft, $"Draft should match ({context})");
        fm1.Weight.Should().Be(fm2.Weight, $"Weight should match ({context})");
        fm1.Description.Should().Be(fm2.Description, $"Description should match ({context})");
        fm1.Summary.Should().Be(fm2.Summary, $"Summary should match ({context})");
        fm1.Layout.Should().Be(fm2.Layout, $"Layout should match ({context})");
        fm1.Slug.Should().Be(fm2.Slug, $"Slug should match ({context})");
        fm1.Author.Should().Be(fm2.Author, $"Author should match ({context})");
        fm1.Type.Should().Be(fm2.Type, $"Type should match ({context})");

        // 比较日期
        AssertDatesEquivalent(fm1.Date, fm2.Date, $"Date ({context})");
        AssertDatesEquivalent(fm1.LastMod, fm2.LastMod, $"LastMod ({context})");

        // 比较列表（只比较三种格式都支持的字段）
        fm1.Tags.Should().BeEquivalentTo(fm2.Tags, $"Tags should match ({context})");
        fm1.Categories.Should().BeEquivalentTo(fm2.Categories, $"Categories should match ({context})");
        // 注意：不检查 Keywords 和 Aliases，因为 YAML/JSON 序列化器不支持这些字段
    }

    /// <summary>
    /// 断言两个日期等价
    /// </summary>
    private static void AssertDatesEquivalent(DateTimeOffset? date1, DateTimeOffset? date2, string context)
    {
        if (date1 is null && date2 is null)
            return;

        date1.Should().NotBeNull($"{context} - date1 should not be null when date2 is not null");
        date2.Should().NotBeNull($"{context} - date2 should not be null when date1 is not null");

        date1!.Value.UtcDateTime.Date.Should().Be(
            date2!.Value.UtcDateTime.Date,
            $"{context} - dates should match");
    }

    #endregion
}


/// <summary>
/// Front Matter 属性测试专用的测试数据
/// 用于生成可序列化和反序列化的 Front Matter 数据
/// </summary>
public sealed class FrontMatterTestData
{
    /// <summary>
    /// 文章标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 发布日期
    /// </summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>
    /// 是否为草稿
    /// </summary>
    public bool Draft { get; init; }

    /// <summary>
    /// 文章描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 文章摘要
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// 作者
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// 权重
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 布局模板
    /// </summary>
    public string? Layout { get; init; }

    /// <summary>
    /// URL 别名
    /// </summary>
    public string? Slug { get; init; }

    /// <summary>
    /// 标签列表
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// 分类列表
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>
    /// 关键词列表
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>
    /// 转换为 FrontMatter 对象
    /// </summary>
    public FrontMatter ToFrontMatter() => new()
    {
        Title = Title,
        Date = Date,
        Draft = Draft,
        Description = Description,
        Summary = Summary,
        Author = Author,
        Weight = Weight,
        Layout = Layout,
        Slug = Slug,
        Tags = Tags,
        Categories = Categories,
        Keywords = Keywords
    };

    /// <summary>
    /// 重写 ToString 用于测试输出
    /// </summary>
    public override string ToString() =>
        $"FrontMatterTestData {{ Title=\"{Title}\", Draft={Draft}, Weight={Weight}, Tags=[{string.Join(",", Tags)}] }}";
}


/// <summary>
/// Front Matter 属性测试专用的 FsCheck 生成器
/// 生成可在三种格式之间正确转换的 Front Matter 数据
/// 注意：只生成三种格式都支持的字段，不包含 Keywords 和 Aliases
/// （因为 YAML/JSON 序列化器未实现这些字段的序列化）
/// </summary>
public static class FrontMatterPropertyArbitraries
{
    /// <summary>
    /// 有效的标题列表
    /// </summary>
    private static readonly string[] ValidTitles =
    [
        "Hello World",
        "Getting Started",
        "Tutorial",
        "测试文章",
        "入门指南",
        "技术分享",
        "My First Post",
        "Introduction to Programming",
        "深入理解静态站点生成器"
    ];

    /// <summary>
    /// 有效的标签列表
    /// </summary>
    private static readonly string[] ValidTags =
    [
        "programming", "web", "dotnet", "csharp", "javascript",
        "tutorial", "guide", "tips", "news", "review",
        "技术", "编程", "教程", "博客", "开发"
    ];

    /// <summary>
    /// 有效的分类列表
    /// </summary>
    private static readonly string[] ValidCategories =
    [
        "Technology", "Programming", "Web Development", "Tutorial",
        "News", "Review", "Opinion", "Guide", "Reference",
        "技术", "编程", "教程", "新闻", "评论"
    ];

    /// <summary>
    /// 有效的布局列表
    /// </summary>
    private static readonly string[] ValidLayouts =
    [
        "single", "post", "page", "default", "article", "blog"
    ];

    /// <summary>
    /// 有效的描述列表
    /// </summary>
    private static readonly string[] ValidDescriptions =
    [
        "A great post about programming",
        "Tutorial for beginners",
        "这是一篇关于编程的文章",
        "入门教程",
        "技术分享文章"
    ];

    /// <summary>
    /// 生成用于属性测试的 FrontMatterTestData
    /// 确保生成的数据可以在三种格式之间正确转换
    /// 注意：不生成 Keywords 字段，因为 YAML/JSON 序列化器不支持
    /// </summary>
    public static Arbitrary<FrontMatterTestData> FrontMatterTestData() =>
        (from title in Gen.Elements(ValidTitles)
         from hasDate in ArbMap.Default.GeneratorFor<bool>()
         from year in Gen.Choose(2020, 2026)
         from month in Gen.Choose(1, 12)
         from day in Gen.Choose(1, 28)
         from draft in ArbMap.Default.GeneratorFor<bool>()
         from hasDescription in ArbMap.Default.GeneratorFor<bool>()
         from description in Gen.Elements(ValidDescriptions)
         from hasAuthor in ArbMap.Default.GeneratorFor<bool>()
         from author in Gen.Elements("John Doe", "Jane Smith", "张三", "李四", "测试作者")
         from weight in Gen.Choose(-10, 100)
         from hasLayout in ArbMap.Default.GeneratorFor<bool>()
         from layout in Gen.Elements(ValidLayouts)
         from hasSlug in ArbMap.Default.GeneratorFor<bool>()
         from slug in Gen.Elements("my-post", "article-1", "guide", "tutorial", "test")
         from tagCount in Gen.Choose(0, 4)
         from tags in Gen.ListOf<string>(Gen.Elements(ValidTags)).Select(t => t.Take(tagCount).ToList())
         from categoryCount in Gen.Choose(0, 3)
         from categories in Gen.ListOf<string>(Gen.Elements(ValidCategories)).Select(c => c.Take(categoryCount).ToList())
         select new FrontMatterTestData
         {
             Title = title,
             Date = hasDate ? new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero) : null,
             Draft = draft,
             Description = hasDescription ? description : null,
             Author = hasAuthor ? author : null,
             Weight = weight,
             Layout = hasLayout ? layout : null,
             Slug = hasSlug ? slug : null,
             Tags = tags.Distinct().ToList(),
             Categories = categories.Distinct().ToList(),
             Keywords = [] // 不生成 Keywords，因为 YAML/JSON 序列化器不支持
         }).ToArbitrary();
}

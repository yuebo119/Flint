// Flint 静态站点生成器
// Front Matter 解析器属性测试
// **Property 2: Front Matter 解析往返一致性**
// **验证: 需求 2.3, 2.4, 2.5, 2.12**

using Flint.Core.Content;
using Flint.Core.Models;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Content;

/// <summary>
/// Front Matter 解析器属性测试
/// 验证 YAML、TOML、JSON 三种格式的往返一致性
/// </summary>
public class FrontMatterParserPropertyTests
{
    private readonly FrontMatterParser _parser = new();

    #region Property 2: Front Matter 解析往返一致性

    /// <summary>
    /// **Property 2: Front Matter 解析往返一致性 (YAML)**
    /// 对于任意有效的 Front Matter 数据，解析后再序列化应该产生等价的数据结构
    /// **验证: 需求 2.3, 2.12**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidFrontMatterArbitrary)])]
    public Property YamlRoundTrip_ShouldPreserveFrontMatter(ValidFrontMatter validFm)
    {
        ArgumentNullException.ThrowIfNull(validFm);

        // Arrange
        var original = validFm.Value;

        // Act - 序列化为 YAML，然后解析回来
        var yaml = _parser.Serialize(original, FrontMatterFormat.Yaml);
        var parseSuccess = _parser.TryParse(yaml.AsSpan(), out var parsed, out _);

        // Assert - 核心属性应该保持一致
        return (parseSuccess &&
                parsed is not null &&
                parsed.Title == original.Title &&
                parsed.Draft == original.Draft &&
                CompareDateTimeOffset(parsed.Date, original.Date) &&
                CompareStringLists(parsed.Tags, original.Tags) &&
                CompareStringLists(parsed.Categories, original.Categories) &&
                parsed.Description == original.Description &&
                parsed.Layout == original.Layout &&
                parsed.Slug == original.Slug &&
                parsed.Author == original.Author &&
                parsed.Weight == original.Weight)
            .ToProperty()
            .Label($"YAML 往返一致性: Title={original.Title}, Tags={string.Join(",", original.Tags)}");
    }

    /// <summary>
    /// **Property 2: Front Matter 解析往返一致性 (TOML)**
    /// 对于任意有效的 Front Matter 数据，解析后再序列化应该产生等价的数据结构
    /// **验证: 需求 2.4, 2.12**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidFrontMatterArbitrary)])]
    public Property TomlRoundTrip_ShouldPreserveFrontMatter(ValidFrontMatter validFm)
    {
        ArgumentNullException.ThrowIfNull(validFm);

        // Arrange
        var original = validFm.Value;

        // Act - 序列化为 TOML，然后解析回来
        var toml = _parser.Serialize(original, FrontMatterFormat.Toml);
        var parseSuccess = _parser.TryParse(toml.AsSpan(), out var parsed, out _);

        // 如果解析失败，返回失败
        if (!parseSuccess || parsed is null)
        {
            return false.ToProperty().Label($"TOML 解析失败: {toml}");
        }

        // Assert - 核心属性应该保持一致
        var titleMatch = parsed.Title == original.Title;
        var draftMatch = parsed.Draft == original.Draft;
        var dateMatch = CompareDateTimeOffset(parsed.Date, original.Date);
        var tagsMatch = CompareStringLists(parsed.Tags, original.Tags);
        var categoriesMatch = CompareStringLists(parsed.Categories, original.Categories);
        // Description 比较需要处理 null 和空字符串的情况
        var descriptionMatch = (parsed.Description ?? "") == (original.Description ?? "");

        // 构建详细的诊断信息
        var diagnostics = new List<string>();
        if (!titleMatch)
            diagnostics.Add($"Title: '{parsed.Title}' != '{original.Title}'");
        if (!draftMatch)
            diagnostics.Add($"Draft: {parsed.Draft} != {original.Draft}");
        if (!dateMatch)
            diagnostics.Add($"Date: {parsed.Date} != {original.Date}");
        if (!tagsMatch)
            diagnostics.Add($"Tags: [{string.Join(",", parsed.Tags)}] != [{string.Join(",", original.Tags)}]");
        if (!categoriesMatch)
            diagnostics.Add($"Categories: [{string.Join(",", parsed.Categories)}] != [{string.Join(",", original.Categories)}]");
        if (!descriptionMatch)
            diagnostics.Add($"Description: '{parsed.Description}' != '{original.Description}'");

        var allMatch = titleMatch && draftMatch && dateMatch && tagsMatch && categoriesMatch && descriptionMatch;

        return allMatch
            .ToProperty()
            .Label(allMatch
                ? $"TOML 往返一致性: Title={original.Title}, Draft={original.Draft}"
                : $"TOML 往返失败: {string.Join("; ", diagnostics)}");
    }

    /// <summary>
    /// **Property 2: Front Matter 解析往返一致性 (JSON)**
    /// 对于任意有效的 Front Matter 数据，解析后再序列化应该产生等价的数据结构
    /// **验证: 需求 2.5, 2.12**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidFrontMatterArbitrary)])]
    public Property JsonRoundTrip_ShouldPreserveFrontMatter(ValidFrontMatter validFm)
    {
        ArgumentNullException.ThrowIfNull(validFm);

        // Arrange
        var original = validFm.Value;

        // Act - 序列化为 JSON，然后解析回来
        var json = _parser.Serialize(original, FrontMatterFormat.Json);
        var parseSuccess = _parser.TryParse(json.AsSpan(), out var parsed, out _);

        // Assert - 核心属性应该保持一致
        return (parseSuccess &&
                parsed is not null &&
                parsed.Title == original.Title &&
                parsed.Draft == original.Draft &&
                CompareDateTimeOffset(parsed.Date, original.Date) &&
                CompareStringLists(parsed.Tags, original.Tags) &&
                CompareStringLists(parsed.Categories, original.Categories) &&
                parsed.Description == original.Description &&
                parsed.Layout == original.Layout &&
                parsed.Slug == original.Slug &&
                parsed.Author == original.Author &&
                parsed.Weight == original.Weight)
            .ToProperty()
            .Label($"JSON 往返一致性: Title={original.Title}, Weight={original.Weight}");
    }

    #endregion

    #region 跨格式一致性测试

    /// <summary>
    /// **Property 2 扩展: 跨格式一致性**
    /// 同一 Front Matter 在不同格式间转换应该保持核心数据一致
    /// **验证: 需求 2.3, 2.4, 2.5, 2.12**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(ValidFrontMatterArbitrary)])]
    public Property CrossFormatConsistency_ShouldPreserveCoreData(ValidFrontMatter validFm)
    {
        ArgumentNullException.ThrowIfNull(validFm);

        // Arrange
        var original = validFm.Value;

        // Act - YAML -> TOML -> JSON -> 解析
        var yaml = _parser.Serialize(original, FrontMatterFormat.Yaml);
        if (!_parser.TryParse(yaml.AsSpan(), out var fromYaml, out _) || fromYaml is null)
        {
            return false.ToProperty().Label("YAML 解析失败");
        }

        var toml = _parser.Serialize(fromYaml, FrontMatterFormat.Toml);
        if (!_parser.TryParse(toml.AsSpan(), out var fromToml, out _) || fromToml is null)
        {
            return false.ToProperty().Label("TOML 解析失败");
        }

        var json = _parser.Serialize(fromToml, FrontMatterFormat.Json);
        if (!_parser.TryParse(json.AsSpan(), out var fromJson, out _) || fromJson is null)
        {
            return false.ToProperty().Label("JSON 解析失败");
        }

        // Assert - 最终结果应该与原始数据一致
        return (fromJson.Title == original.Title &&
                fromJson.Draft == original.Draft &&
                CompareStringLists(fromJson.Tags, original.Tags) &&
                CompareStringLists(fromJson.Categories, original.Categories))
            .ToProperty()
            .Label("跨格式转换一致性: YAML -> TOML -> JSON");
    }

    #endregion

    #region 格式检测测试

    /// <summary>
    /// 格式检测应该正确识别 Front Matter 格式
    /// **验证: 需求 2.3, 2.4, 2.5**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(ValidFrontMatterArbitrary)])]
    public Property DetectFormat_ShouldIdentifyCorrectFormat(ValidFrontMatter validFm, FrontMatterFormat format)
    {
        ArgumentNullException.ThrowIfNull(validFm);

        // Arrange
        var original = validFm.Value;

        // Act - 序列化为指定格式
        var serialized = _parser.Serialize(original, format);
        var detectedFormat = _parser.DetectFormat(serialized.AsSpan());

        // Assert - 检测到的格式应该与序列化格式一致
        return (detectedFormat == format)
            .ToProperty()
            .Label($"格式检测: 期望 {format}, 检测到 {detectedFormat}");
    }

    #endregion

    #region 边界情况测试

    /// <summary>
    /// 空标签和分类列表应该正确处理
    /// **验证: 需求 2.12**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(ValidFrontMatterArbitrary)])]
    public Property EmptyLists_ShouldBePreserved(ValidFrontMatterWithEmptyLists validFm)
    {
        ArgumentNullException.ThrowIfNull(validFm);

        // Arrange
        var original = validFm.Value;

        // Act - 测试所有三种格式
        var yamlSuccess = TestRoundTrip(original, FrontMatterFormat.Yaml);
        var tomlSuccess = TestRoundTrip(original, FrontMatterFormat.Toml);
        var jsonSuccess = TestRoundTrip(original, FrontMatterFormat.Json);

        return (yamlSuccess && tomlSuccess && jsonSuccess)
            .ToProperty()
            .Label("空列表往返一致性");
    }

    /// <summary>
    /// 特殊字符应该正确转义和解析
    /// **验证: 需求 2.12**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(ValidFrontMatterArbitrary)])]
    public Property SpecialCharacters_ShouldBeEscapedCorrectly(ValidFrontMatterWithSpecialChars validFm)
    {
        ArgumentNullException.ThrowIfNull(validFm);

        // Arrange
        var original = validFm.Value;

        // Act - 测试 JSON 格式（对特殊字符最敏感）
        var json = _parser.Serialize(original, FrontMatterFormat.Json);
        var parseSuccess = _parser.TryParse(json.AsSpan(), out var parsed, out _);

        return (parseSuccess &&
                parsed is not null &&
                parsed.Title == original.Title &&
                parsed.Description == original.Description)
            .ToProperty()
            .Label($"特殊字符处理: Title={original.Title}");
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 测试往返一致性
    /// </summary>
    private bool TestRoundTrip(FrontMatter original, FrontMatterFormat format)
    {
        var serialized = _parser.Serialize(original, format);
        var parseSuccess = _parser.TryParse(serialized.AsSpan(), out var parsed, out _);

        return parseSuccess &&
               parsed is not null &&
               parsed.Title == original.Title &&
               parsed.Draft == original.Draft;
    }

    /// <summary>
    /// 比较两个 DateTimeOffset 值（考虑时区差异）
    /// </summary>
    private static bool CompareDateTimeOffset(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;

        // 比较 UTC 时间，允许 1 秒的误差（序列化精度问题）
        return Math.Abs((a.Value.UtcDateTime - b.Value.UtcDateTime).TotalSeconds) < 1;
    }

    /// <summary>
    /// 比较两个字符串列表
    /// </summary>
    private static bool CompareStringLists(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        return a.SequenceEqual(b);
    }

    #endregion
}

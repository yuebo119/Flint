// 临时调试测试

using Flint.Core.Content;
using Flint.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace Flint.Core.Tests.Content;

public class TomlDebugTest
{
    private readonly ITestOutputHelper _output;
    private readonly FrontMatterParser _parser = new();

    public TomlDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Debug_TomlRoundTrip()
    {
        var fm = new FrontMatter
        {
            Title = "Quick Start",
            Draft = false,
            Tags = ["performance"]
        };

        var toml = _parser.Serialize(fm, FrontMatterFormat.Toml);
        _output.WriteLine("=== TOML Output ===");
        _output.WriteLine(toml);

        var success = _parser.TryParse(toml.AsSpan(), out var parsed, out var remaining);
        _output.WriteLine($"Parse success: {success}");
        _output.WriteLine($"Remaining: [{remaining.ToString()}]");

        if (parsed != null)
        {
            _output.WriteLine($"Parsed Title: {parsed.Title}");
            _output.WriteLine($"Parsed Tags: [{string.Join(",", parsed.Tags)}]");
            _output.WriteLine($"Parsed Draft: {parsed.Draft}");
        }
    }

    [Fact]
    public void Debug_TomlRoundTrip_EmptyTagsWithDraft()
    {
        // 这是失败的测试用例
        var fm = new FrontMatter
        {
            Title = "Advanced Topics",
            Draft = true,
            Tags = []
        };

        var toml = _parser.Serialize(fm, FrontMatterFormat.Toml);
        _output.WriteLine("=== TOML Output ===");
        _output.WriteLine(toml);

        var success = _parser.TryParse(toml.AsSpan(), out var parsed, out var remaining);
        _output.WriteLine($"Parse success: {success}");
        _output.WriteLine($"Remaining: [{remaining.ToString()}]");

        if (parsed != null)
        {
            _output.WriteLine($"Parsed Title: {parsed.Title}");
            _output.WriteLine($"Parsed Tags count: {parsed.Tags.Count}");
            _output.WriteLine($"Parsed Draft: {parsed.Draft}");
            _output.WriteLine($"Parsed Categories count: {parsed.Categories.Count}");
            _output.WriteLine($"Parsed Description: {parsed.Description ?? "null"}");
        }

        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal("Advanced Topics", parsed.Title);
        Assert.True(parsed.Draft);
        Assert.Empty(parsed.Tags);
        Assert.Empty(parsed.Categories);
    }

    [Fact]
    public void Debug_TomlRoundTrip_FailingCase()
    {
        // 这是 PBT 失败的测试用例
        var fm = new FrontMatter
        {
            Title = "Configuration Manual",
            Draft = false,
            Tags = []
        };

        var toml = _parser.Serialize(fm, FrontMatterFormat.Toml);
        _output.WriteLine("=== TOML Output ===");
        _output.WriteLine(toml);
        _output.WriteLine("=== END TOML ===");

        var success = _parser.TryParse(toml.AsSpan(), out var parsed, out var remaining);
        _output.WriteLine($"Parse success: {success}");
        _output.WriteLine($"Remaining length: {remaining.Length}");
        _output.WriteLine($"Remaining: [{remaining.ToString()}]");

        if (parsed != null)
        {
            _output.WriteLine($"Parsed Title: [{parsed.Title}]");
            _output.WriteLine($"Parsed Tags count: {parsed.Tags.Count}");
            _output.WriteLine($"Parsed Draft: {parsed.Draft}");
            _output.WriteLine($"Parsed Categories count: {parsed.Categories.Count}");
            _output.WriteLine($"Parsed Description: [{parsed.Description ?? "null"}]");
        }
        else
        {
            _output.WriteLine("Parsed is NULL!");
        }

        Assert.True(success, "Parse should succeed");
        Assert.NotNull(parsed);
        Assert.Equal("Configuration Manual", parsed.Title);
        Assert.False(parsed.Draft);
        Assert.Empty(parsed.Tags);
        Assert.Empty(parsed.Categories);
        Assert.Null(parsed.Description);
    }

    [Fact]
    public void Debug_TomlRoundTrip_WithAllFields()
    {
        // 测试带有所有字段的情况
        var fm = new FrontMatter
        {
            Title = "Configuration Manual",
            Draft = false,
            Tags = [],
            Categories = [],
            Description = "A brief introduction",
            Layout = "single",
            Slug = "config-manual",
            Author = "John Doe",
            Weight = 10,
            Date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero)
        };

        var toml = _parser.Serialize(fm, FrontMatterFormat.Toml);
        _output.WriteLine("=== TOML Output ===");
        _output.WriteLine(toml);
        _output.WriteLine("=== END TOML ===");

        var success = _parser.TryParse(toml.AsSpan(), out var parsed, out var remaining);
        _output.WriteLine($"Parse success: {success}");

        if (parsed != null)
        {
            _output.WriteLine($"Original Title: [{fm.Title}] vs Parsed: [{parsed.Title}]");
            _output.WriteLine($"Original Draft: [{fm.Draft}] vs Parsed: [{parsed.Draft}]");
            _output.WriteLine($"Original Date: [{fm.Date}] vs Parsed: [{parsed.Date}]");
            _output.WriteLine($"Original Tags: [{string.Join(",", fm.Tags)}] vs Parsed: [{string.Join(",", parsed.Tags)}]");
            _output.WriteLine($"Original Categories: [{string.Join(",", fm.Categories)}] vs Parsed: [{string.Join(",", parsed.Categories)}]");
            _output.WriteLine($"Original Description: [{fm.Description}] vs Parsed: [{parsed.Description}]");
            _output.WriteLine($"Original Layout: [{fm.Layout}] vs Parsed: [{parsed.Layout}]");
            _output.WriteLine($"Original Slug: [{fm.Slug}] vs Parsed: [{parsed.Slug}]");
            _output.WriteLine($"Original Author: [{fm.Author}] vs Parsed: [{parsed.Author}]");
            _output.WriteLine($"Original Weight: [{fm.Weight}] vs Parsed: [{parsed.Weight}]");

            // 检查每个字段
            _output.WriteLine($"Title match: {parsed.Title == fm.Title}");
            _output.WriteLine($"Draft match: {parsed.Draft == fm.Draft}");
            _output.WriteLine($"Description match: {parsed.Description == fm.Description}");
        }

        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal(fm.Title, parsed.Title);
        Assert.Equal(fm.Draft, parsed.Draft);
        Assert.Equal(fm.Description, parsed.Description);
    }

    [Fact]
    public void Debug_TomlRoundTrip_SimulatePBT()
    {
        // 模拟 PBT 生成的数据
        var fm = new FrontMatter
        {
            Title = "Configuration Manual",
            Draft = false,
            Tags = [],
            Categories = [],
            Description = null,
            Layout = null,
            Slug = null,
            Author = null,
            Weight = 0,
            Date = null
        };

        var toml = _parser.Serialize(fm, FrontMatterFormat.Toml);
        _output.WriteLine("=== TOML Output ===");
        _output.WriteLine(toml);
        _output.WriteLine("=== END TOML ===");

        var success = _parser.TryParse(toml.AsSpan(), out var parsed, out var remaining);
        _output.WriteLine($"Parse success: {success}");

        if (parsed != null)
        {
            // 检查 PBT 测试中的所有条件
            var titleMatch = parsed.Title == fm.Title;
            var draftMatch = parsed.Draft == fm.Draft;
            var dateMatch = CompareDateTimeOffset(parsed.Date, fm.Date);
            var tagsMatch = CompareStringLists(parsed.Tags, fm.Tags);
            var categoriesMatch = CompareStringLists(parsed.Categories, fm.Categories);
            var descriptionMatch = parsed.Description == fm.Description;

            _output.WriteLine($"Title match: {titleMatch} ('{parsed.Title}' vs '{fm.Title}')");
            _output.WriteLine($"Draft match: {draftMatch} ({parsed.Draft} vs {fm.Draft})");
            _output.WriteLine($"Date match: {dateMatch} ({parsed.Date} vs {fm.Date})");
            _output.WriteLine($"Tags match: {tagsMatch} ([{string.Join(",", parsed.Tags)}] vs [{string.Join(",", fm.Tags)}])");
            _output.WriteLine($"Categories match: {categoriesMatch} ([{string.Join(",", parsed.Categories)}] vs [{string.Join(",", fm.Categories)}])");
            _output.WriteLine($"Description match: {descriptionMatch} ('{parsed.Description}' vs '{fm.Description}')");

            var allMatch = titleMatch && draftMatch && dateMatch && tagsMatch && categoriesMatch && descriptionMatch;
            _output.WriteLine($"All match: {allMatch}");

            Assert.True(allMatch, "All fields should match");
        }
        else
        {
            Assert.Fail("Parsed is null");
        }
    }

    [Fact]
    public void Debug_TomlRoundTrip_FailingCase2()
    {
        // 另一个 PBT 失败的测试用例
        var fm = new FrontMatter
        {
            Title = "My First Post",
            Draft = true,
            Tags = [],
            Categories = [],
            Description = null,
            Layout = null,
            Slug = null,
            Author = null,
            Weight = 0,
            Date = null
        };

        var toml = _parser.Serialize(fm, FrontMatterFormat.Toml);
        _output.WriteLine("=== TOML Output ===");
        _output.WriteLine(toml);
        _output.WriteLine("=== END TOML ===");

        var success = _parser.TryParse(toml.AsSpan(), out var parsed, out var remaining);
        _output.WriteLine($"Parse success: {success}");

        if (parsed != null)
        {
            // 检查 PBT 测试中的所有条件
            var titleMatch = parsed.Title == fm.Title;
            var draftMatch = parsed.Draft == fm.Draft;
            var dateMatch = CompareDateTimeOffset(parsed.Date, fm.Date);
            var tagsMatch = CompareStringLists(parsed.Tags, fm.Tags);
            var categoriesMatch = CompareStringLists(parsed.Categories, fm.Categories);
            var descriptionMatch = parsed.Description == fm.Description;

            _output.WriteLine($"Title match: {titleMatch} ('{parsed.Title}' vs '{fm.Title}')");
            _output.WriteLine($"Draft match: {draftMatch} ({parsed.Draft} vs {fm.Draft})");
            _output.WriteLine($"Date match: {dateMatch} ({parsed.Date} vs {fm.Date})");
            _output.WriteLine($"Tags match: {tagsMatch} ([{string.Join(",", parsed.Tags)}] vs [{string.Join(",", fm.Tags)}])");
            _output.WriteLine($"Categories match: {categoriesMatch} ([{string.Join(",", parsed.Categories)}] vs [{string.Join(",", fm.Categories)}])");
            _output.WriteLine($"Description match: {descriptionMatch} ('{parsed.Description}' vs '{fm.Description}')");

            var allMatch = titleMatch && draftMatch && dateMatch && tagsMatch && categoriesMatch && descriptionMatch;
            _output.WriteLine($"All match: {allMatch}");

            Assert.True(allMatch, "All fields should match");
        }
        else
        {
            Assert.Fail("Parsed is null");
        }
    }

    [Fact]
    public void Debug_TomlRoundTrip_WithDate()
    {
        // 测试带日期的情况
        var fm = new FrontMatter
        {
            Title = "Technical Documentation",
            Draft = true,
            Tags = ["testing", "performance"],
            Categories = [],
            Description = null,
            Layout = null,
            Slug = null,
            Author = null,
            Weight = 0,
            Date = new DateTimeOffset(2022, 5, 27, 16, 59, 0, TimeSpan.Zero)
        };

        var toml = _parser.Serialize(fm, FrontMatterFormat.Toml);
        _output.WriteLine("=== TOML Output ===");
        _output.WriteLine(toml);
        _output.WriteLine("=== END TOML ===");

        // 直接使用 Tomlyn 解析来检查日期类型
        var tomlContent = toml.Replace("+++\n", "", StringComparison.Ordinal)
                              .Replace("\n+++\n", "", StringComparison.Ordinal)
                              .Replace("+++", "", StringComparison.Ordinal);
        var model = Tomlyn.Toml.ToModel(tomlContent);
        _output.WriteLine($"TOML Model keys: {string.Join(", ", model.Keys)}");
        if (model.TryGetValue("date", out var dateValue))
        {
            _output.WriteLine($"Date value type: {dateValue?.GetType().FullName}");
            _output.WriteLine($"Date value: {dateValue}");

            // 检查日期值的属性
            if (dateValue != null)
            {
                _output.WriteLine($"Date value properties:");
                foreach (var prop in dateValue.GetType().GetProperties())
                {
                    try
                    {
                        var val = prop.GetValue(dateValue);
                        _output.WriteLine($"  {prop.Name}: {val} ({val?.GetType().Name})");
                    }
                    catch (System.Reflection.TargetInvocationException ex)
                    {
                        _output.WriteLine($"  {prop.Name}: Error - {ex.InnerException?.Message}");
                    }
                }
            }
        }

        var success = _parser.TryParse(toml.AsSpan(), out var parsed, out var remaining);
        _output.WriteLine($"Parse success: {success}");

        if (parsed != null)
        {
            _output.WriteLine($"Original Date: {fm.Date}");
            _output.WriteLine($"Parsed Date: {parsed.Date}");
            _output.WriteLine($"Date match: {CompareDateTimeOffset(parsed.Date, fm.Date)}");

            Assert.True(CompareDateTimeOffset(parsed.Date, fm.Date), $"Date should match: {parsed.Date} vs {fm.Date}");
        }
        else
        {
            Assert.Fail("Parsed is null");
        }
    }

    private static bool CompareDateTimeOffset(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        return Math.Abs((a.Value.UtcDateTime - b.Value.UtcDateTime).TotalSeconds) < 1;
    }

    private static bool CompareStringLists(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        return a.SequenceEqual(b);
    }
}

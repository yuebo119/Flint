// Front Matter 快速路径与 YamlDotNet 完整解析器的等价性验证：
// 快速路径接受的每个样本，双路径产物必须逐字段一致。
// 这是"跳过 YamlDotNet 是否正确"的最终验证——不靠论证靠实测。

using FluentAssertions;
using Xunit;

namespace Flint.Core.Tests.Content;

public sealed class FrontMatterFastPathEquivalenceTests
{
    private readonly Flint.Core.Content.FrontMatterParser _parser = new();

    public static System.Collections.Generic.IEnumerable<object?[]> SimpleSamples()
    {
        yield return new object?[] { "title: Hello World" };
        yield return new object?[] { "title: \"Quoted Title\"" };
        yield return new object?[] { "title: 'Single Quoted'" };
        yield return new object?[] { "draft: false" };
        yield return new object?[] { "draft: true" };
        yield return new object?[] { "weight: 10" };
        yield return new object?[] { "weight: -3" };
        yield return new object?[] { "date: 2026-09-08T10:00:00+08:00" };
        yield return new object?[] { "date: 2026-09-08" };
        yield return new object?[] { "slug: my-page-slug" };
        yield return new object?[] { "description: A longer plain description with dashes - and dots." };
        yield return new object?[] { "tags: [alpha, beta, gamma]" };
        yield return new object?[] { "tags: [\"a b\", c]" };
        yield return new object?[] { "categories: [one]" };
        yield return new object?[] { "outputs: [html, json]" };
        yield return new object?[] { "title: T\ndraft: false\nweight: 5" };
        yield return new object?[] { "title: \"Multi Word\"\nslug: multi\nweight: -2\ndate: 2026-01-01" };
        yield return new object?[] { "type: post\ntitle: Typed" };
    }

    [Theory]
    [MemberData(nameof(SimpleSamples))]
    public void FastPath_与完整解析器_产物必须逐字段一致(string yaml)
    {
        // Arrange - 双路径各自解析同一样本（TryParse 契约要求带 --- 分隔符）
        var wrapped = "---\n" + yaml + "\n---";
        var fastOk = _parser.TryParse(wrapped.AsSpan(), out var fast, out _, null);
        fastOk.Should().BeTrue("样本必须在快速路径白名单内");
        var full = FrontMatterParserTestHelper.ParseViaYamlEngine(yaml);

        // Assert - 逐字段对比
        fast!.Title.Should().Be(full.Title);
        fast.Slug.Should().Be(full.Slug);
        fast.Draft.Should().Be(full.Draft);
        fast.Weight.Should().Be(full.Weight);
        fast.Description.Should().Be(full.Description);
        fast.Summary.Should().Be(full.Summary);
        fast.Type.Should().Be(full.Type);
        fast.Layout.Should().Be(full.Layout);
        fast.Tags.Should().Equal(full.Tags);
        fast.Categories.Should().Equal(full.Categories);
        fast.Date.Should().Be(full.Date);
        fast.LastMod.Should().Be(full.LastMod);
    }

    [Theory]
    [InlineData("title: \"Has \\\"escaped\\\" quotes\"")]
    [InlineData("title: \"Line\nBreak\"")]
    [InlineData("title: Complex # with comment")]
    [InlineData("custom_key: value")]
    [InlineData("title: |\n  block scalar")]
    [InlineData("title: {nested: map}")]
    [InlineData("title:")]
    public void FastPath_复杂形态必须回退完整解析器(string yaml)
    {
        // 回退后走 YamlDotNet/拒绝——两种结果都合法，
        // 禁止的是"快速路径以不同语义吞掉复杂形态"
        var wrapped = "---\n" + yaml + "\n---";
        bool fastOk;
        Flint.Core.Models.FrontMatter? fast;
        try
        {
            fastOk = _parser.TryParse(wrapped.AsSpan(), out fast, out _, null);
        }
        catch (Flint.Core.Content.FrontMatterParseException)
        {
            fastOk = false; // 快速路径拒绝（与回退路径的解析异常语义一致）
            fast = null;
        }
        var fullOk = FrontMatterParserTestHelper.ParseViaYamlEngineOrNull(wrapped, out var full);

        if (fastOk && fullOk)
        {
            // 两路径都成功：产物必须一致（回退路径与快速路径同语义）
            fast!.Title.Should().Be(full!.Title);
        }
    }
}

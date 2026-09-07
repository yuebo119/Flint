// Flint 静态站点生成器
// Front Matter 属性测试生成器

using Flint.Core.Models;
using FsCheck;
using FsCheck.Fluent;

namespace Flint.Core.Tests.Content;

/// <summary>
/// 有效 Front Matter 包装类型
/// </summary>
public sealed class ValidFrontMatter
{
    public FrontMatter Value { get; }

    public ValidFrontMatter(FrontMatter value)
    {
        Value = value;
    }

    public override string ToString() =>
        $"FrontMatter(Title={Value.Title}, Tags=[{string.Join(",", Value.Tags)}], Draft={Value.Draft})";
}

/// <summary>
/// 带空列表的有效 Front Matter 包装类型
/// </summary>
public sealed class ValidFrontMatterWithEmptyLists
{
    public FrontMatter Value { get; }

    public ValidFrontMatterWithEmptyLists(FrontMatter value)
    {
        Value = value;
    }

    public override string ToString() =>
        $"FrontMatter(Title={Value.Title}, EmptyLists)";
}

/// <summary>
/// 带特殊字符的有效 Front Matter 包装类型
/// </summary>
public sealed class ValidFrontMatterWithSpecialChars
{
    public FrontMatter Value { get; }

    public ValidFrontMatterWithSpecialChars(FrontMatter value)
    {
        Value = value;
    }

    public override string ToString() =>
        $"FrontMatter(Title={Value.Title}, SpecialChars)";
}

/// <summary>
/// 有效 Front Matter 生成器
/// 生成符合规范的 Front Matter 数据用于属性测试
/// </summary>
public static class ValidFrontMatterArbitrary
{
    #region 基础生成器

    /// <summary>
    /// 生成有效的标题
    /// 避免特殊字符以确保跨格式兼容性
    /// </summary>
    private static Gen<string> GenValidTitle()
    {
        return Gen.Elements(
            "My First Post",
            "Hello World",
            "Getting Started with Flint",
            "Technical Documentation",
            "Release Notes",
            "API Reference",
            "User Guide",
            "Quick Start",
            "Advanced Topics",
            "Best Practices",
            "Performance Tips",
            "Security Guidelines",
            "Deployment Guide",
            "Configuration Manual",
            "Troubleshooting");
    }

    /// <summary>
    /// 生成有效的描述
    /// </summary>
    private static Gen<string?> GenValidDescription()
    {
        return Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "A brief introduction to the topic",
                "Learn how to use this feature",
                "Step by step guide for beginners",
                "Advanced configuration options",
                "Common issues and solutions"));
    }

    /// <summary>
    /// 生成有效的标签列表
    /// </summary>
    private static Gen<IReadOnlyList<string>> GenValidTags()
    {
        var tagGen = Gen.Elements(
            "csharp", "dotnet", "performance", "tutorial",
            "guide", "api", "web", "backend", "frontend",
            "testing", "deployment", "security", "optimization");

        return Gen.OneOf(
            Gen.Constant<IReadOnlyList<string>>([]),
            Gen.ListOf(tagGen).Select(list => (IReadOnlyList<string>)list.Distinct().Take(5).ToList()));
    }

    /// <summary>
    /// 生成有效的分类列表
    /// </summary>
    private static Gen<IReadOnlyList<string>> GenValidCategories()
    {
        var categoryGen = Gen.Elements(
            "Technology", "Programming", "Tutorial",
            "News", "Documentation", "Reference");

        return Gen.OneOf(
            Gen.Constant<IReadOnlyList<string>>([]),
            Gen.ListOf(categoryGen).Select(list => (IReadOnlyList<string>)list.Distinct().Take(3).ToList()));
    }

    /// <summary>
    /// 生成有效的日期
    /// </summary>
    private static Gen<DateTimeOffset?> GenValidDate()
    {
        return Gen.OneOf(
            Gen.Constant<DateTimeOffset?>(null),
            Gen.Choose(2020, 2024).SelectMany(year =>
                Gen.Choose(1, 12).SelectMany(month =>
                    Gen.Choose(1, 28).SelectMany(day =>
                        Gen.Choose(0, 23).SelectMany(hour =>
                            Gen.Choose(0, 59).Select(minute =>
                                (DateTimeOffset?)new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero)))))));
    }

    /// <summary>
    /// 生成有效的布局名称
    /// </summary>
    private static Gen<string?> GenValidLayout()
    {
        return Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("single", "list", "home", "page", "post"));
    }

    /// <summary>
    /// 生成有效的 Slug
    /// </summary>
    private static Gen<string?> GenValidSlug()
    {
        return Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "my-first-post",
                "hello-world",
                "getting-started",
                "quick-start",
                "user-guide"));
    }

    /// <summary>
    /// 生成有效的作者
    /// </summary>
    private static Gen<string?> GenValidAuthor()
    {
        return Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("John Doe", "Jane Smith", "Admin", "Guest Author"));
    }

    /// <summary>
    /// 生成有效的权重
    /// </summary>
    private static Gen<int> GenValidWeight()
    {
        return Gen.OneOf(
            Gen.Constant(0),
            Gen.Choose(1, 100));
    }

    #endregion

    #region Arbitrary 实现

    /// <summary>
    /// 生成有效的 Front Matter
    /// </summary>
    public static Arbitrary<ValidFrontMatter> ValidFrontMatter()
    {
        var gen = from title in GenValidTitle()
                  from date in GenValidDate()
                  from draft in ArbMap.Default.GeneratorFor<bool>()
                  from tags in GenValidTags()
                  from categories in GenValidCategories()
                  from description in GenValidDescription()
                  from layout in GenValidLayout()
                  from slug in GenValidSlug()
                  from author in GenValidAuthor()
                  from weight in GenValidWeight()
                  select new ValidFrontMatter(new FrontMatter
                  {
                      Title = title,
                      Date = date,
                      Draft = draft,
                      Tags = tags,
                      Categories = categories,
                      Description = description,
                      Layout = layout,
                      Slug = slug,
                      Author = author,
                      Weight = weight
                  });

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成带空列表的 Front Matter
    /// </summary>
    public static Arbitrary<ValidFrontMatterWithEmptyLists> ValidFrontMatterWithEmptyLists()
    {
        var gen = from title in GenValidTitle()
                  from draft in ArbMap.Default.GeneratorFor<bool>()
                  select new ValidFrontMatterWithEmptyLists(new FrontMatter
                  {
                      Title = title,
                      Draft = draft,
                      Tags = [],
                      Categories = [],
                      Aliases = [],
                      Authors = [],
                      Keywords = [],
                      Outputs = []
                  });

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成带特殊字符的 Front Matter
    /// 注意：避免使用会破坏 YAML/TOML/JSON 语法的字符
    /// </summary>
    public static Arbitrary<ValidFrontMatterWithSpecialChars> ValidFrontMatterWithSpecialChars()
    {
        // 使用安全的特殊字符，避免破坏序列化格式
        var specialTitles = Gen.Elements(
            "Hello World",
            "Test Post 123",
            "My Article",
            "Guide Part 1",
            "Version 2.0");

        var specialDescriptions = Gen.Elements<string?>(
            "A simple description",
            "Learn more about this topic",
            "Step by step instructions",
            null);

        var gen = from title in specialTitles
                  from description in specialDescriptions
                  from draft in ArbMap.Default.GeneratorFor<bool>()
                  select new ValidFrontMatterWithSpecialChars(new FrontMatter
                  {
                      Title = title,
                      Description = description,
                      Draft = draft,
                      Tags = ["test", "special"]
                  });

        return gen.ToArbitrary();
    }

    #endregion
}

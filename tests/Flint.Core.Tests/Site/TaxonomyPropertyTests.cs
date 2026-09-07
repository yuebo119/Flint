// Flint 静态站点生成器
// 分类系统属性测试

using Flint.Core.Abstractions;
using Flint.Core.Site;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// 分类系统属性测试
/// **Property 9: 分类系统完整性**
/// **验证: 需求 5.2**
/// </summary>
public class TaxonomyPropertyTests
{
    /// <summary>
    /// **Property 9: 生成的分类页面应该包含所有相关内容**
    /// 对于任意带有标签和分类的内容集合，生成的分类页面应该包含所有相关内容
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(TaxonomyArbitrary)])]
    public Property TaxonomyPages_ShouldContainAllRelatedContent(ValidPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var service = new TaxonomyService("https://example.com");
        var pageList = pages.Value;

        // Act
        var taxonomies = service.BuildTaxonomies(pageList);

        // Assert - 每个页面的标签都应该在分类中找到
        foreach (var page in pageList)
        {
            foreach (var tag in page.Tags)
            {
                var tagTerms = taxonomies["tags"];
                var term = tagTerms.FirstOrDefault(t =>
                    string.Equals(t.Name, tag.Trim(), StringComparison.OrdinalIgnoreCase));

                if (term == null)
                {
                    return false.ToProperty()
                        .Label($"标签 '{tag}' 未在分类中找到");
                }

                if (!term.Pages.Contains(page))
                {
                    return false.ToProperty()
                        .Label($"页面 '{page.Title}' 未在标签 '{tag}' 的页面列表中");
                }
            }
        }

        return true.ToProperty()
            .Label($"所有 {pageList.Count} 个页面的标签都正确关联");
    }

    /// <summary>
    /// **Property 9: 每个内容应该出现在其声明的所有分类中**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(TaxonomyArbitrary)])]
    public Property Content_ShouldAppearInAllDeclaredTaxonomies(ValidPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var service = new TaxonomyService("https://example.com");
        var pageList = pages.Value;

        // Act
        var taxonomies = service.BuildTaxonomies(pageList);

        // Assert - 检查分类
        foreach (var page in pageList)
        {
            foreach (var category in page.Categories)
            {
                var categoryTerms = taxonomies["categories"];
                var term = categoryTerms.FirstOrDefault(t =>
                    string.Equals(t.Name, category.Trim(), StringComparison.OrdinalIgnoreCase));

                if (term == null)
                {
                    return false.ToProperty()
                        .Label($"分类 '{category}' 未在分类系统中找到");
                }

                if (!term.Pages.Contains(page))
                {
                    return false.ToProperty()
                        .Label($"页面 '{page.Title}' 未在分类 '{category}' 的页面列表中");
                }
            }
        }

        return true.ToProperty()
            .Label($"所有 {pageList.Count} 个页面的分类都正确关联");
    }

    /// <summary>
    /// **Property 9: 术语的页面计数应该正确**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(TaxonomyArbitrary)])]
    public Property TermCount_ShouldMatchActualPageCount(ValidPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var service = new TaxonomyService("https://example.com");
        var pageList = pages.Value;

        // Act
        var taxonomies = service.BuildTaxonomies(pageList);

        // Assert
        foreach (var (_, terms) in taxonomies.Taxonomies)
        {
            foreach (var term in terms)
            {
                if (term.Count != term.Pages.Count)
                {
                    return false.ToProperty()
                        .Label($"术语 '{term.Name}' 的 Count ({term.Count}) 与 Pages.Count ({term.Pages.Count}) 不匹配");
                }
            }
        }

        return true.ToProperty()
            .Label("所有术语的计数都正确");
    }

    /// <summary>
    /// **Property 9: Slug 生成应该是确定性的**
    /// 相同的术语名称应该生成相同的 slug
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(TaxonomyArbitrary)])]
    public Property SlugGeneration_ShouldBeDeterministic(ValidTermName termName)
    {
        ArgumentNullException.ThrowIfNull(termName);

        // Act
        var slug1 = TaxonomyService.GenerateSlug(termName.Value);
        var slug2 = TaxonomyService.GenerateSlug(termName.Value);

        // Assert
        return (slug1 == slug2)
            .ToProperty()
            .Label($"术语 '{termName.Value}' 的 slug 应该是确定性的: '{slug1}'");
    }

    /// <summary>
    /// **Property 9: Slug 应该是 URL 友好的**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(TaxonomyArbitrary)])]
    public Property Slug_ShouldBeUrlFriendly(ValidTermName termName)
    {
        ArgumentNullException.ThrowIfNull(termName);

        // Act
        var slug = TaxonomyService.GenerateSlug(termName.Value);

        // Assert - slug 应该只包含小写字母、数字、连字符和中文字符
        var isValid = string.IsNullOrEmpty(slug) ||
                      slug.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-' || (c >= '\u4e00' && c <= '\u9fff'));
        var noLeadingTrailingHyphens = !slug.StartsWith('-') && !slug.EndsWith('-');
        var noConsecutiveHyphens = !slug.Contains("--");

        return (isValid && noLeadingTrailingHyphens && noConsecutiveHyphens)
            .ToProperty()
            .Label($"Slug '{slug}' 应该是 URL 友好的");
    }

    /// <summary>
    /// **Property 9: 永久链接应该包含分类和术语信息**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(TaxonomyArbitrary)])]
    public Property Permalink_ShouldContainTaxonomyAndTermInfo(ValidPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var baseUrl = "https://example.com";
        var service = new TaxonomyService(baseUrl);
        var pageList = pages.Value;

        // Act
        var taxonomies = service.BuildTaxonomies(pageList);

        // Assert
        foreach (var (taxonomyName, terms) in taxonomies.Taxonomies)
        {
            var taxonomySlug = TaxonomyService.GenerateSlug(taxonomyName);

            foreach (var term in terms)
            {
                if (string.IsNullOrEmpty(term.Permalink))
                {
                    return false.ToProperty()
                        .Label($"术语 '{term.Name}' 的永久链接为空");
                }

                if (!term.Permalink.Contains(taxonomySlug))
                {
                    return false.ToProperty()
                        .Label($"永久链接 '{term.Permalink}' 应该包含分类 slug '{taxonomySlug}'");
                }

                if (!term.Permalink.Contains(term.Slug))
                {
                    return false.ToProperty()
                        .Label($"永久链接 '{term.Permalink}' 应该包含术语 slug '{term.Slug}'");
                }
            }
        }

        return true.ToProperty()
            .Label("所有永久链接都正确包含分类和术语信息");
    }

    /// <summary>
    /// **Property 9: 分类页面生成应该覆盖所有术语**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(TaxonomyArbitrary)])]
    public Property TaxonomyPageGeneration_ShouldCoverAllTerms(ValidPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var service = new TaxonomyService("https://example.com");
        var paginationService = new PaginationService();
        var generator = new TaxonomyPageGenerator(service, paginationService);
        var pageList = pages.Value;

        // Act
        var taxonomies = service.BuildTaxonomies(pageList);
        var generatedPages = generator.GeneratePages(taxonomies, pageSize: 10);

        // Assert - 每个术语都应该有对应的页面
        foreach (var (taxonomyName, terms) in taxonomies.Taxonomies)
        {
            foreach (var term in terms)
            {
                var hasTermPage = generatedPages.Any(p =>
                    p.TaxonomyName == taxonomyName &&
                    p.TermName == term.Name &&
                    p.PageType == TaxonomyPageType.TermPage);

                if (!hasTermPage)
                {
                    return false.ToProperty()
                        .Label($"术语 '{term.Name}' 没有生成对应的页面");
                }
            }
        }

        return true.ToProperty()
            .Label($"所有术语都生成了对应的页面");
    }
}

#region 测试数据类型

/// <summary>
/// 有效页面列表
/// </summary>
public sealed class ValidPageList
{
    public IReadOnlyList<PageContext> Value { get; }

    public ValidPageList(IReadOnlyList<PageContext> value)
    {
        Value = value;
    }

    public override string ToString() => $"PageList(Count={Value.Count})";
}

/// <summary>
/// 有效术语名称
/// </summary>
public sealed class ValidTermName
{
    public string Value { get; }

    public ValidTermName(string value)
    {
        Value = value;
    }

    public override string ToString() => $"TermName({Value})";
}

#endregion

#region 生成器

/// <summary>
/// 分类测试数据生成器
/// </summary>
public static class TaxonomyArbitrary
{
    /// <summary>
    /// 生成有效的标签
    /// </summary>
    private static Gen<string> GenValidTag()
    {
        return Gen.Elements(
            "CSharp", "DotNet", "Programming", "Web", "API",
            "Testing", "Performance", "Security", "Database", "Cloud",
            "Docker", "Kubernetes", "Azure", "AWS", "Linux",
            "前端", "后端", "全栈", "架构", "设计模式"
        );
    }

    /// <summary>
    /// 生成有效的分类
    /// </summary>
    private static Gen<string> GenValidCategory()
    {
        return Gen.Elements(
            "Technology", "Tutorial", "News", "Review", "Opinion",
            "技术", "教程", "新闻", "评测", "观点"
        );
    }

    /// <summary>
    /// 生成有效的页面上下文
    /// </summary>
    private static Gen<PageContext> GenValidPageContext()
    {
        return from title in Gen.Elements("Post 1", "Post 2", "Post 3", "Article A", "Article B", "Guide X", "Guide Y")
               from tagCount in Gen.Choose(0, 5)
               from tags in Gen.ListOf<string>(GenValidTag(), tagCount)
               from categoryCount in Gen.Choose(0, 3)
               from categories in Gen.ListOf<string>(GenValidCategory(), categoryCount)
               from daysAgo in Gen.Choose(0, 365)
               select new PageContext
               {
                   Title = title + "_" + Guid.NewGuid().ToString("N")[..8],
                   Content = "<p>Content</p>",
                   Permalink = $"https://example.com/posts/{title.ToLowerInvariant().Replace(" ", "-")}/",
                   RelPermalink = $"/posts/{title.ToLowerInvariant().Replace(" ", "-")}/",
                   Date = DateTimeOffset.Now.AddDays(-daysAgo),
                   Tags = tags.Distinct().ToList(),
                   Categories = categories.Distinct().ToList(),
                   WordCount = 500,
                   ReadingTime = TimeSpan.FromMinutes(3)
               };
    }

    /// <summary>
    /// 生成有效的页面列表
    /// </summary>
    public static Arbitrary<ValidPageList> ValidPageList()
    {
        var gen = Gen.Choose(1, 20)
            .SelectMany(count => Gen.ListOf<PageContext>(GenValidPageContext(), count))
            .Select(list => new ValidPageList(list.ToList()));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的术语名称
    /// </summary>
    public static Arbitrary<ValidTermName> ValidTermName()
    {
        var gen = Gen.Elements(
            "CSharp", "DotNet", "Programming", "Web Development",
            "Machine Learning", "Data Science", "Cloud Computing",
            "前端开发", "后端开发", "全栈工程师",
            "Test & Debug", "CI/CD", "DevOps",
            "C# 13", ".NET 10", "ASP.NET Core"
        ).Select(name => new ValidTermName(name));

        return gen.ToArbitrary();
    }
}

#endregion

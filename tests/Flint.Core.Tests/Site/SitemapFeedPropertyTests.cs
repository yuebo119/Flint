// Flint 静态站点生成器
// Sitemap 和 Feed 属性测试

using System.Xml.Linq;
using Flint.Core.Abstractions;
using Flint.Core.Site;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// Sitemap 和 Feed 属性测试
/// **Property 15: Sitemap 和 Feed 完整性**
/// **验证: 需求 5.4, 5.5**
/// </summary>
public class SitemapFeedPropertyTests
{
    /// <summary>
    /// **Property 15: Sitemap 应该包含所有公开页面的 URL**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(SitemapFeedArbitrary)])]
    public Property Sitemap_ShouldContainAllPublicPages(ValidSitemapPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var generator = new SitemapGenerator("https://example.com");
        var publicPages = pages.Value.Where(p => !p.Draft).ToList();

        // Act
        var sitemap = generator.Generate(pages.Value);
        var doc = XDocument.Parse(sitemap);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = doc.Descendants(ns + "loc").Select(e => e.Value).ToList();

        // Assert - 所有公开页面都应该在 sitemap 中
        foreach (var page in publicPages)
        {
            var expectedUrl = page.Permalink.StartsWith("http")
                ? page.Permalink
                : "https://example.com" + page.Permalink;

            if (!urls.Contains(expectedUrl))
            {
                return false.ToProperty()
                    .Label($"页面 '{page.Title}' 的 URL '{expectedUrl}' 未在 sitemap 中找到");
            }
        }

        return true.ToProperty()
            .Label($"所有 {publicPages.Count} 个公开页面都在 sitemap 中");
    }

    /// <summary>
    /// **Property 15: Sitemap 不应该包含草稿页面**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(SitemapFeedArbitrary)])]
    public Property Sitemap_ShouldNotContainDraftPages(ValidSitemapPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var generator = new SitemapGenerator("https://example.com");
        var draftPages = pages.Value.Where(p => p.Draft).ToList();

        if (draftPages.Count == 0)
        {
            return true.ToProperty().Label("没有草稿页面");
        }

        // Act
        var sitemap = generator.Generate(pages.Value);
        var doc = XDocument.Parse(sitemap);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = doc.Descendants(ns + "loc").Select(e => e.Value).ToList();

        // Assert - 草稿页面不应该在 sitemap 中
        foreach (var page in draftPages)
        {
            var draftUrl = page.Permalink.StartsWith("http")
                ? page.Permalink
                : "https://example.com" + page.Permalink;

            if (urls.Contains(draftUrl))
            {
                return false.ToProperty()
                    .Label($"草稿页面 '{page.Title}' 不应该在 sitemap 中");
            }
        }

        return true.ToProperty()
            .Label($"所有 {draftPages.Count} 个草稿页面都被排除");
    }

    /// <summary>
    /// **Property 15: Sitemap 应该是有效的 XML**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(SitemapFeedArbitrary)])]
    public Property Sitemap_ShouldBeValidXml(ValidSitemapPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var generator = new SitemapGenerator("https://example.com");

        // Act
        var sitemap = generator.Generate(pages.Value);

        // Assert
        try
        {
            var doc = XDocument.Parse(sitemap);
            var hasUrlset = doc.Root?.Name.LocalName == "urlset";
            return hasUrlset.ToProperty().Label("Sitemap 是有效的 XML 且包含 urlset 根元素");
        }
        catch (Exception ex)
        {
            return false.ToProperty().Label($"Sitemap XML 解析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// **Property 15: RSS Feed 应该包含最新的内容条目**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(SitemapFeedArbitrary)])]
    public Property RssFeed_ShouldContainLatestEntries(ValidSitemapPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var options = new FeedOptions
        {
            Title = "Test Blog",
            BaseUrl = "https://example.com",
            MaxItems = 10
        };
        var generator = new FeedGenerator(options);
        var publicPages = pages.Value
            .Where(p => !p.Draft)
            .OrderByDescending(p => p.Date)
            .Take(10)
            .ToList();

        // Act
        var rss = generator.GenerateRss(pages.Value);
        var doc = XDocument.Parse(rss);
        var items = doc.Descendants("item").ToList();

        // Assert - Feed 应该包含最新的条目
        var expectedCount = Math.Min(publicPages.Count, 10);
        return (items.Count == expectedCount)
            .ToProperty()
            .Label($"RSS Feed 包含 {items.Count} 个条目，期望 {expectedCount} 个");
    }

    /// <summary>
    /// **Property 15: RSS Feed 应该是有效的 XML**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(SitemapFeedArbitrary)])]
    public Property RssFeed_ShouldBeValidXml(ValidSitemapPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var options = new FeedOptions
        {
            Title = "Test Blog",
            BaseUrl = "https://example.com"
        };
        var generator = new FeedGenerator(options);

        // Act
        var rss = generator.GenerateRss(pages.Value);

        // Assert
        try
        {
            var doc = XDocument.Parse(rss);
            var hasRss = doc.Root?.Name.LocalName == "rss";
            var hasChannel = doc.Descendants("channel").Any();
            return (hasRss && hasChannel).ToProperty()
                .Label("RSS Feed 是有效的 XML 且包含 rss/channel 结构");
        }
        catch (Exception ex)
        {
            return false.ToProperty().Label($"RSS XML 解析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// **Property 15: Atom Feed 应该是有效的 XML**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(SitemapFeedArbitrary)])]
    public Property AtomFeed_ShouldBeValidXml(ValidSitemapPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var options = new FeedOptions
        {
            Title = "Test Blog",
            BaseUrl = "https://example.com"
        };
        var generator = new FeedGenerator(options);

        // Act
        var atom = generator.GenerateAtom(pages.Value);

        // Assert
        try
        {
            var doc = XDocument.Parse(atom);
            var hasFeed = doc.Root?.Name.LocalName == "feed";
            return hasFeed.ToProperty()
                .Label("Atom Feed 是有效的 XML 且包含 feed 根元素");
        }
        catch (Exception ex)
        {
            return false.ToProperty().Label($"Atom XML 解析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// **Property 15: Feed 条目应该按日期降序排列**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(SitemapFeedArbitrary)])]
    public Property Feed_EntriesShouldBeOrderedByDateDescending(ValidSitemapPageList pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        // Arrange
        var options = new FeedOptions
        {
            Title = "Test Blog",
            BaseUrl = "https://example.com",
            MaxItems = 20
        };
        var generator = new FeedGenerator(options);

        // Act
        var rss = generator.GenerateRss(pages.Value);
        var doc = XDocument.Parse(rss);
        var pubDates = doc.Descendants("pubDate")
            .Select(e => DateTime.Parse(e.Value))
            .ToList();

        // Assert - 日期应该是降序的
        for (var i = 1; i < pubDates.Count; i++)
        {
            if (pubDates[i] > pubDates[i - 1])
            {
                return false.ToProperty()
                    .Label($"Feed 条目未按日期降序排列: {pubDates[i - 1]} < {pubDates[i]}");
            }
        }

        return true.ToProperty()
            .Label($"所有 {pubDates.Count} 个条目按日期降序排列");
    }
}

#region 测试数据类型

/// <summary>
/// 有效的 Sitemap 页面列表
/// </summary>
public sealed class ValidSitemapPageList
{
    public IReadOnlyList<PageContext> Value { get; }

    public ValidSitemapPageList(IReadOnlyList<PageContext> value)
    {
        Value = value;
    }

    public override string ToString() => $"PageList(Count={Value.Count}, Drafts={Value.Count(p => p.Draft)})";
}

#endregion

#region 生成器

/// <summary>
/// Sitemap/Feed 测试数据生成器
/// </summary>
public static class SitemapFeedArbitrary
{
    /// <summary>
    /// 生成有效的页面上下文
    /// </summary>
    private static Gen<PageContext> GenValidPageContext()
    {
        return from title in Gen.Elements("Post 1", "Post 2", "Post 3", "Article A", "Article B", "Guide X")
               from isDraft in Gen.OneOf(Gen.Constant(false), Gen.Constant(false), Gen.Constant(false), Gen.Constant(false), Gen.Constant(true))
               from daysAgo in Gen.Choose(0, 365)
               from type in Gen.Elements("post", "page", "article")
               let id = Guid.NewGuid().ToString("N")[..8]
               select new PageContext
               {
                   Title = $"{title}_{id}",
                   Content = $"<p>Content for {title}</p>",
                   Permalink = $"/posts/{title.ToLowerInvariant().Replace(" ", "-")}-{id}/",
                   RelPermalink = $"/posts/{title.ToLowerInvariant().Replace(" ", "-")}-{id}/",
                   Date = DateTimeOffset.Now.AddDays(-daysAgo),
                   Tags = ["test", "sample"],
                   Categories = ["General"],
                   WordCount = 500,
                   ReadingTime = TimeSpan.FromMinutes(3),
                   Draft = isDraft,
                   Type = type,
                   Summary = $"Summary for {title}",
                   Description = $"Description for {title}"
               };
    }

    /// <summary>
    /// 生成有效的页面列表
    /// </summary>
    public static Arbitrary<ValidSitemapPageList> ValidSitemapPageList()
    {
        var gen = Gen.Choose(1, 30)
            .SelectMany(count => Gen.ListOf<PageContext>(GenValidPageContext(), count))
            .Select(list => new ValidSitemapPageList(list.ToList()));

        return gen.ToArbitrary();
    }
}

#endregion

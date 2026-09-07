// Flint 静态站点生成器
// Feed 生成器单元测试

using System.Xml.Linq;
using Flint.Core.Abstractions;
using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// FeedGenerator 单元测试
/// </summary>
public class FeedGeneratorTests
{
    private readonly FeedOptions _defaultOptions;

    public FeedGeneratorTests()
    {
        _defaultOptions = new FeedOptions
        {
            Title = "测试站点",
            BaseUrl = "https://example.com",
            Description = "测试描述",
            Language = "zh-CN",
            Author = "测试作者",
            Copyright = "© 2024 测试",
            MaxItems = 10
        };
    }

    #region RSS 生成测试

    [Fact]
    public void GenerateRss_应该生成有效的RSS2_0()
    {
        // Arrange
        var generator = new FeedGenerator(_defaultOptions);
        var pages = CreateTestPages();

        // Act
        var rss = generator.GenerateRss(pages);

        // Assert
        Assert.NotEmpty(rss);
        var doc = XDocument.Parse(rss);
        Assert.Equal("rss", doc.Root?.Name.LocalName);
        Assert.Equal("2.0", doc.Root?.Attribute("version")?.Value);
    }

    [Fact]
    public void GenerateRss_应该包含频道信息()
    {
        // Arrange
        var generator = new FeedGenerator(_defaultOptions);
        var pages = CreateTestPages();

        // Act
        var rss = generator.GenerateRss(pages);

        // Assert
        var doc = XDocument.Parse(rss);
        var channel = doc.Root?.Element("channel");
        Assert.NotNull(channel);
        Assert.Equal("测试站点", channel.Element("title")?.Value);
        Assert.Equal("https://example.com", channel.Element("link")?.Value);
        Assert.Equal("测试描述", channel.Element("description")?.Value);
        Assert.Equal("zh-CN", channel.Element("language")?.Value);
    }

    [Fact]
    public void GenerateRss_应该包含文章条目()
    {
        // Arrange
        var generator = new FeedGenerator(_defaultOptions);
        var pages = CreateTestPages();

        // Act
        var rss = generator.GenerateRss(pages);

        // Assert
        var doc = XDocument.Parse(rss);
        var items = doc.Descendants("item").ToList();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Element("title")?.Value == "测试文章1");
    }

    [Fact]
    public void GenerateRss_应该按日期降序排列()
    {
        // Arrange
        var generator = new FeedGenerator(_defaultOptions);
        var pages = new List<PageContext>
        {
            CreatePage("旧文章", DateTimeOffset.Now.AddDays(-10)),
            CreatePage("新文章", DateTimeOffset.Now.AddDays(-1)),
            CreatePage("中间文章", DateTimeOffset.Now.AddDays(-5))
        };

        // Act
        var rss = generator.GenerateRss(pages);

        // Assert
        var doc = XDocument.Parse(rss);
        var titles = doc.Descendants("item").Select(i => i.Element("title")?.Value).ToList();
        Assert.Equal("新文章", titles[0]);
        Assert.Equal("中间文章", titles[1]);
        Assert.Equal("旧文章", titles[2]);
    }

    [Fact]
    public void GenerateRss_应该限制最大条目数()
    {
        // Arrange
        var options = _defaultOptions with { MaxItems = 2 };
        var generator = new FeedGenerator(options);
        var pages = Enumerable.Range(1, 5)
            .Select(i => CreatePage($"文章{i}", DateTimeOffset.Now.AddDays(-i)))
            .ToList();

        // Act
        var rss = generator.GenerateRss(pages);

        // Assert
        var doc = XDocument.Parse(rss);
        var items = doc.Descendants("item").ToList();
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void GenerateRss_应该排除草稿()
    {
        // Arrange
        var options = _defaultOptions with { IncludeDrafts = false };
        var generator = new FeedGenerator(options);
        var pages = new List<PageContext>
        {
            CreatePage("已发布", DateTimeOffset.Now, draft: false),
            CreatePage("草稿", DateTimeOffset.Now, draft: true)
        };

        // Act
        var rss = generator.GenerateRss(pages);

        // Assert
        var doc = XDocument.Parse(rss);
        var items = doc.Descendants("item").ToList();
        Assert.Single(items);
        Assert.Equal("已发布", items[0].Element("title")?.Value);
    }

    #endregion

    #region Atom 生成测试

    [Fact]
    public void GenerateAtom_应该生成有效的Atom1_0()
    {
        // Arrange
        var generator = new FeedGenerator(_defaultOptions);
        var pages = CreateTestPages();

        // Act
        var atom = generator.GenerateAtom(pages);

        // Assert
        Assert.NotEmpty(atom);
        var doc = XDocument.Parse(atom);
        Assert.Equal("feed", doc.Root?.Name.LocalName);
        Assert.Equal("http://www.w3.org/2005/Atom", doc.Root?.Name.NamespaceName);
    }

    [Fact]
    public void GenerateAtom_应该包含Feed信息()
    {
        // Arrange
        var generator = new FeedGenerator(_defaultOptions);
        var pages = CreateTestPages();

        // Act
        var atom = generator.GenerateAtom(pages);

        // Assert
        var doc = XDocument.Parse(atom);
        XNamespace ns = "http://www.w3.org/2005/Atom";
        Assert.Equal("测试站点", doc.Root?.Element(ns + "title")?.Value);
        Assert.Equal("测试描述", doc.Root?.Element(ns + "subtitle")?.Value);
    }

    [Fact]
    public void GenerateAtom_应该包含Entry条目()
    {
        // Arrange
        var generator = new FeedGenerator(_defaultOptions);
        var pages = CreateTestPages();

        // Act
        var atom = generator.GenerateAtom(pages);

        // Assert
        var doc = XDocument.Parse(atom);
        XNamespace ns = "http://www.w3.org/2005/Atom";
        var entries = doc.Descendants(ns + "entry").ToList();
        Assert.Equal(2, entries.Count);
    }

    #endregion

    #region 辅助方法

    private static List<PageContext> CreateTestPages()
    {
        return
        [
            CreatePage("测试文章1", DateTimeOffset.Now.AddDays(-1)),
            CreatePage("测试文章2", DateTimeOffset.Now.AddDays(-2))
        ];
    }

    private static PageContext CreatePage(
        string title,
        DateTimeOffset date,
        bool draft = false,
        string? content = null,
        string? type = null)
    {
        return new PageContext
        {
            Title = title,
            Date = date,
            Draft = draft,
            Content = content ?? $"<p>{title} 内容</p>",
            Summary = $"{title} 摘要",
            Permalink = $"https://example.com/posts/{title.ToLowerInvariant().Replace(" ", "-")}/",
            RelPermalink = $"/posts/{title.ToLowerInvariant().Replace(" ", "-")}/",
            Type = type ?? "post",
            Categories = ["分类1"],
            Tags = ["标签1", "标签2"],
            WordCount = 100,
            ReadingTime = TimeSpan.FromMinutes(1)
        };
    }

    #endregion
}

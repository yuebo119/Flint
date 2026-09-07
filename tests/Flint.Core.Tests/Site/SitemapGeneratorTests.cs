// Flint 静态站点生成器
// Sitemap 生成器单元测试

using System.Xml.Linq;
using Flint.Core.Abstractions;
using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// SitemapGenerator 单元测试
/// </summary>
public class SitemapGeneratorTests
{
    private const string BaseUrl = "https://example.com";
    private static readonly XNamespace SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

    #region 基本生成测试

    [Fact]
    public void Generate_应该生成有效的XML()
    {
        // Arrange
        var generator = new SitemapGenerator(BaseUrl);
        var pages = CreateTestPages();

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        Assert.NotEmpty(sitemap);
        var doc = XDocument.Parse(sitemap);
        Assert.Equal("urlset", doc.Root?.Name.LocalName);
    }

    [Fact]
    public void Generate_应该使用正确的命名空间()
    {
        // Arrange
        var generator = new SitemapGenerator(BaseUrl);
        var pages = CreateTestPages();

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        Assert.Equal(SitemapNs.NamespaceName, doc.Root?.Name.NamespaceName);
    }

    [Fact]
    public void Generate_应该包含所有页面()
    {
        // Arrange
        var generator = new SitemapGenerator(BaseUrl);
        var pages = CreateTestPages();

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var urls = doc.Descendants(SitemapNs + "url").ToList();
        Assert.Equal(3, urls.Count);
    }

    #endregion

    #region URL 生成测试

    [Fact]
    public void Generate_应该生成完整的URL()
    {
        // Arrange
        var generator = new SitemapGenerator(BaseUrl);
        var pages = new List<PageContext>
        {
            CreatePage("/about/", DateTimeOffset.Now)
        };

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var loc = doc.Descendants(SitemapNs + "loc").First().Value;
        Assert.Equal("https://example.com/about/", loc);
    }

    #endregion

    #region lastmod 测试

    [Fact]
    public void Generate_应该包含lastmod()
    {
        // Arrange
        var generator = new SitemapGenerator(BaseUrl);
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var pages = new List<PageContext>
        {
            CreatePage("/test/", date)
        };

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var lastmod = doc.Descendants(SitemapNs + "lastmod").First().Value;
        Assert.Equal("2024-01-15", lastmod);
    }

    #endregion

    #region changefreq 测试

    [Fact]
    public void Generate_IncludeChangeFreq为true时应该包含changefreq()
    {
        // Arrange
        var options = new SitemapOptions { IncludeChangeFreq = true };
        var generator = new SitemapGenerator(BaseUrl, options);
        var pages = CreateTestPages();

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var changefreq = doc.Descendants(SitemapNs + "changefreq").ToList();
        Assert.NotEmpty(changefreq);
    }

    [Fact]
    public void Generate_IncludeChangeFreq为false时不应该包含changefreq()
    {
        // Arrange
        var options = new SitemapOptions { IncludeChangeFreq = false };
        var generator = new SitemapGenerator(BaseUrl, options);
        var pages = CreateTestPages();

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var changefreq = doc.Descendants(SitemapNs + "changefreq").ToList();
        Assert.Empty(changefreq);
    }

    #endregion

    #region priority 测试

    [Fact]
    public void Generate_IncludePriority为true时应该包含priority()
    {
        // Arrange
        var options = new SitemapOptions { IncludePriority = true };
        var generator = new SitemapGenerator(BaseUrl, options);
        var pages = CreateTestPages();

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var priority = doc.Descendants(SitemapNs + "priority").ToList();
        Assert.NotEmpty(priority);
    }

    [Fact]
    public void Generate_首页应该有最高优先级()
    {
        // Arrange
        var options = new SitemapOptions { IncludePriority = true };
        var generator = new SitemapGenerator(BaseUrl, options);
        var pages = new List<PageContext>
        {
            CreatePage("/", DateTimeOffset.Now)
        };

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var priority = doc.Descendants(SitemapNs + "priority").First().Value;
        Assert.Equal("1.0", priority);
    }

    #endregion

    #region 过滤测试

    [Fact]
    public void Generate_应该排除草稿()
    {
        // Arrange
        var options = new SitemapOptions { IncludeDrafts = false };
        var generator = new SitemapGenerator(BaseUrl, options);
        var pages = new List<PageContext>
        {
            CreatePage("/published/", DateTimeOffset.Now, draft: false),
            CreatePage("/draft/", DateTimeOffset.Now, draft: true)
        };

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var urls = doc.Descendants(SitemapNs + "url").ToList();
        Assert.Single(urls);
        Assert.Contains("published", urls[0].Element(SitemapNs + "loc")?.Value);
    }

    [Fact]
    public void Generate_IncludeDrafts为true时应该包含草稿()
    {
        // Arrange
        var options = new SitemapOptions { IncludeDrafts = true };
        var generator = new SitemapGenerator(BaseUrl, options);
        var pages = new List<PageContext>
        {
            CreatePage("/published/", DateTimeOffset.Now, draft: false),
            CreatePage("/draft/", DateTimeOffset.Now, draft: true)
        };

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var urls = doc.Descendants(SitemapNs + "url").ToList();
        Assert.Equal(2, urls.Count);
    }

    [Fact]
    public void Generate_应该排除指定类型()
    {
        // Arrange
        var options = new SitemapOptions
        {
            ExcludedTypes = new HashSet<string> { "hidden" }
        };
        var generator = new SitemapGenerator(BaseUrl, options);
        var pages = new List<PageContext>
        {
            CreatePage("/visible/", DateTimeOffset.Now, type: "page"),
            CreatePage("/hidden/", DateTimeOffset.Now, type: "hidden")
        };

        // Act
        var sitemap = generator.Generate(pages);

        // Assert
        var doc = XDocument.Parse(sitemap);
        var urls = doc.Descendants(SitemapNs + "url").ToList();
        Assert.Single(urls);
    }

    #endregion

    #region Sitemap 索引测试

    [Fact]
    public void GenerateIndex_应该生成有效的索引文件()
    {
        // Arrange
        var generator = new SitemapGenerator(BaseUrl);
        var entries = new List<SitemapIndexEntry>
        {
            new() { Url = "https://example.com/sitemap-1.xml" },
            new() { Url = "https://example.com/sitemap-2.xml" }
        };

        // Act
        var index = generator.GenerateIndex(entries);

        // Assert
        var doc = XDocument.Parse(index);
        Assert.Equal("sitemapindex", doc.Root?.Name.LocalName);
    }

    [Fact]
    public void GenerateIndex_应该包含所有子sitemap()
    {
        // Arrange
        var generator = new SitemapGenerator(BaseUrl);
        var entries = new List<SitemapIndexEntry>
        {
            new() { Url = "https://example.com/sitemap-1.xml" },
            new() { Url = "https://example.com/sitemap-2.xml" },
            new() { Url = "https://example.com/sitemap-3.xml" }
        };

        // Act
        var index = generator.GenerateIndex(entries);

        // Assert
        var doc = XDocument.Parse(index);
        var sitemaps = doc.Descendants(SitemapNs + "sitemap").ToList();
        Assert.Equal(3, sitemaps.Count);
    }

    #endregion

    #region 辅助方法

    private static List<PageContext> CreateTestPages()
    {
        return
        [
            CreatePage("/", DateTimeOffset.Now),
            CreatePage("/about/", DateTimeOffset.Now.AddDays(-1)),
            CreatePage("/contact/", DateTimeOffset.Now.AddDays(-2))
        ];
    }

    private static PageContext CreatePage(
        string permalink,
        DateTimeOffset date,
        bool draft = false,
        string? type = null)
    {
        return new PageContext
        {
            Title = $"Page {permalink}",
            Content = $"<p>Content for {permalink}</p>",
            Permalink = permalink,
            RelPermalink = permalink,
            Date = date,
            Draft = draft,
            Type = type ?? "page",
            Tags = [],
            Categories = [],
            WordCount = 100,
            ReadingTime = TimeSpan.FromMinutes(1)
        };
    }

    #endregion
}

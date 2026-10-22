// Flint 静态站点生成器
// XML 产物声明编码契约测试
//
// 回归防护：XmlWriter 以 StringBuilder 为目标时忽略 settings.Encoding，
// 声明恒写 utf-16 而调用方以 UTF-8 无 BOM 落盘，严格解析器（XmlReader、
// sitemap 校验器、feed 阅读器）直接拒绝产物。测试必须走**字节级**路径：
// XDocument.Parse(string) 看不到声明与实际字节的矛盾。

using System.Xml;
using Flint.Core.Abstractions;
using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

public sealed class XmlOutputEncodingTests : IDisposable
{
    private readonly string _tempDir;

    public XmlOutputEncodingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_xmlenc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    private static readonly DateTimeOffset PageDate = new(2024, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private static List<PageContext> Pages() =>
    [
        new PageContext
        {
            Title = "P",
            Content = "<p>正文</p>",
            Permalink = "https://example.com/p/",
            RelPermalink = "/p/",
            Date = PageDate,
            Tags = [],
            Categories = [],
            WordCount = 3,
            ReadingTime = TimeSpan.FromMinutes(1),
            Type = "post"
        }
    ];

    /// <summary>以产物落盘的真实方式写出（UTF-8 无 BOM），再用严格解析器读字节</summary>
    private string WriteAndStrictParse(string name, string xml)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, xml);
        using var reader = XmlReader.Create(path);
        while (reader.Read())
        {
        }

        return path;
    }

    [Fact]
    public void Sitemap_声明为utf8且可被严格解析器读取()
    {
        var sitemap = new SitemapGenerator("https://example.com").Generate(Pages());

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", sitemap, StringComparison.Ordinal);
        Assert.DoesNotContain("utf-16", sitemap, StringComparison.OrdinalIgnoreCase);
        WriteAndStrictParse("sitemap.xml", sitemap);
    }

    [Fact]
    public void Rss_声明为utf8且可被严格解析器读取()
    {
        var rss = new FeedGenerator(new FeedOptions
        {
            Title = "T",
            BaseUrl = "https://example.com"
        }).GenerateRss(Pages());

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", rss, StringComparison.Ordinal);
        Assert.DoesNotContain("utf-16", rss, StringComparison.OrdinalIgnoreCase);
        WriteAndStrictParse("rss.xml", rss);
    }

    [Fact]
    public void Atom_声明为utf8且可被严格解析器读取()
    {
        var atom = new FeedGenerator(new FeedOptions
        {
            Title = "T",
            BaseUrl = "https://example.com"
        }).GenerateAtom(Pages());

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", atom, StringComparison.Ordinal);
        Assert.DoesNotContain("utf-16", atom, StringComparison.OrdinalIgnoreCase);
        WriteAndStrictParse("atom.xml", atom);
    }

    [Fact]
    public void Sitemap_无日期页不产出0001边界时间()
    {
        var undated = new List<PageContext>
        {
            new()
            {
                Title = "U",
                Content = "",
                Permalink = "https://example.com/u/",
                RelPermalink = "/u/",
                Date = DateTimeOffset.MinValue,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero
            }
        };

        var sitemap = new SitemapGenerator("https://example.com").Generate(undated);

        Assert.DoesNotContain("0001-01-01", sitemap, StringComparison.Ordinal);
        Assert.DoesNotContain("<lastmod", sitemap, StringComparison.Ordinal);
    }

    [Fact]
    public void Rss_无日期页不产出0001边界时间()
    {
        var undated = new List<PageContext>
        {
            new()
            {
                Title = "U",
                Content = "",
                Permalink = "https://example.com/u/",
                RelPermalink = "/u/",
                Date = DateTimeOffset.MinValue,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero
            }
        };

        var rss = new FeedGenerator(new FeedOptions
        {
            Title = "T",
            BaseUrl = "https://example.com"
        }).GenerateRss(undated);

        Assert.DoesNotContain("0001", rss, StringComparison.Ordinal);
        Assert.DoesNotContain("<lastBuildDate", rss, StringComparison.Ordinal);
    }
}
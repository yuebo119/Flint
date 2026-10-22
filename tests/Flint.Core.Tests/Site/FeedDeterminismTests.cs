// Flint 静态站点生成器
// Feed 时间戳可复现性契约测试
//
// 回归防护：lastBuildDate / atom updated 曾直取 DateTimeOffset.Now，
// 同一输入两次构建产出不同字节（CDN 缓存、差分部署、--cache 写盘跳过全受牵连）。
// 断言用精确值而非"两次调用相等"：同一秒内的两次 Now 调用也相等，抓不住回归。

using System.Globalization;
using Flint.Core.Abstractions;
using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

public sealed class FeedDeterminismTests
{
    private static readonly DateTimeOffset Older = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset Newer = new(2025, 6, 7, 8, 9, 10, TimeSpan.Zero);

    private static List<PageContext> Pages() =>
    [
        new PageContext
        {
            Title = "Older",
            Content = "a",
            Permalink = "https://example.com/older/",
            RelPermalink = "/older/",
            Date = Older,
            Tags = [],
            Categories = [],
            WordCount = 1,
            ReadingTime = TimeSpan.FromMinutes(1)
        },
        new PageContext
        {
            Title = "Newer",
            Content = "b",
            Permalink = "https://example.com/newer/",
            RelPermalink = "/newer/",
            Date = Newer,
            LastMod = Newer,
            Tags = [],
            Categories = [],
            WordCount = 1,
            ReadingTime = TimeSpan.FromMinutes(1)
        }
    ];

    private static FeedGenerator Generator() => new(new FeedOptions
    {
        Title = "T",
        BaseUrl = "https://example.com"
    });

    [Fact]
    public void Rss_lastBuildDate取页面最大日期而非构建时刻()
    {
        var rss = Generator().GenerateRss(Pages());

        // RFC 822 形态（+0000，无冒号），与 FormatRssDate 同口径
        var expected = Newer.ToString(
            "ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture) + "+0000";
        Assert.Contains($"<lastBuildDate>{expected}</lastBuildDate>", rss, StringComparison.Ordinal);
    }

    [Fact]
    public void Atom_updated取页面最大日期而非构建时刻()
    {
        var atom = Generator().GenerateAtom(Pages());

        Assert.Contains(
            $"<updated>{Newer:O}</updated>",
            atom,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rss_两次生成字节一致()
    {
        var first = Generator().GenerateRss(Pages());
        var second = Generator().GenerateRss(Pages());

        Assert.Equal(first, second);
        Assert.DoesNotContain(DateTimeOffset.Now.Year.ToString(CultureInfo.InvariantCulture), first, StringComparison.Ordinal);
    }

    [Fact]
    public void Atom_两次生成字节一致()
    {
        var first = Generator().GenerateAtom(Pages());
        var second = Generator().GenerateAtom(Pages());

        Assert.Equal(first, second);
    }

    [Fact]
    public void 无日期页的feed不产出构建时刻()
    {
        var undated = new List<PageContext>
        {
            new()
            {
                Title = "U",
                Content = "u",
                Permalink = "https://example.com/u/",
                RelPermalink = "/u/",
                Date = DateTimeOffset.MinValue,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero
            }
        };

        var rss = Generator().GenerateRss(undated);
        Assert.DoesNotContain("<lastBuildDate", rss, StringComparison.Ordinal);

        var atom = Generator().GenerateAtom(undated);
        Assert.DoesNotContain("<updated>", atom, StringComparison.Ordinal);
    }
}
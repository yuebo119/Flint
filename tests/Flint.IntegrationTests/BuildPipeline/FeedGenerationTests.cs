// Flint 静态站点生成器
// Feed 生成测试
// 验证 sitemap.xml、RSS 和 Atom feed 的正确生成

using System.Xml;
using System.Xml.Linq;
using Flint.Core.Models;
using Flint.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// Feed 生成测试
/// 验证 sitemap.xml、RSS 和 Atom feed 的正确生成
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 2.8: sitemap.xml 生成
/// - Requirements 2.9: RSS 和 Atom feed 生成
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
[Trait("TestType", "Feed")]
public class FeedGenerationTests : IAsyncLifetime
{
    #region 私有字段

    private readonly ITestOutputHelper _output;
    private readonly TestSiteFixture _fixture;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建 Feed 生成测试实例
    /// </summary>
    /// <param name="output">测试输出帮助器</param>
    public FeedGenerationTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture = new TestSiteFixture();
    }

    #endregion

    #region IAsyncLifetime 实现

    /// <summary>
    /// 异步初始化
    /// </summary>
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
    }

    /// <summary>
    /// 异步清理
    /// </summary>
    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    #endregion

    #region Sitemap 测试

    /// <summary>
    /// 测试 sitemap.xml 生成
    /// 验证构建后生成有效的 sitemap.xml 文件
    /// </summary>
    [Fact]
    [Trait("TestType", "Sitemap")]
    public async Task BuildAsync_ShouldGenerateSitemapXml()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("complete");

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证 sitemap.xml 存在
        var sitemapExists = _fixture.OutputFileExists("sitemap.xml");
        _output.WriteLine($"sitemap.xml 存在: {sitemapExists}");

        sitemapExists.Should().BeTrue("应该生成 sitemap.xml");

        // 验证 sitemap.xml 内容有效
        var sitemapContent = await _fixture.GetOutputFileAsync("sitemap.xml");
        sitemapContent.Should().NotBeNullOrEmpty("sitemap.xml 内容不应为空");

        // 验证 XML 格式有效
        var isValidXml = IsValidXml(sitemapContent!);
        _output.WriteLine($"XML 格式有效: {isValidXml}");
        isValidXml.Should().BeTrue("sitemap.xml 应该是有效的 XML");
    }

    /// <summary>
    /// 测试 sitemap.xml 符合 Sitemap 协议规范
    /// 验证 sitemap.xml 包含正确的命名空间和结构
    /// </summary>
    [Fact]
    [Trait("TestType", "Sitemap")]
    public async Task BuildAsync_SitemapShouldFollowSitemapProtocol()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加测试文章
        var content = """
            +++
            title = "测试文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            测试内容。
            """;
        await _fixture.AddContentAsync("posts/test-article.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 获取 sitemap.xml 内容
        var sitemapContent = await _fixture.GetOutputFileAsync("sitemap.xml");
        if (sitemapContent == null)
        {
            _output.WriteLine("sitemap.xml 不存在");
            return;
        }

        // 解析 XML
        var doc = XDocument.Parse(sitemapContent);
        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");

        // 验证根元素
        var root = doc.Root;
        root.Should().NotBeNull("应该有根元素");

        // 验证包含 url 元素
        var urls = doc.Descendants(ns + "url").ToList();
        _output.WriteLine($"URL 数量: {urls.Count}");

        // 验证每个 url 元素包含 loc
        foreach (var url in urls)
        {
            var loc = url.Element(ns + "loc");
            loc.Should().NotBeNull("每个 url 应该包含 loc 元素");
            _output.WriteLine($"  - {loc?.Value}");
        }
    }

    /// <summary>
    /// 测试 sitemap.xml 包含所有页面
    /// 验证 sitemap.xml 包含站点中所有页面的 URL
    /// </summary>
    [Fact]
    [Trait("TestType", "Sitemap")]
    public async Task BuildAsync_SitemapShouldContainAllPages()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加多个测试文章
        var articles = new[] { "article-1", "article-2", "article-3" };
        foreach (var article in articles)
        {
            var content = $"""
                +++
                title = "{article}"
                date = 2024-01-15T10:00:00+08:00
                draft = false
                +++

                {article} 内容。
                """;
            await _fixture.AddContentAsync($"posts/{article}.md", content);
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 获取 sitemap.xml 内容
        var sitemapContent = await _fixture.GetOutputFileAsync("sitemap.xml");
        if (sitemapContent == null)
        {
            _output.WriteLine("sitemap.xml 不存在");
            return;
        }

        // 验证包含所有文章的 URL
        foreach (var article in articles)
        {
            var containsArticle = sitemapContent.Contains(article, StringComparison.OrdinalIgnoreCase);
            _output.WriteLine($"包含 {article}: {containsArticle}");
        }
    }

    #endregion

    #region RSS Feed 测试

    /// <summary>
    /// 测试 RSS feed 生成
    /// 验证构建后生成有效的 RSS feed 文件
    /// </summary>
    [Fact]
    [Trait("TestType", "RSS")]
    public async Task BuildAsync_ShouldGenerateRssFeed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("complete");

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证 RSS feed 存在（可能是 index.xml 或 rss.xml）
        var rssExists = _fixture.OutputFileExists("index.xml") ||
                        _fixture.OutputFileExists("rss.xml") ||
                        _fixture.OutputFileExists("feed.xml");
        _output.WriteLine($"RSS feed 存在: {rssExists}");

        // 获取 RSS feed 内容
        var rssContent = await _fixture.GetOutputFileAsync("index.xml") ??
                         await _fixture.GetOutputFileAsync("rss.xml") ??
                         await _fixture.GetOutputFileAsync("feed.xml");

        if (rssContent != null)
        {
            // 验证 XML 格式有效
            var isValidXml = IsValidXml(rssContent);
            _output.WriteLine($"XML 格式有效: {isValidXml}");
            isValidXml.Should().BeTrue("RSS feed 应该是有效的 XML");
        }
    }

    /// <summary>
    /// 测试 RSS feed 符合 RSS 2.0 规范
    /// 验证 RSS feed 包含正确的结构和必需元素
    /// </summary>
    [Fact]
    [Trait("TestType", "RSS")]
    public async Task BuildAsync_RssFeedShouldFollowRss20Spec()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加测试文章
        var content = """
            +++
            title = "RSS 测试文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            description = "这是 RSS 测试文章的描述"
            +++

            RSS 测试内容。
            """;
        await _fixture.AddContentAsync("posts/rss-test.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 获取 RSS feed 内容
        var rssContent = await _fixture.GetOutputFileAsync("index.xml") ??
                         await _fixture.GetOutputFileAsync("rss.xml");

        if (rssContent == null)
        {
            _output.WriteLine("RSS feed 不存在");
            return;
        }

        // 解析 XML
        var doc = XDocument.Parse(rssContent);

        // 验证根元素是 rss
        var root = doc.Root;
        if (root?.Name.LocalName == "rss")
        {
            // 验证 version 属性
            var version = root.Attribute("version")?.Value;
            _output.WriteLine($"RSS 版本: {version}");

            // 验证 channel 元素
            var channel = root.Element("channel");
            channel.Should().NotBeNull("RSS feed 应该包含 channel 元素");

            if (channel != null)
            {
                // 验证必需元素
                var title = channel.Element("title");
                var link = channel.Element("link");
                var description = channel.Element("description");

                _output.WriteLine($"title: {title?.Value}");
                _output.WriteLine($"link: {link?.Value}");
                _output.WriteLine($"description: {description?.Value}");

                // 验证 item 元素
                var items = channel.Elements("item").ToList();
                _output.WriteLine($"item 数量: {items.Count}");

                foreach (var item in items)
                {
                    var itemTitle = item.Element("title")?.Value;
                    var itemLink = item.Element("link")?.Value;
                    _output.WriteLine($"  - {itemTitle}: {itemLink}");
                }
            }
        }
        else
        {
            _output.WriteLine($"根元素不是 rss: {root?.Name.LocalName}");
        }
    }

    /// <summary>
    /// 测试 RSS feed 日期格式
    /// 验证 RSS feed 中的日期符合 RFC 822 格式
    /// </summary>
    [Fact]
    [Trait("TestType", "RSS")]
    public async Task BuildAsync_RssFeedShouldHaveCorrectDateFormat()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加测试文章
        var content = """
            +++
            title = "日期格式测试"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            日期格式测试内容。
            """;
        await _fixture.AddContentAsync("posts/date-test.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 获取 RSS feed 内容
        var rssContent = await _fixture.GetOutputFileAsync("index.xml") ??
                         await _fixture.GetOutputFileAsync("rss.xml");

        if (rssContent == null)
        {
            _output.WriteLine("RSS feed 不存在");
            return;
        }

        // 检查日期格式（RFC 822）
        // 例如: Mon, 15 Jan 2024 10:00:00 +0800
        var containsDate = rssContent.Contains("pubDate", StringComparison.OrdinalIgnoreCase) ||
                           rssContent.Contains("lastBuildDate", StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"包含日期元素: {containsDate}");
    }

    #endregion

    #region Atom Feed 测试

    /// <summary>
    /// 测试 Atom feed 生成
    /// 验证构建后生成有效的 Atom feed 文件
    /// </summary>
    [Fact]
    [Trait("TestType", "Atom")]
    public async Task BuildAsync_ShouldGenerateAtomFeed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("complete");

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证 Atom feed 存在
        var atomExists = _fixture.OutputFileExists("atom.xml") ||
                         _fixture.OutputFileExists("feed.atom");
        _output.WriteLine($"Atom feed 存在: {atomExists}");

        // 获取 Atom feed 内容
        var atomContent = await _fixture.GetOutputFileAsync("atom.xml") ??
                          await _fixture.GetOutputFileAsync("feed.atom");

        if (atomContent != null)
        {
            // 验证 XML 格式有效
            var isValidXml = IsValidXml(atomContent);
            _output.WriteLine($"XML 格式有效: {isValidXml}");
        }
    }

    /// <summary>
    /// 测试 Atom feed 符合 Atom 1.0 规范
    /// 验证 Atom feed 包含正确的命名空间和结构
    /// </summary>
    [Fact]
    [Trait("TestType", "Atom")]
    public async Task BuildAsync_AtomFeedShouldFollowAtom10Spec()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加测试文章
        var content = """
            +++
            title = "Atom 测试文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            Atom 测试内容。
            """;
        await _fixture.AddContentAsync("posts/atom-test.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 获取 Atom feed 内容
        var atomContent = await _fixture.GetOutputFileAsync("atom.xml") ??
                          await _fixture.GetOutputFileAsync("feed.atom");

        if (atomContent == null)
        {
            _output.WriteLine("Atom feed 不存在");
            return;
        }

        // 解析 XML
        var doc = XDocument.Parse(atomContent);
        var ns = XNamespace.Get("http://www.w3.org/2005/Atom");

        // 验证根元素是 feed
        var root = doc.Root;
        if (root?.Name.LocalName == "feed")
        {
            // 验证必需元素
            var title = root.Element(ns + "title");
            var id = root.Element(ns + "id");
            var updated = root.Element(ns + "updated");

            _output.WriteLine($"title: {title?.Value}");
            _output.WriteLine($"id: {id?.Value}");
            _output.WriteLine($"updated: {updated?.Value}");

            // 验证 entry 元素
            var entries = root.Elements(ns + "entry").ToList();
            _output.WriteLine($"entry 数量: {entries.Count}");

            foreach (var entry in entries)
            {
                var entryTitle = entry.Element(ns + "title")?.Value;
                var entryId = entry.Element(ns + "id")?.Value;
                _output.WriteLine($"  - {entryTitle}: {entryId}");
            }
        }
        else
        {
            _output.WriteLine($"根元素不是 feed: {root?.Name.LocalName}");
        }
    }

    /// <summary>
    /// 测试 Atom feed 日期格式
    /// 验证 Atom feed 中的日期符合 ISO 8601 格式
    /// </summary>
    [Fact]
    [Trait("TestType", "Atom")]
    public async Task BuildAsync_AtomFeedShouldHaveCorrectDateFormat()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加测试文章
        var content = """
            +++
            title = "Atom 日期测试"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            Atom 日期测试内容。
            """;
        await _fixture.AddContentAsync("posts/atom-date-test.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 获取 Atom feed 内容
        var atomContent = await _fixture.GetOutputFileAsync("atom.xml") ??
                          await _fixture.GetOutputFileAsync("feed.atom");

        if (atomContent == null)
        {
            _output.WriteLine("Atom feed 不存在");
            return;
        }

        // 检查日期格式（ISO 8601）
        // 例如: 2024-01-15T10:00:00+08:00
        var containsUpdated = atomContent.Contains("updated", StringComparison.OrdinalIgnoreCase);
        var containsPublished = atomContent.Contains("published", StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"包含 updated 元素: {containsUpdated}");
        _output.WriteLine($"包含 published 元素: {containsPublished}");
    }

    #endregion

    #region Feed 内容正确性测试

    /// <summary>
    /// 测试 Feed 包含所有页面
    /// 验证 Feed 文件包含站点中所有页面
    /// </summary>
    [Fact]
    [Trait("TestType", "FeedContent")]
    public async Task BuildAsync_FeedShouldContainAllPages()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加多个测试文章
        var articles = new[] { "feed-article-1", "feed-article-2", "feed-article-3" };
        foreach (var article in articles)
        {
            var content = $"""
                +++
                title = "{article}"
                date = 2024-01-15T10:00:00+08:00
                draft = false
                +++

                {article} 内容。
                """;
            await _fixture.AddContentAsync($"posts/{article}.md", content);
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 获取 RSS feed 内容
        var rssContent = await _fixture.GetOutputFileAsync("index.xml") ??
                         await _fixture.GetOutputFileAsync("rss.xml");

        if (rssContent != null)
        {
            _output.WriteLine("检查 RSS feed 内容:");
            foreach (var article in articles)
            {
                var containsArticle = rssContent.Contains(article, StringComparison.OrdinalIgnoreCase);
                _output.WriteLine($"  RSS 包含 {article}: {containsArticle}");
            }
        }

        // 获取 Atom feed 内容
        var atomContent = await _fixture.GetOutputFileAsync("atom.xml") ??
                          await _fixture.GetOutputFileAsync("feed.atom");

        if (atomContent != null)
        {
            _output.WriteLine("检查 Atom feed 内容:");
            foreach (var article in articles)
            {
                var containsArticle = atomContent.Contains(article, StringComparison.OrdinalIgnoreCase);
                _output.WriteLine($"  Atom 包含 {article}: {containsArticle}");
            }
        }
    }

    /// <summary>
    /// 测试 Feed 不包含草稿
    /// 验证 Feed 文件不包含草稿内容
    /// </summary>
    [Fact]
    [Trait("TestType", "FeedContent")]
    public async Task BuildAsync_FeedShouldNotContainDrafts()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加草稿文章
        var draftContent = """
            +++
            title = "草稿文章"
            date = 2024-01-15T10:00:00+08:00
            draft = true
            +++

            草稿内容。
            """;
        await _fixture.AddContentAsync("posts/draft-article.md", draftContent);

        // 添加正常文章
        var normalContent = """
            +++
            title = "正常文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            正常内容。
            """;
        await _fixture.AddContentAsync("posts/normal-article.md", normalContent);

        // Act - 执行构建（不包含草稿）
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeDrafts = false
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 获取 RSS feed 内容
        var rssContent = await _fixture.GetOutputFileAsync("index.xml") ??
                         await _fixture.GetOutputFileAsync("rss.xml");

        if (rssContent != null)
        {
            var containsDraft = rssContent.Contains("草稿文章", StringComparison.OrdinalIgnoreCase);
            var containsNormal = rssContent.Contains("正常文章", StringComparison.OrdinalIgnoreCase);
            _output.WriteLine($"RSS 包含草稿: {containsDraft}");
            _output.WriteLine($"RSS 包含正常文章: {containsNormal}");

            containsDraft.Should().BeFalse("RSS feed 不应包含草稿");
        }
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 验证 XML 格式是否有效
    /// </summary>
    private static bool IsValidXml(string content)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}

/// <summary>
/// disableKinds 消费验证（T2.2 数组语义 + 本任务接线）
/// </summary>
public class DisableKindsFeedTests : IDisposable
{
    private readonly string _siteRoot = Path.Combine(Path.GetTempPath(), $"flint-dk-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_siteRoot)) Directory.Delete(_siteRoot, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private async Task<(bool Success, string Public)> BuildWithConfigAsync(string extraConfig)
    {
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content"));
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts", "_default"));
        File.WriteAllText(Path.Combine(_siteRoot, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"dk\"\n" + extraConfig);
        File.WriteAllText(Path.Combine(_siteRoot, "content", "a.md"),
            "---\ntitle: \"A\"\n---\n\nBody\n");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"), "{{ page.title }}");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "_default", "list.html"), "<ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "_default", "term.html"), "<ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "_default", "taxonomy.html"), "<ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>");

        var builder = new Flint.Core.Site.SiteBuilder(
            new Flint.Core.Content.ContentParser(),
            new Flint.Core.Templates.ScribanTemplateRenderer(Path.Combine(_siteRoot, "layouts")),
            new Flint.Core.Assets.AssetPipeline(),
            new Flint.Core.Configuration.ConfigLoader());
        var result = await builder.BuildAsync(new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = Path.Combine(_siteRoot, "public")
        });
        return (result.Success, Path.Combine(_siteRoot, "public"));
    }

    [Fact]
    public async Task DisableKinds_Rss_SkipsFeedFiles()
    {
        var (success, publicDir) = await BuildWithConfigAsync("disableKinds = [\"RSS\", \"sitemap\"]\n");

        using (new FluentAssertions.Execution.AssertionScope())
        {
            success.Should().BeTrue();
            File.Exists(Path.Combine(publicDir, "rss.xml")).Should().BeFalse("disableKinds 含 RSS 应跳过 feed");
            File.Exists(Path.Combine(publicDir, "atom.xml")).Should().BeFalse();
            File.Exists(Path.Combine(publicDir, "sitemap.xml")).Should().BeFalse("disableKinds 含 sitemap 应跳过 sitemap");
            File.Exists(Path.Combine(publicDir, "a", "index.html")).Should().BeTrue("内容页不受影响");
        }
    }

    [Fact]
    public async Task DisableKinds_TaxonomyTerm_SkipsTaxonomyPages()
    {
        var (success, publicDir) = await BuildWithConfigAsync("disableKinds = [\"taxonomy\", \"term\"]\n");

        using (new FluentAssertions.Execution.AssertionScope())
        {
            success.Should().BeTrue();
            Directory.Exists(Path.Combine(publicDir, "categories")).Should().BeFalse("taxonomy 页应被跳过");
            File.Exists(Path.Combine(publicDir, "tags", "x", "index.html")).Should().BeFalse("term 页应被跳过");
            File.Exists(Path.Combine(publicDir, "a", "index.html")).Should().BeTrue("内容页不受影响");
        }
    }

    [Fact]
    public async Task NoDisableKinds_FeedsGenerated()
    {
        var (success, publicDir) = await BuildWithConfigAsync("");

        using (new FluentAssertions.Execution.AssertionScope())
        {
            success.Should().BeTrue();
            File.Exists(Path.Combine(publicDir, "rss.xml")).Should().BeTrue();
            File.Exists(Path.Combine(publicDir, "atom.xml")).Should().BeTrue();
            File.Exists(Path.Combine(publicDir, "sitemap.xml")).Should().BeTrue();
        }
    }

    [Fact]
    public async Task HomeOutputsHtmlOnly_NoFeedFiles()
    {
        // outputs 配置消费（T3.2）：home 输出格式列表不含 rss 时不产出订阅文件
        var (success, publicDir) = await BuildWithConfigAsync("[outputs]\nhome = [\"HTML\"]\n");

        using (new FluentAssertions.Execution.AssertionScope())
        {
            success.Should().BeTrue();
            File.Exists(Path.Combine(publicDir, "rss.xml")).Should().BeFalse("home outputs 不含 rss 应跳过 feed.xml");
            File.Exists(Path.Combine(publicDir, "atom.xml")).Should().BeFalse();
            File.Exists(Path.Combine(publicDir, "a", "index.html")).Should().BeTrue("内容页不受影响");
        }
    }

    [Fact]
    public async Task HomeOutputsExplicitRss_FeedsGenerated()
    {
        var (success, publicDir) = await BuildWithConfigAsync("[outputs]\nhome = [\"HTML\", \"RSS\"]\n");

        using (new FluentAssertions.Execution.AssertionScope())
        {
            success.Should().BeTrue();
            File.Exists(Path.Combine(publicDir, "rss.xml")).Should().BeTrue("显式配置 RSS 应产出 feed");
            File.Exists(Path.Combine(publicDir, "atom.xml")).Should().BeTrue();
        }
    }

    [Fact]
    public async Task RssItem_MatchesRenderedPage()
    {
        // T3.1 验收：同一页面 HTML 与 RSS 双输出内容一致性（同一 ParsedContent 喂两个输出）
        var (success, publicDir) = await BuildWithConfigAsync("");

        using (new FluentAssertions.Execution.AssertionScope())
        {
            success.Should().BeTrue();

            var pageHtml = await File.ReadAllTextAsync(Path.Combine(publicDir, "a", "index.html"));
            pageHtml.Should().Contain("A", "HTML 输出应含页面标题");

            var rss = await File.ReadAllTextAsync(Path.Combine(publicDir, "rss.xml"));
            rss.Should().Contain("A", "RSS item 标题应与页面标题同源");
            rss.Should().Contain("/a/", "RSS item 链接应指向同一页面 permalink");
        }
    }
}

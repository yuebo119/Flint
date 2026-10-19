// Flint 静态站点生成器
// Liquid 辅助模板端到端测试（FLUID-MIGRATION-PLAN P3-3）：Fluid 原生站点的
// 404.liquid / robots.liquid / sitemap.liquid / rss.liquid 覆盖各自的内置
// 生成器，产物由 Fluid 渲染。

using Flint.IntegrationTests.Utilities;
using AwesomeAssertions;
using Xunit;

namespace Flint.IntegrationTests.Cli;

/// <summary>
/// Liquid 辅助输出模板（404/robots/sitemap/rss）的端到端验证
/// </summary>
[Trait("Category", "CLI")]
[Trait("Category", "EndToEnd")]
public sealed class CliLiquidAuxTemplateTests : IAsyncLifetime
{
    private readonly CliTestRunner _cli;
    private readonly string _testDir;

    public CliLiquidAuxTemplateTests()
    {
        _cli = new CliTestRunner();
        _testDir = Path.Combine(Path.GetTempPath(), "Flint-cli-aux", Guid.NewGuid().ToString("N")[..8]);
    }

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_testDir);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _cli.Dispose();
        await Task.Delay(100);
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
    }

    /// <summary>Fluid 站点的 404/robots/sitemap/rss 模板覆盖内置生成器</summary>
    [Fact]
    public async Task Liquid_辅助模板_覆盖内置生成器()
    {
        var siteName = $"aux-{Guid.NewGuid():N}"[..12];
        var sitePath = Path.Combine(_testDir, siteName);

        var createResult = await _cli.NewSiteAsync(siteName, _testDir);
        createResult.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {createResult.ErrorOutput}");

        var layouts = Path.Combine(sitePath, "layouts");
        // 404：含 Liquid 条件与站点标题
        await File.WriteAllTextAsync(Path.Combine(layouts, "404.liquid"), """
            <!DOCTYPE html>
            <html><head><title>{% if page.title %}{{ page.title }}{% else %}未找到{% endif %} | {{ site.title }}</title></head>
            <body><h1>404 — {{ site.title }}</h1></body></html>
            """);
        // robots：Liquid 变量插值（脚手架 config 自带 params.description，
        // default 分支不触发——断言落到实际插值形态）
        await File.WriteAllTextAsync(Path.Combine(layouts, "robots.liquid"), """
            User-agent: *
            Disallow: /{{ site.params.description | default: 'private' }}/
            Sitemap: {{ site.base_url | default: 'http://localhost:1313/' }}sitemap.xml
            """);
        // sitemap：Liquid 循环（覆盖 SitemapGenerator）
        await File.WriteAllTextAsync(Path.Combine(layouts, "sitemap.liquid"), """
            <?xml version="1.0" encoding="utf-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
            {% for p in site.regular_pages %}<url><loc>{{ p.permalink }}</loc></url>
            {% endfor %}</urlset>
            """);
        // rss：Liquid 循环（覆盖 FeedGenerator）
        await File.WriteAllTextAsync(Path.Combine(layouts, "rss.liquid"), """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
            <title>{{ site.title }}</title>
            {% for p in site.regular_pages %}<item><title>{{ p.title }}</title><link>{{ p.permalink }}</link></item>
            {% endfor %}</channel></rss>
            """);

        var buildResult = await _cli.BuildAsync(workingDirectory: sitePath);

        buildResult.IsSuccess.Should().BeTrue($"构建应该成功，错误: {buildResult.ErrorOutput}\n{buildResult.StandardOutput}");

        var publicDir = Path.Combine(sitePath, "public");
        var notFound = await File.ReadAllTextAsync(Path.Combine(publicDir, "404.html"));
        notFound.Should().Contain("404 —", "404 应由 Liquid 模板渲染");

        var robots = await File.ReadAllTextAsync(Path.Combine(publicDir, "robots.txt"));
        robots.Should().Contain("Disallow: /使用 Flint 构建的站点/", "robots 应由 Liquid 模板渲染（params.description 来自脚手架配置）");
        robots.Should().Contain("Sitemap: http://localhost:1313/sitemap.xml", "robots 应由 Liquid 模板渲染（site.base_url 插值）");

        var sitemap = await File.ReadAllTextAsync(Path.Combine(publicDir, "sitemap.xml"));
        sitemap.Should().Contain("<urlset", "sitemap 应由 Liquid 模板覆盖内置生成器");
        sitemap.Should().Contain("<loc>", "sitemap 应含 Liquid 循环产出的 loc");

        var rss = await File.ReadAllTextAsync(Path.Combine(publicDir, "rss.xml"));
        rss.Should().Contain("<rss version=\"2.0\">", "rss 应由 Liquid 模板覆盖 FeedGenerator");
        rss.Should().Contain("<item>", "rss 应含 Liquid 循环产出的 item");
    }

    /// <summary>无模板时内置生成器兜底（与引擎无关）</summary>
    [Fact]
    public async Task 无辅助模板时_内置生成器兜底()
    {
        var siteName = $"auxgen-{Guid.NewGuid():N}"[..13];
        var sitePath = Path.Combine(_testDir, siteName);

        var createResult = await _cli.NewSiteAsync(siteName, _testDir);
        createResult.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {createResult.ErrorOutput}");

        var buildResult = await _cli.BuildAsync(workingDirectory: sitePath);

        buildResult.IsSuccess.Should().BeTrue($"构建应该成功，错误: {buildResult.ErrorOutput}");

        var publicDir = Path.Combine(sitePath, "public");
        File.Exists(Path.Combine(publicDir, "sitemap.xml")).Should().BeTrue("无 sitemap 模板时 SitemapGenerator 兜底");
        File.Exists(Path.Combine(publicDir, "rss.xml")).Should().BeTrue("无 rss 模板时 FeedGenerator 兜底");
        // 404 是模板门控产物（TemplateExists("404") 才产出，与 Hugo 的
        // "无 404 模板不产 404.html" 对齐）——无模板时不产出
        File.Exists(Path.Combine(publicDir, "404.html")).Should().BeFalse("无 404 模板时不产出");
    }
}

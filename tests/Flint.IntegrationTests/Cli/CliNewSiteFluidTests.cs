// Flint 静态站点生成器
// 新站点默认 Fluid 端到端测试（FLUID-MIGRATION-PLAN P2-4/P2-5）：
// 默认 `Flint new site` 生成 .liquid 脚手架且可构建（首页/单页/列表/分类/RSS）；
// --engine scriban 保持旧 .html 行为。

using Flint.IntegrationTests.Utilities;
using AwesomeAssertions;
using Xunit;

namespace Flint.IntegrationTests.Cli;

/// <summary>
/// 新站点默认 Fluid 的端到端验证
/// </summary>
[Trait("Category", "CLI")]
[Trait("Category", "EndToEnd")]
public sealed class CliNewSiteFluidTests : IAsyncLifetime
{
    private readonly CliTestRunner _cli;
    private readonly string _testDir;

    public CliNewSiteFluidTests()
    {
        _cli = new CliTestRunner();
        _testDir = Path.Combine(Path.GetTempPath(), "Flint-cli-fluid", Guid.NewGuid().ToString("N")[..8]);
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
        TryDelete(_testDir);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
    }

    /// <summary>默认新站点生成 .liquid 脚手架（无 .html 布局）</summary>
    [Fact]
    public async Task 默认新站点_生成_liquid_脚手架()
    {
        var siteName = $"fluid-default-{Guid.NewGuid():N}"[..16];
        var sitePath = Path.Combine(_testDir, siteName);

        var result = await _cli.NewSiteAsync(siteName, _testDir);

        result.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {result.ErrorOutput}");
        File.Exists(Path.Combine(sitePath, "layouts", "index.liquid")).Should().BeTrue("应有 index.liquid");
        File.Exists(Path.Combine(sitePath, "layouts", "_default", "single.liquid")).Should().BeTrue("应有 single.liquid");
        File.Exists(Path.Combine(sitePath, "layouts", "_default", "list.liquid")).Should().BeTrue("应有 list.liquid");
        File.Exists(Path.Combine(sitePath, "layouts", "_default", "taxonomy.liquid")).Should().BeTrue("应有 taxonomy.liquid");
        File.Exists(Path.Combine(sitePath, "layouts", "_default", "term.liquid")).Should().BeTrue("应有 term.liquid");
        File.Exists(Path.Combine(sitePath, "layouts", "partials", "head.liquid")).Should().BeTrue("应有 partials/head.liquid");
        File.Exists(Path.Combine(sitePath, "layouts", "partials", "header.liquid")).Should().BeTrue("应有 partials/header.liquid");
        File.Exists(Path.Combine(sitePath, "layouts", "partials", "footer.liquid")).Should().BeTrue("应有 partials/footer.liquid");
        // 不应有 .html 布局残留（默认 Fluid 站点零 Scriban 模板）
        Directory.EnumerateFiles(Path.Combine(sitePath, "layouts"), "*.html", SearchOption.AllDirectories)
            .Should().BeEmpty("默认新站点不应生成 .html 布局");
    }

    /// <summary>--engine scriban 仍生成旧 .html 脚手架</summary>
    [Fact]
    public async Task Engine_scriban_生成_html_脚手架()
    {
        var siteName = $"scriban-compat-{Guid.NewGuid():N}"[..17];
        var sitePath = Path.Combine(_testDir, siteName);

        var result = await _cli.NewSiteAsync(siteName, _testDir, engine: "scriban");

        result.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {result.ErrorOutput}");
        File.Exists(Path.Combine(sitePath, "layouts", "index.html")).Should().BeTrue("应有 index.html");
        File.Exists(Path.Combine(sitePath, "layouts", "_default", "single.html")).Should().BeTrue("应有 single.html");
        Directory.EnumerateFiles(Path.Combine(sitePath, "layouts"), "*.liquid", SearchOption.AllDirectories)
            .Should().BeEmpty("scriban 模式不应生成 .liquid");
    }

    /// <summary>
    /// P2-5 冒烟：默认新站点构建产出首页/单页/列表/分类/RSS 且内容为 Fluid 渲染
    /// </summary>
    [Fact]
    public async Task 默认新站点_构建冒烟_全页面类型()
    {
        var siteName = $"fluid-build-{Guid.NewGuid():N}"[..16];
        var sitePath = Path.Combine(_testDir, siteName);

        var createResult = await _cli.NewSiteAsync(siteName, _testDir);
        createResult.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {createResult.ErrorOutput}");

        // 补充带标签/分类的文章与 section 索引（脚手架自带 welcome.md 与 about.md；
        // _index.md 让 posts section 页真实产出——合成 section 无 _index.md 时
        // Flint 不产列表页，与 Hugo 的"无 _index.md 也产 section 页"是有意差异，
        // 见 SiteBuilder.Tree.IsNonProducingSynthesized）
        var postsDir = Path.Combine(sitePath, "content", "posts");
        Directory.CreateDirectory(postsDir);
        await File.WriteAllTextAsync(Path.Combine(postsDir, "_index.md"), """
            +++
            title = "文章"
            date = 2026-01-01T10:00:00+08:00
            draft = false
            +++
            """);
        await File.WriteAllTextAsync(Path.Combine(postsDir, "liquid-post.md"), """
            +++
            title = "Liquid 文章"
            date = 2026-03-01T10:00:00+08:00
            draft = false
            tags = ["Fluid"]
            categories = ["测试"]
            +++

            Liquid 渲染的内容。
            """);

        var buildResult = await _cli.BuildAsync(workingDirectory: sitePath);

        buildResult.IsSuccess.Should().BeTrue($"新站点构建应该成功，错误: {buildResult.ErrorOutput}\n{buildResult.StandardOutput}");

        var publicDir = Path.Combine(sitePath, "public");
        // 首页
        File.Exists(Path.Combine(publicDir, "index.html")).Should().BeTrue("应产出首页");
        // 单页
        File.Exists(Path.Combine(publicDir, "posts", "welcome", "index.html")).Should().BeTrue("应产出单页");
        // 列表页
        File.Exists(Path.Combine(publicDir, "posts", "index.html")).Should().BeTrue("应产出列表页");
        // 分类页（taxonomy + term）
        File.Exists(Path.Combine(publicDir, "tags", "index.html")).Should().BeTrue("应产出分类列表页");
        File.Exists(Path.Combine(publicDir, "tags", "fluid", "index.html")).Should().BeTrue("应产出词条页");
        // RSS（模板不存在时走内置 FeedGenerator，与引擎无关）
        File.Exists(Path.Combine(publicDir, "index.xml")).Should().BeTrue("应产出首页 RSS");

        // 内容确为 Fluid 渲染（站点标题出现在首页）
        var indexHtml = await File.ReadAllTextAsync(Path.Combine(publicDir, "index.html"));
        indexHtml.Should().Contain("欢迎来到", "首页应由 Liquid 脚手架渲染");
    }
}

// Flint 静态站点生成器
// 页面树 cascade 传播集成测试

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Models;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// cascade 级联传播集成测试（T2.2）
/// branch 节点（_index.md）front matter 的 cascade 块应级联到全部后代页面的 Params，
/// 页面自身显式 Params 优先于级联值（对齐 Hugo）
/// </summary>
public sealed class PageTreeCascadeTests : IDisposable
{
    private readonly string _siteRoot;

    public PageTreeCascadeTests()
    {
        _siteRoot = Path.Combine(Path.GetTempPath(), $"flint-cascade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_siteRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteRoot)) Directory.Delete(_siteRoot, recursive: true); }
        catch (IOException) { }
    }

    private void WriteSite()
    {
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content", "notes", "deep"));
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts"));

        File.WriteAllText(Path.Combine(_siteRoot, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"cascade-test\"\n");

        // branch 节点携带 cascade：banner 级联到全部后代；theme 被子页显式值覆盖
        File.WriteAllText(Path.Combine(_siteRoot, "content", "notes", "_index.md"), """
            +++
            title = "Notes"
            [cascade]
            banner = "from-notes-cascade"
            theme = "cascade-value"
            +++

            Notes section.
            """);

        // 深层子页：不显式设置 banner（应收到级联值），显式覆盖 theme
        File.WriteAllText(Path.Combine(_siteRoot, "content", "notes", "deep", "a.md"), """
            +++
            title = "Deep A"
            theme = "page-override"
            +++

            Body A.
            """);

        // 兄弟 section 的页面：不应收到 notes 的级联
        File.WriteAllText(Path.Combine(_siteRoot, "content", "other.md"), """
            +++
            title = "Other"
            +++

            Body Other.
            """);

        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<div>banner={{ page.params.banner }}</div><div>theme={{ page.params.theme }}</div>{{ page.content }}");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "list.html"),
            "<ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>");
    }

    private async Task<BuildResult> BuildAsync()
    {
        WriteSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        return await builder.BuildAsync(new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = Path.Combine(_siteRoot, "public")
        });
    }

    [Fact]
    public async Task Cascade_FlowsToAllDescendants()
    {
        var result = await BuildAsync();

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue("构建应成功: {0}",
                string.Join("; ", result.Errors.Select(e => e.Message)));

            var deepA = Path.Combine(_siteRoot, "public", "notes", "deep", "a", "index.html"); // 目录结构语义：/notes/deep/a/
            File.Exists(deepA).Should().BeTrue("深层子页应被构建");
            var html = File.ReadAllText(deepA, Encoding.UTF8);
            html.Should().Contain("banner=from-notes-cascade", "cascade 值应级联到深层后代");
        }
    }

    [Fact]
    public async Task Cascade_PageExplicitParamsOverride()
    {
        var result = await BuildAsync();

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue();
            var deepA = Path.Combine(_siteRoot, "public", "notes", "deep", "a", "index.html"); // 目录结构语义：/notes/deep/a/
            var html = File.ReadAllText(deepA, Encoding.UTF8);
            html.Should().Contain("theme=page-override", "页面显式 Params 应覆盖级联值");
        }
    }

    [Fact]
    public async Task Cascade_DoesNotLeakToSiblingSections()
    {
        var result = await BuildAsync();

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue();
            var other = Path.Combine(_siteRoot, "public", "other", "index.html");
            File.Exists(other).Should().BeTrue();
            var html = File.ReadAllText(other, Encoding.UTF8);
            html.Should().Contain("banner=", "兄弟 section 页面应存在");
            html.Should().NotContain("from-notes-cascade", "级联不得泄漏到兄弟 section");
        }
    }

    [Fact]
    public async Task Cascade_TargetPathMismatch_ShouldNotApply()
    {
        // cascade 带 _target.path = "/nomatch/**"——不匹配任何后代，Params 不下传
        WriteSite();
        File.WriteAllText(Path.Combine(_siteRoot, "content", "notes", "_index.md"), """
            +++
            title = "Notes"
            [cascade]
            banner = "should-not-appear"
            [cascade._target]
            path = "/nomatch/**"
            +++

            Notes section.
            """);

        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        var result = await builder.BuildAsync(new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = Path.Combine(_siteRoot, "public")
        });

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue();
            var html = File.ReadAllText(Path.Combine(_siteRoot, "public", "notes", "deep", "a", "index.html"), Encoding.UTF8);
            html.Should().NotContain("should-not-appear", "_target.path 不匹配时级联不生效");
        }
    }

    [Fact]
    public async Task Cascade_TargetKindPageOnly_ShouldApplyToPages()
    {
        // _target.kind = "page"：后代页面生效（kind 过滤命中）
        WriteSite();
        File.WriteAllText(Path.Combine(_siteRoot, "content", "notes", "_index.md"), """
            +++
            title = "Notes"
            [cascade]
            banner = "kind-filtered"
            [cascade._target]
            kind = "page"
            +++

            Notes section.
            """);

        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        var result = await builder.BuildAsync(new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = Path.Combine(_siteRoot, "public")
        });

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue();
            var html = File.ReadAllText(Path.Combine(_siteRoot, "public", "notes", "deep", "a", "index.html"), Encoding.UTF8);
            html.Should().Contain("banner=kind-filtered", "_target.kind=page 时后代页面应命中");
        }
    }

    [Fact]
    public async Task Cascade_FieldDefaults_TitleAndDraftShouldFlowToDescendants()
    {
        // 非 Params 字段（title/draft）也应下传：子页未设 title（得默认
        // "Untitled"）与 draft（false）时被 cascade 填充
        WriteSite();
        File.WriteAllText(Path.Combine(_siteRoot, "content", "notes", "_index.md"), """
            +++
            title = "Notes"
            [cascade]
            title = "Cascaded Title"
            draft = true
            +++

            Notes section.
            """);
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<div>title={{ page.title }}</div><div>draft={{ page.draft }}</div>{{ page.content }}");

        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        var result = await builder.BuildAsync(new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = Path.Combine(_siteRoot, "public")
        });

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue();
            // a.md 显式声明了 title: "Deep A"——页面显式值必须恒优先
            var htmlA = File.ReadAllText(Path.Combine(_siteRoot, "public", "notes", "deep", "a", "index.html"), Encoding.UTF8);
            htmlA.Should().Contain("title=Deep A", "页面显式 title 优先于级联");
            htmlA.Should().Contain("draft=true", "子页未设 draft 时级联 draft=true 应生效");
        }
    }
}

/// <summary>
/// 测试用站点构建器工厂（与 BuildHandler/ServeHandler 的生产装配保持一致）
/// </summary>
internal static class TestSiteFactory
{
    public static ISiteBuilder CreateBuilder(string siteRoot)
    {
        var configLoader = new Flint.Core.Configuration.ConfigLoader();
        var contentParser = new Flint.Core.Content.ContentParser();
        var templateRenderer = new Flint.Core.Templates.ScribanTemplateRenderer(
            Path.Combine(siteRoot, "layouts"));
        var assetPipeline = new Flint.Core.Assets.AssetPipeline();
        return new Flint.Core.Site.SiteBuilder(contentParser, templateRenderer, assetPipeline, configLoader);
    }
}

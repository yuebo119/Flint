// Flint 静态站点生成器
// 页面树增量精确替换集成测试（T2.3）

using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// 增量构建的树上精确替换行为：
/// 修改 → 原位替换节点；删除 → 摘除节点并清理陈旧输出；新增 → 插入 + 补缺 section。
/// 全量 siteContext 从树产出，不再重新解析全部文件
/// </summary>
public sealed class PageTreeIncrementalTests : IDisposable
{
    private readonly string _siteRoot;

    public PageTreeIncrementalTests()
    {
        _siteRoot = Path.Combine(Path.GetTempPath(), $"flint-incr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_siteRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteRoot)) Directory.Delete(_siteRoot, recursive: true); }
        catch (IOException) { }
    }

    private void WriteInitialSite()
    {
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content", "posts"));
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts"));

        File.WriteAllText(Path.Combine(_siteRoot, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"incr-test\"\n");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<article>{{ page.title }}:{{ page.content }}</article>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "list.html"),
            "<ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>");

        File.WriteAllText(Path.Combine(_siteRoot, "content", "posts", "a.md"),
            "---\ntitle: \"A\"\n---\n\nContent A\n");
        File.WriteAllText(Path.Combine(_siteRoot, "content", "posts", "b.md"),
            "---\ntitle: \"B\"\n---\n\nContent B\n");
    }

    private static Flint.Core.Models.BuildOptions Options(string siteRoot) => new()
    {
        SourcePath = siteRoot,
        OutputPath = Path.Combine(siteRoot, "public")
    };

    [Fact]
    public async Task Incremental_Update_ReplacesNodeContent()
    {
        WriteInitialSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options(_siteRoot))).Success.Should().BeTrue();

        // 修改 a.md 内容
        await File.WriteAllTextAsync(
            Path.Combine(_siteRoot, "content", "posts", "a.md"),
            "---\ntitle: \"A\"\n---\n\nUPDATED-A\n");

        var result = await builder.IncrementalBuildAsync(
            Options(_siteRoot), [Path.Combine(_siteRoot, "content", "posts", "a.md")]);

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue(": {0}", string.Join("; ", result.Errors.Select(e => e.Message)));
            var html = File.ReadAllText(
                Path.Combine(_siteRoot, "public", "a", "index.html"), System.Text.Encoding.UTF8);
            html.Should().Contain("UPDATED-A", "增量构建后内容应更新");
            html.Should().NotContain("Content A");
        }
    }

    [Fact]
    public async Task Incremental_Delete_RemovesStaleOutput()
    {
        WriteInitialSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options(_siteRoot))).Success.Should().BeTrue();
        var stalePage = Path.Combine(_siteRoot, "public", "b", "index.html");
        File.Exists(stalePage).Should().BeTrue("全量构建应生成 b 页");

        // 删除 b.md
        File.Delete(Path.Combine(_siteRoot, "content", "posts", "b.md"));

        var result = await builder.IncrementalBuildAsync(
            Options(_siteRoot), [Path.Combine(_siteRoot, "content", "posts", "b.md")]);

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue(": {0}", string.Join("; ", result.Errors.Select(e => e.Message)));
            File.Exists(stalePage).Should().BeFalse("删除页的陈旧输出应被清理");
        }
    }

    [Fact]
    public async Task Incremental_NewFile_InsertsAndBuilds()
    {
        WriteInitialSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options(_siteRoot))).Success.Should().BeTrue();

        // 新增 c.md（新 section，触发补缺合成）
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content", "docs"));
        await File.WriteAllTextAsync(
            Path.Combine(_siteRoot, "content", "docs", "c.md"),
            "---\ntitle: \"C\"\n---\n\nContent C\n");

        var result = await builder.IncrementalBuildAsync(
            Options(_siteRoot), [Path.Combine(_siteRoot, "content", "docs", "c.md")]);

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue(": {0}", string.Join("; ", result.Errors.Select(e => e.Message)));
            var newPage = Path.Combine(_siteRoot, "public", "c", "index.html");
            File.Exists(newPage).Should().BeTrue("新增页应被构建");
            File.ReadAllText(newPage).Should().Contain("Content C");
        }
    }

    [Fact]
    public async Task Incremental_TemplateChange_RebuildsAffectedPages()
    {
        // T4.2：模板变化走增量——依赖图反查受影响页用新模板重渲染，不再要求全量重建
        WriteInitialSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options(_siteRoot))).Success.Should().BeTrue();

        var singlePath = Path.Combine(_siteRoot, "layouts", "single.html");
        await File.WriteAllTextAsync(singlePath, "<section>{{ page.title }}</section>");

        var result = await builder.IncrementalBuildAsync(Options(_siteRoot), [singlePath]);

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue(": {0}", string.Join("; ", result.Errors.Select(e => e.Message)));

            var pageA = File.ReadAllText(
                Path.Combine(_siteRoot, "public", "a", "index.html"), System.Text.Encoding.UTF8);
            pageA.Should().Contain("<section>A</section>", "受影响页应使用新模板渲染");
            pageA.Should().NotContain("<article>", "旧模板输出应被替换");

            // 模板不被误当作静态资源复制到输出
            File.Exists(Path.Combine(_siteRoot, "public", "single.html")).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Incremental_TermTemplateChange_UpdatesTermPages()
    {
        // T4.2：词条页模板变化后 term 页输出同步更新（分类页在增量路径重产）
        WriteInitialSite();
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts"));
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "term.html"),
            "<term-old>{{ for p in pages }}{{ p.title }}{{ end }}</term-old>");

        await File.WriteAllTextAsync(
            Path.Combine(_siteRoot, "content", "posts", "a.md"),
            "---\ntitle: \"A\"\ntags: [\"t\"]\n---\n\nContent A\n");

        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options(_siteRoot))).Success.Should().BeTrue();
        var termPage = Path.Combine(_siteRoot, "public", "tags", "t", "index.html");
        File.Exists(termPage).Should().BeTrue("全量构建应产出词条页");
        File.ReadAllText(termPage).Should().Contain("term-old");

        // 修改 term.html 模板
        await File.WriteAllTextAsync(Path.Combine(_siteRoot, "layouts", "term.html"),
            "<term-new>{{ for p in pages }}{{ p.title }}{{ end }}</term-new>");

        var result = await builder.IncrementalBuildAsync(
            Options(_siteRoot), [Path.Combine(_siteRoot, "layouts", "term.html")]);

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue(": {0}", string.Join("; ", result.Errors.Select(e => e.Message)));
            File.ReadAllText(termPage).Should().Contain("term-new", "词条页应使用新模板");
            File.ReadAllText(termPage).Should().NotContain("term-old");
        }
    }

    [Fact]
    public async Task Incremental_TemplateChange_FastRenderMode_OnlyRendersVisitedUrls()
    {
        // T4.4：模板变化 + 访问集非空时，只重渲染访问中的页面（其余页输出保持不动）
        WriteInitialSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options(_siteRoot))).Success.Should().BeTrue();

        var singlePath = Path.Combine(_siteRoot, "layouts", "single.html");
        await File.WriteAllTextAsync(singlePath, "<fast>{{ page.title }}</fast>");

        var options = new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = Path.Combine(_siteRoot, "public"),
            PreferredUrls = (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/a/" }
        };

        var result = await builder.IncrementalBuildAsync(options, [singlePath]);

        using (new AssertionScope())
        {
            result.Success.Should().BeTrue(": {0}", string.Join("; ", result.Errors.Select(e => e.Message)));

            File.ReadAllText(Path.Combine(_siteRoot, "public", "a", "index.html"))
                .Should().Contain("<fast>A</fast>", "访问中的页面应秒刷为新模板");
            File.ReadAllText(Path.Combine(_siteRoot, "public", "b", "index.html"))
                .Should().Contain("<article>", "未访问页面的输出保持不变（fast render 语义）");
        }
    }
}

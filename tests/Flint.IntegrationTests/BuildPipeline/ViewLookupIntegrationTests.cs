// Flint 静态站点生成器
// 内容视图查找序集成测试（C4）
// 验证 .Render "view" 按页面路径逐级上溯、精确目录匹配、页面上下文

using Flint.IntegrationTests.Fixtures;
using AwesomeAssertions;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// C4 内容视图查找：render "view" 的候选路径按页面路径从深到浅，
/// 深层视图优先、不跨目录误命中、整条链缺失时输出为空
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
[Trait("TestType", "ViewLookup")]
public class ViewLookupIntegrationTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;

    public ViewLookupIntegrationTests()
    {
        _fixture = new TestSiteFixture();
    }

    public async ValueTask InitializeAsync() => await _fixture.InitializeAsync();

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    private async Task PrepareSiteAsync()
    {
        // minimal 夹具自带 content/index.md（home）；此处补 docs 树与所需模板
        await _fixture.CreateSiteAsync("minimal");
        await _fixture.SetConfigAsync("""
            baseURL = "https://example.org/"
            title = "View Lookup Test"
            """);
        await _fixture.AddContentAsync("docs/api/leaf.md", "---\ntitle: Deep\n---\nLeaf");
        await _fixture.AddContentAsync("docs/_index.md", "---\ntitle: Docs\n---\nDocs");
    }

    [Fact]
    public async Task 视图_深层目录视图优先于浅层()
    {
        await PrepareSiteAsync();
        // 单页渲染时查找 summary：/docs/api/leaf 深层命中 docs/api/summary
        await _fixture.AddTemplateAsync("_default/single.html", "S[{{ render \"summary\" }}]");
        await _fixture.AddTemplateAsync("docs/api/summary.html", "DEEP");
        await _fixture.AddTemplateAsync("docs/summary.html", "SHALLOW");
        await _fixture.AddTemplateAsync("summary.html", "ROOT");

        var result = await _fixture.BuildAsync();

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        var output = await _fixture.GetOutputFileAsync(Path.Combine("docs", "api", "leaf", "index.html"));
        output.Should().Be("S[DEEP]");
    }

    [Fact]
    public async Task 视图_深层缺失回退到浅层与根()
    {
        await PrepareSiteAsync();
        await _fixture.AddTemplateAsync("_default/single.html", "S[{{ render \"summary\" }}]");
        await _fixture.AddTemplateAsync("summary.html", "ROOT");

        await _fixture.BuildAsync();

        var output = await _fixture.GetOutputFileAsync(Path.Combine("docs", "api", "leaf", "index.html"));
        output.Should().Be("S[ROOT]");
    }

    [Fact]
    public async Task 视图_不跨目录误命中同名视图()
    {
        await PrepareSiteAsync();
        // 只有 other/summary 存在，docs 系页面不应命中
        await _fixture.AddTemplateAsync("_default/single.html", "S[{{ render \"summary\" }}]");
        await _fixture.AddTemplateAsync("other/summary.html", "OTHER");

        await _fixture.BuildAsync();

        var output = await _fixture.GetOutputFileAsync(Path.Combine("docs", "api", "leaf", "index.html"));
        output.Should().Be("S[]");
    }

    [Fact]
    public async Task 视图_整条链缺失时输出为空()
    {
        await PrepareSiteAsync();
        await _fixture.AddTemplateAsync("_default/single.html", "S[{{ render \"summary\" }}]");

        await _fixture.BuildAsync();

        var output = await _fixture.GetOutputFileAsync(Path.Combine("docs", "api", "leaf", "index.html"));
        output.Should().Be("S[]");
    }

    [Fact]
    public async Task 视图_以当前页为上下文()
    {
        await PrepareSiteAsync();
        await _fixture.AddTemplateAsync("_default/single.html", "S[{{ render \"summary\" }}]");
        // 视图内使用 page.* —— 上下文必须是当前页
        await _fixture.AddTemplateAsync("docs/api/summary.html", "{{ page.title }}:");

        await _fixture.BuildAsync();

        var output = await _fixture.GetOutputFileAsync(Path.Combine("docs", "api", "leaf", "index.html"));
        output.Should().Be("S[Deep:]");
    }

    [Fact]
    public async Task 视图_section页同样逐级查找()
    {
        await PrepareSiteAsync();
        await _fixture.AddTemplateAsync("_default/list.html", "L[{{ render \"summary\" }}]");
        await _fixture.AddTemplateAsync("docs/summary.html", "DOCS-SUMMARY");
        await _fixture.AddTemplateAsync("summary.html", "ROOT");

        await _fixture.BuildAsync();

        var output = await _fixture.GetOutputFileAsync(Path.Combine("docs", "index.html"));
        output.Should().Be("L[DOCS-SUMMARY]");
    }

    [Fact]
    public async Task 视图_显式接收者以该页为上下文()
    {
        await PrepareSiteAsync();
        // list 模板遍历页面集合并以第二参数指定渲染上下文（对齐 Hugo .Render 页面方法）
        await _fixture.AddTemplateAsync(
            "_default/list.html",
            "L:{{ for p in page.pages }}({{ render \"summary\" p }}){{ end }}");
        await _fixture.AddTemplateAsync("summary.html", "{{ page.title }}");

        await _fixture.BuildAsync();

        var output = await _fixture.GetOutputFileAsync(Path.Combine("docs", "index.html"));
        // 条目 summary 渲染自身标题，而不是外层列表页标题
        output.Should().Contain("(Deep)");
    }

    [Fact]
    public async Task 视图_显式接收者按接收者路径逐级查找()
    {
        await PrepareSiteAsync();
        // 接收者在 docs/api 下，其 summary 应从 docs/api/summary 解析
        await _fixture.AddTemplateAsync(
            "_default/list.html",
            "L:{{ for p in page.pages }}{{ render \"summary\" p }}{{ end }}");
        await _fixture.AddTemplateAsync("docs/api/summary.html", "DEEP");
        await _fixture.AddTemplateAsync("summary.html", "ROOT");

        await _fixture.BuildAsync();

        var output = await _fixture.GetOutputFileAsync(Path.Combine("docs", "index.html"));
        output.Should().Be("L:DEEP");
    }
}

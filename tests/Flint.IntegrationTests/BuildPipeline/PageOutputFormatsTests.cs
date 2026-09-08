// 页面级多输出格式集成测试（对齐 Hugo 页面级 outputs 覆盖）

using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// 页面级 outputs 覆盖与多格式渲染集成测试：
/// front matter outputs = ["html","json"] 的页面应额外产出
/// &lt;permalink&gt;/index.json（由 single.json.html 变体模板渲染）；
/// 无对应变体模板的格式跳过；未声明 outputs 的页面不受影响
/// </summary>
public sealed class PageOutputFormatsTests : IDisposable
{
    private readonly string _siteRoot = Path.Combine(
        Path.GetTempPath(), $"flint-outfmt-{Guid.NewGuid():N}");
    private readonly string _outputRoot;

    public PageOutputFormatsTests()
    {
        _outputRoot = Path.Combine(_siteRoot, "public");
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content"));
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts"));

        File.WriteAllText(Path.Combine(_siteRoot, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"outfmt-test\"\n");

        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<article>{{ page.title }}</article>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "list.html"),
            "<ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteRoot)) Directory.Delete(_siteRoot, recursive: true); }
        catch (IOException) { }
    }

    private async Task<Flint.Core.Models.BuildResult> BuildAsync()
    {
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        return await builder.BuildAsync(new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = _outputRoot
        });
    }

    [Fact]
    public async Task PageOutputs_Json_ShouldProduceIndexJsonViaVariantTemplate()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content", "posts"));
        File.WriteAllText(Path.Combine(_siteRoot, "content", "posts", "a.md"), """
            +++
            title = "Post A"
            outputs = ["html", "json"]
            +++

            Body A.
            """);
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.json.html"),
            "{\"title\":\"{{ page.title }}\"}");

        // Act
        var result = await BuildAsync();

        // Assert
        result.Success.Should().BeTrue(": {0}", string.Join("; ", result.Errors.Select(e => e.Message)));
        var jsonPath = Path.Combine(_outputRoot, "posts", "a", "index.json");
        File.Exists(jsonPath).Should().BeTrue("json 输出格式应产出 index.json");
        File.ReadAllText(jsonPath).Should().Contain("\"Post A\"");
        // html 主格式不受影响
        File.ReadAllText(Path.Combine(_outputRoot, "posts", "a", "index.html"))
            .Should().Contain("<article>Post A</article>");
    }

    [Fact]
    public async Task PageOutputs_JsonWithoutVariantTemplate_ShouldSkipJsonOnly()
    {
        // Arrange - 声明 json 输出但无 single.json.html 变体模板：跳过 json，html 正常
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content", "posts"));
        File.WriteAllText(Path.Combine(_siteRoot, "content", "posts", "b.md"), """
            +++
            title = "Post B"
            outputs = ["html", "json"]
            +++

            Body B.
            """);

        // Act
        var result = await BuildAsync();

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_outputRoot, "posts", "b", "index.html")).Should().BeTrue("html 主格式应正常产出");
        File.Exists(Path.Combine(_outputRoot, "posts", "b", "index.json")).Should().BeFalse("无变体模板时 json 应跳过");
    }

    [Fact]
    public async Task PageOutputs_WithoutFrontMatter_ShouldNotProduceJson()
    {
        // Arrange - 未声明 outputs 的页面不应产出 json（回归保护）
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content"));
        File.WriteAllText(Path.Combine(_siteRoot, "content", "plain.md"), """
            +++
            title = "Plain"
            +++

            Body.
            """);
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.json.html"),
            "{\"title\":\"{{ page.title }}\"}");

        // Act
        var result = await BuildAsync();

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_outputRoot, "posts", "plain", "index.json")).Should().BeFalse(
            "未声明 json 输出的页面不应产出 index.json");
    }
}

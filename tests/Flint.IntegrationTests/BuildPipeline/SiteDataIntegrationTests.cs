// Flint 静态站点生成器
// .Site.Data 集成测试——data/ 目录内容经 DataFileLoader 加载后
// 对模板可见（site.data.*），对齐 Hugo 的 data 目录特性

using System.Text;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// .Site.Data 端到端测试：data/ 目录（YAML/TOML）→ DataFileLoader →
/// SiteContext.Data → 模板 site.data.* 渲染
/// </summary>
public sealed class SiteDataIntegrationTests : IDisposable
{
    private readonly string _siteRoot = Path.Combine(
        Path.GetTempPath(), $"flint-sitedata-{Guid.NewGuid():N}");
    private readonly string _outputRoot;

    public SiteDataIntegrationTests()
    {
        _outputRoot = Path.Combine(_siteRoot, "public");
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content"));
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts"));
        Directory.CreateDirectory(Path.Combine(_siteRoot, "data"));

        File.WriteAllText(Path.Combine(_siteRoot, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"sitedata-test\"\n");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteRoot)) Directory.Delete(_siteRoot, recursive: true); }
        catch (IOException) { }
    }

    private async Task<Flint.Core.Models.BuildResult> BuildAsync()
    {
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        var result = await builder.BuildAsync(new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = _outputRoot
        });
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "构建失败: " + string.Join(" | ", result.Errors.Select(e => e.Message)));
        }
        return result;
    }

    [Fact]
    public async Task SiteData_YamlFile_ShouldBeVisibleToTemplates()
    {
        // Arrange - YAML 数据文件 + 消费它的模板
        File.WriteAllText(Path.Combine(_siteRoot, "data", "authors.yml"),
            "alice:" + Environment.NewLine + "  name: Alice" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<div>DATA={{ site.data.authors.alice.name }}</div>");
        File.WriteAllText(Path.Combine(_siteRoot, "content", "a.md"),
            "---\ntitle: A\n---\nA");

        // Act
        var result = await BuildAsync();

        // Assert - data 内容对模板可见（经 DataFileLoader → SiteContext.Data）
        var rendered = File.ReadAllText(Path.Combine(_outputRoot, "a", "index.html"), Encoding.UTF8);
        rendered.Should().Contain("DATA=Alice", "YAML data 应对模板可见");
    }

    [Fact]
    public async Task SiteData_TomlFile_ShouldBeVisibleToTemplates()
    {
        // Arrange - TOML 数据文件
        File.WriteAllText(Path.Combine(_siteRoot, "data", "team.toml"),
            "[lead]" + Environment.NewLine + "name = \"Bob\"" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<div>TOML-DATA={{ site.data.team.lead.name }}</div>");
        File.WriteAllText(Path.Combine(_siteRoot, "content", "p.md"),
            "---\ntitle: P\n---\nBody.\n");

        // Act
        var result = await BuildAsync();

        // Assert
        var rendered = File.ReadAllText(Path.Combine(_outputRoot, "p", "index.html"), Encoding.UTF8);
        rendered.Should().Contain("TOML-DATA=Bob", "TOML data 应对模板可见");
    }

    [Fact]
    public async Task SiteData_DataDirectoryMissing_ShouldBuildSuccessfully()
    {
        // Arrange - 无 data/ 目录：构建不失败。构造函数默认建了 data/，
        // 显式删除才能覆盖"目录不存在"分支（空目录走的是另一条路径）
        var dataDir = Path.Combine(_siteRoot, "data");
        if (Directory.Exists(dataDir))
        {
            Directory.Delete(dataDir, recursive: true);
        }
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<div>NO-DATA</div>");
        File.WriteAllText(Path.Combine(_siteRoot, "content", "a.md"),
            "---\ntitle: A\n---\nA");

        // Act
        var result = await BuildAsync();

        // Assert
        result.Success.Should().BeTrue("缺 data/ 目录不应导致构建失败");
    }
}

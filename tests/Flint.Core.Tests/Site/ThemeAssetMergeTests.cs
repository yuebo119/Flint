// Flint 静态站点生成器
// 主题 static/assets 资源合并回归测试（对齐 Hugo 主题语义：站点覆盖主题）

using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// 主题资源合并：站点 static/assets 优先，主题回退，同名覆盖，主题独有资源产出
/// </summary>
public sealed class ThemeAssetMergeTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _outputDir;

    public ThemeAssetMergeTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"Flint_theme_asset_{Guid.NewGuid():N}");
        _outputDir = Path.Combine(_testDir, "public");
        Directory.CreateDirectory(Path.Combine(_testDir, "content", "posts"));
        Directory.CreateDirectory(Path.Combine(_testDir, "layouts", "_default"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task Build_主题资源应合并且站点覆盖同名()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_testDir, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"T\"\ntheme = \"t1\"\n");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "content", "posts", "a.md"),
            "---\ntitle: A\n---\nA");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "layouts", "_default", "single.html"),
            "S={{ page.title }}");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "layouts", "index.html"), "home");

        // 站点与主题同名资源 + 主题独有资源
        var siteCss = Path.Combine(_testDir, "static", "css", "site.css");
        var themeCss = Path.Combine(_testDir, "themes", "t1", "static", "css", "site.css");
        var themeJs = Path.Combine(_testDir, "themes", "t1", "static", "js", "theme.js");
        Directory.CreateDirectory(Path.GetDirectoryName(siteCss)!);
        Directory.CreateDirectory(Path.GetDirectoryName(themeCss)!);
        Directory.CreateDirectory(Path.GetDirectoryName(themeJs)!);
        await File.WriteAllTextAsync(siteCss, "site-value");
        await File.WriteAllTextAsync(themeCss, "theme-value");
        await File.WriteAllTextAsync(themeJs, "theme-only");
        // Arrange 自验：三个源文件在构建前必须可见（区分测试环境 IO 时序与构建收集缺陷）
        Assert.True(File.Exists(siteCss), "Arrange 自验失败：站点 css 不可见");
        Assert.True(File.Exists(themeCss), "Arrange 自验失败：主题 css 不可见");
        Assert.True(File.Exists(themeJs), "Arrange 自验失败：主题 js 不可见");

        var builder = new Flint.Core.Site.SiteBuilder(
            new Flint.Core.Content.ContentParser(),
            new Flint.Core.Templates.ScribanTemplateRenderer(
                Path.Combine(_testDir, "layouts"), "http://localhost:1313"),
            // 对齐生产装配：SourceDirectory/OutputDirectory 必须为站点绝对路径，
            // 相对默认值会让绝对资源路径被逃逸防护误判（TestSiteFixture 同教训）
            new Flint.Core.Assets.AssetPipeline(new Flint.Core.Assets.AssetPipelineOptions
            {
                SourceDirectory = _testDir,
                OutputDirectory = _outputDir
            }),
            new Flint.Core.Configuration.ConfigLoader());
        var options = new Flint.Core.Models.BuildOptions
        {
            SourcePath = _testDir,
            OutputPath = _outputDir
        };

        // Act
        var result = await builder.BuildAsync(options);

        // Assert
        Assert.True(result.Success, string.Join(";", result.Errors.Select(e => e.Message)));
        // 3 源 2 键：主题 css 与站点 css 同相对路径被覆盖合并，theme.js 独立产出
        Assert.True(result.AssetsProcessed == 2,
            $"资源处理数异常: {result.AssetsProcessed}（预期 2）");
        // 同名：站点覆盖主题
        var cssPath = Path.Combine(_outputDir, "static", "css", "site.css");
        Assert.True(File.Exists(cssPath), "同名资源应产出");
        Assert.Equal("site-value", await File.ReadAllTextAsync(cssPath));
        // 主题独有：正常产出（重映射到站点命名空间，不落 public/themes）
        Assert.Equal("theme-only",
            await File.ReadAllTextAsync(Path.Combine(_outputDir, "static", "js", "theme.js")));
        Assert.False(Directory.Exists(Path.Combine(_outputDir, "themes")),
            "主题资源不得残留 themes/ 目录链");
    }
}

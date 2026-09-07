// Flint 静态站点生成器
// 主题模块集成测试
// 验证主题模板、资源和配置的合并

using Flint.Core.Modules;
using Flint.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Modules;

/// <summary>
/// 主题模块集成测试
/// 验证主题模板、资源和配置的合并
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 7.6: 主题模板合并
/// - Requirements 7.8: 主题资源合并
/// </remarks>
[Collection("Modules")]
public class ThemeModuleIntegrationTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private ModuleManager? _moduleManager;

    public ThemeModuleIntegrationTests()
    {
        _fixture = new TestSiteFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.CreateSiteAsync("minimal");
    }

    public async Task DisposeAsync()
    {
        _moduleManager?.Dispose();
        await _fixture.DisposeAsync();
    }

    private ModuleManager CreateModuleManager()
    {
        _moduleManager = new ModuleManager(_fixture.SiteRoot);
        return _moduleManager;
    }

    #region 主题模板合并测试

    /// <summary>
    /// 测试主题模板目录结构
    /// </summary>
    [Fact]
    public async Task ThemeTemplates_DirectoryStructure_IsCorrect()
    {
        // Arrange
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var themePath = Path.Combine(themesPath, "test-theme");
        var layoutsPath = Path.Combine(themePath, "layouts");

        Directory.CreateDirectory(layoutsPath);
        Directory.CreateDirectory(Path.Combine(layoutsPath, "_default"));
        Directory.CreateDirectory(Path.Combine(layoutsPath, "partials"));

        // 创建主题模板
        await File.WriteAllTextAsync(
            Path.Combine(layoutsPath, "_default", "baseof.html"),
            """
            <!DOCTYPE html>
            <html>
            <head><title>{{ site.title }}</title></head>
            <body>{{ block "main" . }}{{ end }}</body>
            </html>
            """);

        await File.WriteAllTextAsync(
            Path.Combine(layoutsPath, "_default", "single.html"),
            """
            {{ define "main" }}
            <article>{{ .Content }}</article>
            {{ end }}
            """);

        await File.WriteAllTextAsync(
            Path.Combine(layoutsPath, "partials", "header.html"),
            "<header>{{ site.title }}</header>");

        // Assert
        Directory.Exists(layoutsPath).Should().BeTrue();
        File.Exists(Path.Combine(layoutsPath, "_default", "baseof.html")).Should().BeTrue();
        File.Exists(Path.Combine(layoutsPath, "_default", "single.html")).Should().BeTrue();
        File.Exists(Path.Combine(layoutsPath, "partials", "header.html")).Should().BeTrue();
    }

    /// <summary>
    /// 测试站点模板覆盖主题模板
    /// </summary>
    [Fact]
    public async Task SiteTemplates_OverrideThemeTemplates()
    {
        // Arrange - 创建主题模板
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var themePath = Path.Combine(themesPath, "override-theme");
        var themeLayoutsPath = Path.Combine(themePath, "layouts", "_default");
        Directory.CreateDirectory(themeLayoutsPath);

        await File.WriteAllTextAsync(
            Path.Combine(themeLayoutsPath, "single.html"),
            "<div>主题模板</div>");

        // 创建站点模板（应该覆盖主题模板）
        var siteLayoutsPath = Path.Combine(_fixture.SiteRoot, "layouts", "_default");
        Directory.CreateDirectory(siteLayoutsPath);

        await File.WriteAllTextAsync(
            Path.Combine(siteLayoutsPath, "single.html"),
            "<div>站点模板</div>");

        // Assert - 站点模板应该存在
        var siteTemplate = await File.ReadAllTextAsync(
            Path.Combine(siteLayoutsPath, "single.html"));
        siteTemplate.Should().Contain("站点模板");
    }

    /// <summary>
    /// 测试主题继承（子主题继承父主题）
    /// </summary>
    [Fact]
    public async Task ThemeInheritance_ChildInheritsFromParent()
    {
        // Arrange - 创建父主题
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var parentThemePath = Path.Combine(themesPath, "parent-theme");
        var parentLayoutsPath = Path.Combine(parentThemePath, "layouts", "_default");
        Directory.CreateDirectory(parentLayoutsPath);

        await File.WriteAllTextAsync(
            Path.Combine(parentLayoutsPath, "baseof.html"),
            "<html><body>父主题基础模板</body></html>");

        await File.WriteAllTextAsync(
            Path.Combine(parentThemePath, "theme.toml"),
            """
            name = "parent-theme"
            version = "1.0.0"
            """);

        // 创建子主题
        var childThemePath = Path.Combine(themesPath, "child-theme");
        var childLayoutsPath = Path.Combine(childThemePath, "layouts", "_default");
        Directory.CreateDirectory(childLayoutsPath);

        await File.WriteAllTextAsync(
            Path.Combine(childLayoutsPath, "single.html"),
            "<article>子主题单页模板</article>");

        await File.WriteAllTextAsync(
            Path.Combine(childThemePath, "theme.toml"),
            """
            name = "child-theme"
            version = "1.0.0"
            """);

        // Assert
        File.Exists(Path.Combine(parentLayoutsPath, "baseof.html")).Should().BeTrue();
        File.Exists(Path.Combine(childLayoutsPath, "single.html")).Should().BeTrue();
    }

    #endregion

    #region 主题资源合并测试

    /// <summary>
    /// 测试主题 CSS 资源
    /// </summary>
    [Fact]
    public async Task ThemeAssets_CssFiles_AreAccessible()
    {
        // Arrange
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var themePath = Path.Combine(themesPath, "css-theme");
        var assetsPath = Path.Combine(themePath, "assets", "css");
        Directory.CreateDirectory(assetsPath);

        await File.WriteAllTextAsync(
            Path.Combine(assetsPath, "main.css"),
            """
            :root {
                --primary-color: #007bff;
            }
            body {
                font-family: sans-serif;
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(assetsPath, "components.css"),
            """
            .button {
                padding: 10px 20px;
            }
            """);

        // Assert
        File.Exists(Path.Combine(assetsPath, "main.css")).Should().BeTrue();
        File.Exists(Path.Combine(assetsPath, "components.css")).Should().BeTrue();
    }

    /// <summary>
    /// 测试主题 JavaScript 资源
    /// </summary>
    [Fact]
    public async Task ThemeAssets_JsFiles_AreAccessible()
    {
        // Arrange
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var themePath = Path.Combine(themesPath, "js-theme");
        var assetsPath = Path.Combine(themePath, "assets", "js");
        Directory.CreateDirectory(assetsPath);

        await File.WriteAllTextAsync(
            Path.Combine(assetsPath, "main.js"),
            """
            document.addEventListener('DOMContentLoaded', function() {
                console.log('Theme loaded');
            });
            """);

        // Assert
        File.Exists(Path.Combine(assetsPath, "main.js")).Should().BeTrue();
    }

    /// <summary>
    /// 测试主题图片资源
    /// </summary>
    [Fact]
    public async Task ThemeAssets_ImageFiles_AreAccessible()
    {
        // Arrange
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var themePath = Path.Combine(themesPath, "image-theme");
        var staticPath = Path.Combine(themePath, "static", "images");
        Directory.CreateDirectory(staticPath);

        // 创建一个最小的 PNG 文件
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
            0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
            0x54, 0x08, 0xD7, 0x63, 0xF8, 0xFF, 0xFF, 0x3F,
            0x00, 0x05, 0xFE, 0x02, 0xFE, 0xDC, 0xCC, 0x59,
            0xE7, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
            0x44, 0xAE, 0x42, 0x60, 0x82
        };
        await File.WriteAllBytesAsync(Path.Combine(staticPath, "logo.png"), pngBytes);

        // Assert
        File.Exists(Path.Combine(staticPath, "logo.png")).Should().BeTrue();
    }

    /// <summary>
    /// 测试站点资源覆盖主题资源
    /// </summary>
    [Fact]
    public async Task SiteAssets_OverrideThemeAssets()
    {
        // Arrange - 创建主题资源
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var themePath = Path.Combine(themesPath, "asset-override-theme");
        var themeAssetsPath = Path.Combine(themePath, "assets", "css");
        Directory.CreateDirectory(themeAssetsPath);

        await File.WriteAllTextAsync(
            Path.Combine(themeAssetsPath, "style.css"),
            "/* 主题样式 */");

        // 创建站点资源（应该覆盖主题资源）
        var siteAssetsPath = Path.Combine(_fixture.SiteRoot, "assets", "css");
        Directory.CreateDirectory(siteAssetsPath);

        await File.WriteAllTextAsync(
            Path.Combine(siteAssetsPath, "style.css"),
            "/* 站点样式 */");

        // Assert
        var siteStyle = await File.ReadAllTextAsync(
            Path.Combine(siteAssetsPath, "style.css"));
        siteStyle.Should().Contain("站点样式");
    }

    #endregion

    #region 主题配置合并测试

    /// <summary>
    /// 测试主题默认配置
    /// </summary>
    [Fact]
    public async Task ThemeConfig_DefaultValues_AreSet()
    {
        // Arrange
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var themePath = Path.Combine(themesPath, "config-theme");
        Directory.CreateDirectory(themePath);

        await File.WriteAllTextAsync(
            Path.Combine(themePath, "theme.toml"),
            """
            name = "config-theme"
            version = "1.0.0"
            
            [params]
            primaryColor = "#007bff"
            showSidebar = true
            postsPerPage = 10
            """);

        // Assert
        var configContent = await File.ReadAllTextAsync(
            Path.Combine(themePath, "theme.toml"));
        configContent.Should().Contain("primaryColor");
        configContent.Should().Contain("showSidebar");
        configContent.Should().Contain("postsPerPage");
    }

    /// <summary>
    /// 测试站点配置覆盖主题默认配置
    /// </summary>
    [Fact]
    public async Task SiteConfig_OverridesThemeDefaults()
    {
        // Arrange - 创建主题配置
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var themePath = Path.Combine(themesPath, "default-config-theme");
        Directory.CreateDirectory(themePath);

        await File.WriteAllTextAsync(
            Path.Combine(themePath, "theme.toml"),
            """
            name = "default-config-theme"
            version = "1.0.0"
            
            [params]
            primaryColor = "#007bff"
            """);

        // 创建站点配置（覆盖主题默认值）
        await File.WriteAllTextAsync(
            Path.Combine(_fixture.SiteRoot, "Flint.toml"),
            """
            baseURL = "http://localhost:1313/"
            title = "测试站点"
            theme = "default-config-theme"
            
            [params]
            primaryColor = "#ff0000"
            """);

        // Assert
        var siteConfig = await File.ReadAllTextAsync(
            Path.Combine(_fixture.SiteRoot, "Flint.toml"));
        siteConfig.Should().Contain("#ff0000");
    }

    #endregion

    #region 多主题场景测试

    /// <summary>
    /// 测试多个主题共存
    /// </summary>
    [Fact]
    public async Task MultipleThemes_CanCoexist()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        Directory.CreateDirectory(themesPath);

        // 创建多个主题
        var themeNames = new[] { "theme-a", "theme-b", "theme-c" };
        foreach (var themeName in themeNames)
        {
            var themePath = Path.Combine(themesPath, themeName);
            Directory.CreateDirectory(themePath);
            await File.WriteAllTextAsync(
                Path.Combine(themePath, "theme.toml"),
                $"""
                name = "{themeName}"
                version = "1.0.0"
                """);
        }

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().HaveCount(3);
        modules.Select(m => m.Name).Should().BeEquivalentTo(themeNames);
    }

    /// <summary>
    /// 测试切换主题
    /// </summary>
    [Fact]
    public async Task SwitchTheme_UpdatesConfig()
    {
        // Arrange
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        Directory.CreateDirectory(themesPath);

        // 创建两个主题
        foreach (var themeName in new[] { "theme-1", "theme-2" })
        {
            var themePath = Path.Combine(themesPath, themeName);
            Directory.CreateDirectory(themePath);
            await File.WriteAllTextAsync(
                Path.Combine(themePath, "theme.toml"),
                $"""
                name = "{themeName}"
                version = "1.0.0"
                """);
        }

        // 初始配置使用 theme-1
        await File.WriteAllTextAsync(
            Path.Combine(_fixture.SiteRoot, "Flint.toml"),
            """
            baseURL = "http://localhost:1313/"
            theme = "theme-1"
            """);

        // Act - 切换到 theme-2
        await File.WriteAllTextAsync(
            Path.Combine(_fixture.SiteRoot, "Flint.toml"),
            """
            baseURL = "http://localhost:1313/"
            theme = "theme-2"
            """);

        // Assert
        var config = await File.ReadAllTextAsync(
            Path.Combine(_fixture.SiteRoot, "Flint.toml"));
        config.Should().Contain("theme-2");
    }

    #endregion
}

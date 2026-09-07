// Flint 静态站点生成器
// 配置加载器集成测试
// 测试 TOML、YAML、JSON 配置加载和环境变量覆盖

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Configuration;

/// <summary>
/// ConfigLoader 集成测试
/// 验证配置加载、解析和保存功能
/// </summary>
public class ConfigLoaderIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigLoader _loader;

    public ConfigLoaderIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _loader = new ConfigLoader();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    #region TOML 配置加载测试

    [Fact]
    public async Task LoadAsync_WithTomlConfig_ShouldParseAllFields()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "My Test Site"
            languageCode = "zh-CN"
            theme = "minimal"
            buildDrafts = true
            buildFuture = true
            buildExpired = false
            paginate = 20
            paginatePath = "pages"
            enableGitInfo = true
            summaryLength = 100
            copyright = "© 2024 Test"
            contentDir = "posts"
            layoutDir = "templates"
            staticDir = "public"
            assetDir = "resources"
            dataDir = "data"
            publishDir = "dist"
            archetypeDir = "scaffolds"
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.BaseURL.Should().Be("https://example.com/");
        config.Title.Should().Be("My Test Site");
        config.LanguageCode.Should().Be("zh-CN");
        config.Theme.Should().Be("minimal");
        config.BuildDrafts.Should().BeTrue();
        config.BuildFuture.Should().BeTrue();
        config.BuildExpired.Should().BeFalse();
        config.Paginate.Should().Be(20);
        config.PaginatePath.Should().Be("pages");
        config.EnableGitInfo.Should().BeTrue();
        config.SummaryLength.Should().Be(100);
        config.Copyright.Should().Be("© 2024 Test");
        config.ContentDir.Should().Be("posts");
        config.LayoutDir.Should().Be("templates");
        config.StaticDir.Should().Be("public");
        config.AssetDir.Should().Be("resources");
        config.DataDir.Should().Be("data");
        config.PublishDir.Should().Be("dist");
        config.ArchetypeDir.Should().Be("scaffolds");
    }

    [Fact]
    public async Task LoadAsync_WithTomlPermalinks_ShouldParsePermalinks()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "Test"
            
            [permalinks]
            posts = "/:year/:month/:day/:slug/"
            pages = "/:slug/"
            categories = "/cat/:slug/"
            tags = "/tag/:slug/"
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Permalinks.Posts.Should().Be("/:year/:month/:day/:slug/");
        config.Permalinks.Pages.Should().Be("/:slug/");
        config.Permalinks.Categories.Should().Be("/cat/:slug/");
        config.Permalinks.Tags.Should().Be("/tag/:slug/");
    }

    [Fact]
    public async Task LoadAsync_WithTomlMenus_ShouldParseMenus()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        // 使用标准 TOML 表格语法
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "Test"
            
            [menu]
            main = [
              { name = "Home", url = "/", weight = 1 },
              { name = "About", url = "/about/", weight = 2 }
            ]
            footer = [
              { name = "Privacy", url = "/privacy/", weight = 1 }
            ]
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Menus.Menus.Should().ContainKey("main");
        config.Menus.Menus["main"].Should().HaveCount(2);
        config.Menus.Menus["main"][0].Name.Should().Be("Home");
        config.Menus.Menus["main"][0].URL.Should().Be("/");
        config.Menus.Menus["main"][0].Weight.Should().Be(1);
        config.Menus.Menus["main"][1].Name.Should().Be("About");

        config.Menus.Menus.Should().ContainKey("footer");
        config.Menus.Menus["footer"].Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadAsync_WithTomlMarkup_ShouldParseMarkupConfig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "Test"
            
            [markup.tableOfContents]
            startLevel = 1
            endLevel = 4
            ordered = true
            
            [markup.highlight]
            style = "dracula"
            lineNos = true
            lineNumbersInTable = false
            tabWidth = 2
            
            [markup.goldmark.renderer]
            unsafe = true
            
            [markup.goldmark.extensions]
            table = true
            strikethrough = true
            taskList = true
            footnote = true
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Markup.TableOfContents.StartLevel.Should().Be(1);
        config.Markup.TableOfContents.EndLevel.Should().Be(4);
        config.Markup.TableOfContents.Ordered.Should().BeTrue();

        config.Markup.Highlight.Style.Should().Be("dracula");
        config.Markup.Highlight.LineNos.Should().BeTrue();
        config.Markup.Highlight.LineNumbersInTable.Should().BeFalse();
        config.Markup.Highlight.TabWidth.Should().Be(2);

        config.Markup.Goldmark.Unsafe.Should().BeTrue();
        config.Markup.Goldmark.Extensions.Table.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_WithTomlAuthor_ShouldParseAuthor()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "Test"
            
            [author]
            name = "John Doe"
            email = "john@example.com"
            url = "https://johndoe.com"
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Author.Should().NotBeNull();
        config.Author!.Name.Should().Be("John Doe");
        config.Author.Email.Should().Be("john@example.com");
        config.Author.URL.Should().Be("https://johndoe.com");
    }

    #endregion

    #region YAML 配置加载测试

    [Fact]
    public async Task LoadAsync_WithYamlConfig_ShouldParseAllFields()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.yaml");
        var yamlContent = """
            baseURL: "https://example.com/"
            title: "My YAML Site"
            languageCode: "en-US"
            theme: "modern"
            buildDrafts: true
            buildFuture: false
            paginate: 15
            summaryLength: 80
            copyright: "© 2024 YAML Test"
            """;
        await File.WriteAllTextAsync(configPath, yamlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.BaseURL.Should().Be("https://example.com/");
        config.Title.Should().Be("My YAML Site");
        config.LanguageCode.Should().Be("en-US");
        config.Theme.Should().Be("modern");
        config.BuildDrafts.Should().BeTrue();
        config.BuildFuture.Should().BeFalse();
        config.Paginate.Should().Be(15);
        config.SummaryLength.Should().Be(80);
        config.Copyright.Should().Be("© 2024 YAML Test");
    }

    [Fact]
    public async Task LoadAsync_WithYamlMenus_ShouldParseMenus()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.yaml");
        var yamlContent = """
            baseURL: "https://example.com/"
            title: "Test"
            menu:
              main:
                - name: "Home"
                  url: "/"
                  weight: 1
                - name: "Blog"
                  url: "/blog/"
                  weight: 2
            """;
        await File.WriteAllTextAsync(configPath, yamlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Menus.Menus.Should().ContainKey("main");
        config.Menus.Menus["main"].Should().HaveCount(2);
        config.Menus.Menus["main"][0].Name.Should().Be("Home");
        config.Menus.Menus["main"][1].Name.Should().Be("Blog");
    }

    #endregion

    #region JSON 配置加载测试

    [Fact]
    public async Task LoadAsync_WithJsonConfig_ShouldParseAllFields()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.json");
        var jsonContent = """
            {
              "baseURL": "https://example.com/",
              "title": "My JSON Site",
              "languageCode": "de",
              "theme": "classic",
              "buildDrafts": false,
              "buildFuture": true,
              "paginate": 25,
              "summaryLength": 120,
              "copyright": "© 2024 JSON Test"
            }
            """;
        await File.WriteAllTextAsync(configPath, jsonContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.BaseURL.Should().Be("https://example.com/");
        config.Title.Should().Be("My JSON Site");
        config.LanguageCode.Should().Be("de");
        config.Theme.Should().Be("classic");
        config.BuildDrafts.Should().BeFalse();
        config.BuildFuture.Should().BeTrue();
        config.Paginate.Should().Be(25);
        config.SummaryLength.Should().Be(120);
        config.Copyright.Should().Be("© 2024 JSON Test");
    }

    [Fact]
    public async Task LoadAsync_WithJsonPermalinks_ShouldParsePermalinks()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.json");
        var jsonContent = """
            {
              "baseURL": "https://example.com/",
              "title": "Test",
              "permalinks": {
                "posts": "/blog/:year/:month/:slug/",
                "pages": "/:slug.html"
              }
            }
            """;
        await File.WriteAllTextAsync(configPath, jsonContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Permalinks.Posts.Should().Be("/blog/:year/:month/:slug/");
        config.Permalinks.Pages.Should().Be("/:slug.html");
    }

    #endregion

    #region 自动加载测试

    [Fact]
    public async Task AutoLoadAsync_WithTomlConfig_ShouldFindAndLoad()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        await File.WriteAllTextAsync(configPath, """
            baseURL = "https://auto.example.com/"
            title = "Auto Loaded"
            """);

        // Act
        var config = await _loader.AutoLoadAsync(_tempDir);

        // Assert
        config.BaseURL.Should().Be("https://auto.example.com/");
        config.Title.Should().Be("Auto Loaded");
    }

    [Fact]
    public async Task AutoLoadAsync_WithYamlConfig_ShouldFindAndLoad()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.yaml");
        await File.WriteAllTextAsync(configPath, """
            baseURL: "https://yaml.example.com/"
            title: "YAML Auto Loaded"
            """);

        // Act
        var config = await _loader.AutoLoadAsync(_tempDir);

        // Assert
        config.BaseURL.Should().Be("https://yaml.example.com/");
        config.Title.Should().Be("YAML Auto Loaded");
    }

    [Fact]
    public async Task AutoLoadAsync_WithJsonConfig_ShouldFindAndLoad()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "baseURL": "https://json.example.com/",
              "title": "JSON Auto Loaded"
            }
            """);

        // Act
        var config = await _loader.AutoLoadAsync(_tempDir);

        // Assert
        config.BaseURL.Should().Be("https://json.example.com/");
        config.Title.Should().Be("JSON Auto Loaded");
    }

    [Fact]
    public async Task AutoLoadAsync_WithMultipleConfigs_ShouldPreferToml()
    {
        // Arrange - 创建多个配置文件，TOML 应该优先
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Flint.toml"), """
            baseURL = "https://toml.example.com/"
            title = "TOML Priority"
            """);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Flint.yaml"), """
            baseURL: "https://yaml.example.com/"
            title: "YAML"
            """);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Flint.json"), """
            {
              "baseURL": "https://json.example.com/",
              "title": "JSON"
            }
            """);

        // Act
        var config = await _loader.AutoLoadAsync(_tempDir);

        // Assert
        config.BaseURL.Should().Be("https://toml.example.com/");
        config.Title.Should().Be("TOML Priority");
    }

    [Fact]
    public async Task AutoLoadAsync_WithNoConfig_ShouldThrowFileNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _loader.AutoLoadAsync(_tempDir).AsTask());
    }

    #endregion

    #region 保存测试

    [Fact]
    public async Task SaveAsync_WithTomlFormat_ShouldSaveCorrectly()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://save.example.com/",
            Title = "Saved Site",
            LanguageCode = "fr",
            Theme = "saved-theme",
            BuildDrafts = true,
            Paginate = 30
        };
        var configPath = Path.Combine(_tempDir, "saved.toml");

        // Act
        await _loader.SaveAsync(config, configPath, ConfigFormat.Toml);

        // Assert
        File.Exists(configPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(configPath);
        content.Should().Contain("baseURL = \"https://save.example.com/\"");
        content.Should().Contain("title = \"Saved Site\"");
        content.Should().Contain("languageCode = \"fr\"");
        content.Should().Contain("theme = \"saved-theme\"");
        content.Should().Contain("buildDrafts = true");
        content.Should().Contain("paginate = 30");
    }

    [Fact]
    public async Task SaveAsync_WithYamlFormat_ShouldSaveCorrectly()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://yaml-save.example.com/",
            Title = "YAML Saved",
            LanguageCode = "es"
        };
        var configPath = Path.Combine(_tempDir, "saved.yaml");

        // Act
        await _loader.SaveAsync(config, configPath, ConfigFormat.Yaml);

        // Assert
        File.Exists(configPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(configPath);
        content.Should().Contain("baseURL");
        content.Should().Contain("https://yaml-save.example.com/");
        content.Should().Contain("title");
        content.Should().Contain("YAML Saved");
    }

    [Fact]
    public async Task SaveAsync_WithJsonFormat_ShouldSaveCorrectly()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://json-save.example.com/",
            Title = "JSON Saved",
            LanguageCode = "it"
        };
        var configPath = Path.Combine(_tempDir, "saved.json");

        // Act
        await _loader.SaveAsync(config, configPath, ConfigFormat.Json);

        // Assert
        File.Exists(configPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(configPath);
        content.Should().Contain("\"baseURL\"");
        content.Should().Contain("https://json-save.example.com/");
        content.Should().Contain("\"title\"");
        content.Should().Contain("JSON Saved");
    }

    [Fact]
    public async Task SaveAsync_WithNestedDirectory_ShouldCreateDirectory()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://nested.example.com/",
            Title = "Nested"
        };
        var configPath = Path.Combine(_tempDir, "nested", "deep", "config.toml");

        // Act
        await _loader.SaveAsync(config, configPath, ConfigFormat.Toml);

        // Assert
        File.Exists(configPath).Should().BeTrue();
    }

    #endregion

    #region 错误处理测试

    [Fact]
    public async Task LoadAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "nonexistent.toml");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public void Load_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "nonexistent.toml");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => _loader.Load(configPath));
    }

    [Fact]
    public async Task LoadAsync_WithInvalidToml_ShouldThrowException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.toml");
        await File.WriteAllTextAsync(configPath, """
            baseURL = "unclosed string
            title = missing quote
            """);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public async Task LoadAsync_WithInvalidJson_ShouldThrowException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "baseURL": "https://example.com/",
              "title": "Missing closing brace"
            """);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => _loader.LoadAsync(configPath).AsTask());
    }

    #endregion

    #region 默认值测试

    [Fact]
    public async Task LoadAsync_WithMinimalConfig_ShouldUseDefaults()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "minimal.toml");
        await File.WriteAllTextAsync(configPath, """
            baseURL = "https://minimal.example.com/"
            title = "Minimal"
            """);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.LanguageCode.Should().Be("en");
        config.Theme.Should().BeEmpty();
        config.BuildDrafts.Should().BeFalse();
        config.BuildFuture.Should().BeFalse();
        config.BuildExpired.Should().BeFalse();
        config.Paginate.Should().Be(10);
        config.PaginatePath.Should().Be("page");
        config.EnableGitInfo.Should().BeFalse();
        config.SummaryLength.Should().Be(70);
        config.ContentDir.Should().Be("content");
        config.LayoutDir.Should().Be("layouts");
        config.StaticDir.Should().Be("static");
        config.AssetDir.Should().Be("assets");
        config.DataDir.Should().Be("data");
        config.PublishDir.Should().Be("public");
        config.ArchetypeDir.Should().Be("archetypes");
    }

    [Fact]
    public void GetDefaultConfig_ShouldReturnValidDefaults()
    {
        // Act
        var config = ConfigLoader.GetDefaultConfig("https://default.example.com/", "Default Site");

        // Assert
        config.BaseURL.Should().Be("https://default.example.com/");
        config.Title.Should().Be("Default Site");
        config.LanguageCode.Should().Be("en");
        config.Paginate.Should().Be(10);
        config.SummaryLength.Should().Be(70);
    }

    #endregion

    #region 辅助方法测试

    [Fact]
    public void FindConfigFile_WithExistingConfig_ShouldReturnPath()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "Flint.toml"), "baseURL = \"test\"");

        // Act
        var result = ConfigLoader.FindConfigFile(_tempDir);

        // Assert
        result.Should().NotBeNull();
        result.Should().EndWith("Flint.toml");
    }

    [Fact]
    public void FindConfigFile_WithNoConfig_ShouldReturnNull()
    {
        // Act
        var result = ConfigLoader.FindConfigFile(_tempDir);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void HasConfigFile_WithExistingConfig_ShouldReturnTrue()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "Flint.yaml"), "baseURL: test");

        // Act
        var result = ConfigLoader.HasConfigFile(_tempDir);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasConfigFile_WithNoConfig_ShouldReturnFalse()
    {
        // Act
        var result = ConfigLoader.HasConfigFile(_tempDir);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region 多语言配置测试

    [Fact]
    public async Task LoadAsync_WithMultiLanguageConfig_ShouldParseLanguages()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "Multilingual Site"
            
            [languages.en]
            languageName = "English"
            weight = 1
            title = "English Site"
            
            [languages.zh]
            languageName = "中文"
            weight = 2
            title = "中文站点"
            contentDir = "content/zh"
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Languages.Should().ContainKey("en");
        config.Languages.Should().ContainKey("zh");
        config.Languages["en"].LanguageName.Should().Be("English");
        config.Languages["en"].Weight.Should().Be(1);
        config.Languages["zh"].LanguageName.Should().Be("中文");
        config.Languages["zh"].ContentDir.Should().Be("content/zh");
    }

    #endregion

    #region 模块配置测试

    [Fact]
    public async Task LoadAsync_WithModuleConfig_ShouldParseModules()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        // 使用标准 TOML 语法
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "Module Site"
            
            [module]
            imports = [
              { path = "github.com/example/theme", disabled = false, mounts = [
                { source = "layouts", target = "layouts" },
                { source = "assets", target = "assets" }
              ]}
            ]
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Module.Imports.Should().HaveCount(1);
        config.Module.Imports[0].Path.Should().Be("github.com/example/theme");
        config.Module.Imports[0].Disabled.Should().BeFalse();
        config.Module.Imports[0].Mounts.Should().HaveCount(2);
    }

    #endregion

    #region 安全配置测试

    [Fact]
    public async Task LoadAsync_WithSecurityConfig_ShouldParseSecurity()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "Secure Site"
            
            [security]
            allowedDomains = ["example.com", "cdn.example.com"]
            allowedCommands = ["npm", "node"]
            httpTimeout = 60
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Security.AllowedDomains.Should().Contain("example.com");
        config.Security.AllowedDomains.Should().Contain("cdn.example.com");
        config.Security.AllowedCommands.Should().Contain("npm");
        config.Security.HttpTimeout.Should().Be(60);
    }

    #endregion

    #region 缓存配置测试

    [Fact]
    public async Task LoadAsync_WithCacheConfig_ShouldParseCaches()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "Flint.toml");
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "Cached Site"
            
            [caches]
            enabled = true
            dir = ".cache"
            maxSize = 200
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Caches.Enabled.Should().BeTrue();
        config.Caches.Dir.Should().Be(".cache");
        config.Caches.MaxSize.Should().Be(200);
    }

    #endregion
}

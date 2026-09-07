// Flint 静态站点生成器
// 配置边界条件测试
// 测试空配置文件、超大配置文件、特殊字符配置值、深层嵌套配置
// _Requirements: 4.1, 4.2, 4.3_

using System.Text;
using Flint.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Configuration;

/// <summary>
/// 配置边界条件测试
/// 验证配置系统对边界条件输入的处理
/// </summary>
public class ConfigBoundaryConditionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigLoader _loader;

    public ConfigBoundaryConditionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-config-boundary-test-{Guid.NewGuid():N}");
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

    #region 空配置文件测试

    [Fact]
    public async Task LoadAsync_WithEmptyTomlFile_ShouldReturnDefaultConfig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "empty.toml");
        await File.WriteAllTextAsync(configPath, "");

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        // 空配置应该使用默认值
        config.Paginate.Should().Be(10);
        config.SummaryLength.Should().Be(70);
        config.LanguageCode.Should().Be("en");
    }

    [Fact]
    public async Task LoadAsync_WithEmptyYamlFile_ShouldReturnDefaultConfig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "empty.yaml");
        await File.WriteAllTextAsync(configPath, "");

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Paginate.Should().Be(10);
        config.SummaryLength.Should().Be(70);
    }

    [Fact]
    public async Task LoadAsync_WithEmptyJsonObject_ShouldReturnDefaultConfig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "empty.json");
        await File.WriteAllTextAsync(configPath, "{}");

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Paginate.Should().Be(10);
        config.SummaryLength.Should().Be(70);
    }

    [Fact]
    public async Task LoadAsync_WithWhitespaceOnlyToml_ShouldReturnDefaultConfig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "whitespace.toml");
        await File.WriteAllTextAsync(configPath, "   \n\t\n   ");

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadAsync_WithCommentsOnlyToml_ShouldReturnDefaultConfig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "comments.toml");
        await File.WriteAllTextAsync(configPath, "# 这是注释\n# 另一行注释\n");

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadAsync_WithCommentsOnlyYaml_ShouldReturnDefaultConfig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "comments.yaml");
        await File.WriteAllTextAsync(configPath, "# 这是注释\n# 另一行注释\n");

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
    }

    #endregion

    #region 超大配置文件测试

    [Fact]
    public async Task LoadAsync_WithLargeTomlFile_ShouldLoadSuccessfully()
    {
        // Arrange - 创建包含大量配置项的 TOML 文件
        var configPath = Path.Combine(_tempDir, "large.toml");
        var sb = new StringBuilder();
        sb.AppendLine("baseURL = \"https://example.com/\"");
        sb.AppendLine("title = \"Large Config Test\"");
        sb.AppendLine();

        // 添加大量参数
        sb.AppendLine("[params]");
        for (int i = 0; i < 1000; i++)
        {
            sb.AppendLine($"param{i} = \"value{i}\"");
        }

        await File.WriteAllTextAsync(configPath, sb.ToString());

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.BaseURL.Should().Be("https://example.com/");
        config.Title.Should().Be("Large Config Test");
        config.Params.Should().HaveCount(1000);
    }

    [Fact]
    public async Task LoadAsync_WithLargeJsonFile_ShouldLoadSuccessfully()
    {
        // Arrange - 创建包含大量配置项的 JSON 文件
        var configPath = Path.Combine(_tempDir, "large.json");
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"baseURL\": \"https://example.com/\",");
        sb.AppendLine("  \"title\": \"Large Config Test\",");
        sb.AppendLine("  \"params\": {");

        for (int i = 0; i < 999; i++)
        {
            sb.AppendLine($"    \"param{i}\": \"value{i}\",");
        }
        sb.AppendLine("    \"param999\": \"value999\"");

        sb.AppendLine("  }");
        sb.AppendLine("}");

        await File.WriteAllTextAsync(configPath, sb.ToString());

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.BaseURL.Should().Be("https://example.com/");
        config.Params.Should().HaveCount(1000);
    }

    [Fact]
    public async Task LoadAsync_WithLargeYamlFile_ShouldLoadSuccessfully()
    {
        // Arrange - 创建包含大量配置项的 YAML 文件
        var configPath = Path.Combine(_tempDir, "large.yaml");
        var sb = new StringBuilder();
        sb.AppendLine("baseURL: \"https://example.com/\"");
        sb.AppendLine("title: \"Large Config Test\"");
        sb.AppendLine("params:");

        for (int i = 0; i < 1000; i++)
        {
            sb.AppendLine($"  param{i}: \"value{i}\"");
        }

        await File.WriteAllTextAsync(configPath, sb.ToString());

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.BaseURL.Should().Be("https://example.com/");
        config.Params.Should().HaveCount(1000);
    }

    [Fact]
    public async Task LoadAsync_WithManyMenuItems_ShouldLoadSuccessfully()
    {
        // Arrange - 创建包含大量菜单项的配置
        var configPath = Path.Combine(_tempDir, "many-menus.toml");
        var sb = new StringBuilder();
        sb.AppendLine("baseURL = \"https://example.com/\"");
        sb.AppendLine("title = \"Menu Test\"");
        sb.AppendLine();
        sb.AppendLine("[menu]");
        sb.Append("main = [");

        for (int i = 0; i < 100; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append($"{{ name = \"Item{i}\", url = \"/item{i}/\", weight = {i} }}");
        }
        sb.AppendLine("]");

        await File.WriteAllTextAsync(configPath, sb.ToString());

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Menus.Menus.Should().ContainKey("main");
        config.Menus.Menus["main"].Should().HaveCount(100);
    }

    [Fact]
    public async Task LoadAsync_WithManyLanguages_ShouldLoadSuccessfully()
    {
        // Arrange - 创建包含多种语言的配置
        var configPath = Path.Combine(_tempDir, "many-languages.toml");
        var sb = new StringBuilder();
        sb.AppendLine("baseURL = \"https://example.com/\"");
        sb.AppendLine("title = \"Multilingual Test\"");
        sb.AppendLine();

        // 添加多种语言配置
        string[] languages = ["en", "zh", "ja", "ko", "fr", "de", "es", "pt", "ru", "ar"];
        foreach (var lang in languages)
        {
            sb.AppendLine($"[languages.{lang}]");
            sb.AppendLine($"languageName = \"{lang.ToUpperInvariant()}\"");
            sb.AppendLine($"weight = {Array.IndexOf(languages, lang) + 1}");
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(configPath, sb.ToString());

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Languages.Should().HaveCount(10);
        config.Languages.Should().ContainKey("en");
        config.Languages.Should().ContainKey("zh");
    }

    #endregion

    #region 特殊字符配置值测试

    [Theory]
    [InlineData("Hello 世界")]
    [InlineData("Привет мир")]
    [InlineData("مرحبا بالعالم")]
    [InlineData("שלום עולם")]
    [InlineData("🎉🚀💻")]
    public async Task LoadAsync_WithUnicodeTitle_ShouldPreserveCharacters(string unicodeTitle)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "unicode.toml");
        var content = $"""
            baseURL = "https://example.com/"
            title = "{unicodeTitle}"
            """;
        await File.WriteAllTextAsync(configPath, content, Encoding.UTF8);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Title.Should().Be(unicodeTitle);
    }

    [Theory]
    [InlineData("Hello 世界")]
    [InlineData("Emoji 🎉🚀💻")]
    public async Task LoadAsync_WithUnicodeInYaml_ShouldPreserveCharacters(string unicodeValue)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "unicode.yaml");
        var content = $"""
            baseURL: "https://example.com/"
            title: "{unicodeValue}"
            """;
        await File.WriteAllTextAsync(configPath, content, Encoding.UTF8);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Title.Should().Be(unicodeValue);
    }

    [Fact]
    public async Task LoadAsync_WithSpecialCharactersInUrl_ShouldPreserve()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "special-url.toml");
        var content = """
            baseURL = "https://example.com/path?query=value&other=123#anchor"
            title = "Special URL Test"
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.BaseURL.Should().Be("https://example.com/path?query=value&other=123#anchor");
    }

    [Fact]
    public async Task LoadAsync_WithEscapedCharactersInToml_ShouldUnescape()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "escaped.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Line1\nLine2\tTabbed"
            copyright = "Copyright \u00A9 2024"
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Title.Should().Contain("\n");
        config.Title.Should().Contain("\t");
        config.Copyright.Should().Contain("©");
    }

    [Fact]
    public async Task LoadAsync_WithQuotesInValue_ShouldPreserve()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "quotes.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "He said \"Hello World\""
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Title.Should().Contain("\"Hello World\"");
    }

    [Fact]
    public async Task LoadAsync_WithBackslashesInPath_ShouldPreserve()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "backslash.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Test"
            contentDir = "content\\posts"
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.ContentDir.Should().Contain("\\");
    }

    [Fact]
    public async Task LoadAsync_WithVeryLongString_ShouldPreserve()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "long-string.toml");
        var longTitle = new string('A', 10000);
        var content = $"""
            baseURL = "https://example.com/"
            title = "{longTitle}"
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Title.Should().HaveLength(10000);
        config.Title.Should().Be(longTitle);
    }

    [Fact]
    public async Task LoadAsync_WithMultilineStringInToml_ShouldPreserve()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "multiline.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Test"
            copyright = '''
            Line 1
            Line 2
            Line 3
            '''
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Copyright.Should().Contain("Line 1");
        config.Copyright.Should().Contain("Line 2");
        config.Copyright.Should().Contain("Line 3");
    }

    [Fact]
    public async Task LoadAsync_WithEmptyStringValues_ShouldPreserve()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "empty-strings.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Test"
            theme = ""
            copyright = ""
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Theme.Should().BeEmpty();
        config.Copyright.Should().BeEmpty();
    }

    #endregion

    #region 深层嵌套配置测试

    [Fact]
    public async Task LoadAsync_WithDeeplyNestedParams_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "nested-params.json");
        var content = """
            {
              "baseURL": "https://example.com/",
              "title": "Nested Test",
              "params": {
                "level1": {
                  "level2": {
                    "level3": {
                      "level4": {
                        "level5": {
                          "value": "deep value"
                        }
                      }
                    }
                  }
                }
              }
            }
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Params.Should().ContainKey("level1");
    }

    [Fact]
    public async Task LoadAsync_WithNestedMenuStructure_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "nested-menu.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Nested Menu Test"
            
            [menu]
            main = [
              { name = "Parent1", url = "/parent1/", weight = 1, identifier = "parent1" },
              { name = "Child1", url = "/parent1/child1/", weight = 1, parent = "parent1" },
              { name = "Child2", url = "/parent1/child2/", weight = 2, parent = "parent1" },
              { name = "Parent2", url = "/parent2/", weight = 2, identifier = "parent2" },
              { name = "Child3", url = "/parent2/child3/", weight = 1, parent = "parent2" }
            ]
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Menus.Menus["main"].Should().HaveCount(5);
        config.Menus.Menus["main"].Should().Contain(m => m.Parent == "parent1");
        config.Menus.Menus["main"].Should().Contain(m => m.Parent == "parent2");
    }

    [Fact]
    public async Task LoadAsync_WithNestedMarkupConfig_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "nested-markup.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Nested Markup Test"
            
            [markup]
            [markup.tableOfContents]
            startLevel = 1
            endLevel = 4
            ordered = true
            
            [markup.highlight]
            style = "monokai"
            lineNos = true
            lineNumbersInTable = false
            tabWidth = 2
            
            [markup.goldmark]
            [markup.goldmark.renderer]
            unsafe = true
            
            [markup.goldmark.extensions]
            table = true
            strikethrough = true
            taskList = true
            footnote = true
            definitionList = true
            typographer = true
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Markup.TableOfContents.StartLevel.Should().Be(1);
        config.Markup.TableOfContents.EndLevel.Should().Be(4);
        config.Markup.Highlight.Style.Should().Be("monokai");
        config.Markup.Goldmark.Unsafe.Should().BeTrue();
        config.Markup.Goldmark.Extensions.Table.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_WithNestedLanguageParams_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "nested-lang.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Nested Language Test"
            
            [languages.en]
            languageName = "English"
            weight = 1
            [languages.en.params]
            description = "English description"
            author = "John Doe"
            
            [languages.zh]
            languageName = "中文"
            weight = 2
            [languages.zh.params]
            description = "中文描述"
            author = "张三"
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Languages["en"].Params.Should().ContainKey("description");
        config.Languages["zh"].Params.Should().ContainKey("description");
    }

    [Fact]
    public async Task LoadAsync_WithNestedModuleConfig_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "nested-module.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Nested Module Test"
            
            [module]
            imports = [
              { path = "github.com/example/theme1", disabled = false, mounts = [
                { source = "layouts", target = "layouts" },
                { source = "assets", target = "assets" },
                { source = "static", target = "static" }
              ]},
              { path = "github.com/example/theme2", disabled = false, mounts = [
                { source = "layouts", target = "layouts/theme2" }
              ]}
            ]
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Module.Imports.Should().HaveCount(2);
        config.Module.Imports[0].Mounts.Should().HaveCount(3);
        config.Module.Imports[1].Mounts.Should().HaveCount(1);
    }

    #endregion

    #region 边界数值测试

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task LoadAsync_WithBoundaryPaginateValues_ShouldLoadSuccessfully(int paginate)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, $"paginate-{paginate}.toml");
        var content = $"""
            baseURL = "https://example.com/"
            title = "Paginate Test"
            paginate = {paginate}
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Paginate.Should().Be(paginate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task LoadAsync_WithBoundarySummaryLength_ShouldLoadSuccessfully(int summaryLength)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, $"summary-{summaryLength}.toml");
        var content = $"""
            baseURL = "https://example.com/"
            title = "Summary Test"
            summaryLength = {summaryLength}
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.SummaryLength.Should().Be(summaryLength);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    public async Task LoadAsync_WithBoundaryHttpTimeout_ShouldLoadSuccessfully(int timeout)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, $"timeout-{timeout}.toml");
        var content = $"""
            baseURL = "https://example.com/"
            title = "Timeout Test"
            
            [security]
            httpTimeout = {timeout}
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Security.HttpTimeout.Should().Be(timeout);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public async Task LoadAsync_WithBoundaryTocLevels_ShouldLoadSuccessfully(int level)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, $"toc-{level}.toml");
        var content = $"""
            baseURL = "https://example.com/"
            title = "TOC Test"
            
            [markup.tableOfContents]
            startLevel = {level}
            endLevel = {level}
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Markup.TableOfContents.StartLevel.Should().Be(level);
        config.Markup.TableOfContents.EndLevel.Should().Be(level);
    }

    #endregion

    #region 特殊路径测试

    [Theory]
    [InlineData("content")]
    [InlineData("my-content")]
    [InlineData("content_files")]
    [InlineData("Content")]
    [InlineData("CONTENT")]
    public async Task LoadAsync_WithVariousContentDirNames_ShouldLoadSuccessfully(string contentDir)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "content-dir.toml");
        var content = $"""
            baseURL = "https://example.com/"
            title = "Content Dir Test"
            contentDir = "{contentDir}"
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.ContentDir.Should().Be(contentDir);
    }

    [Fact]
    public async Task LoadAsync_WithNestedDirectoryPath_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "nested-dir.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Nested Dir Test"
            contentDir = "src/content/posts"
            layoutDir = "src/layouts/templates"
            staticDir = "src/static/files"
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.ContentDir.Should().Be("src/content/posts");
        config.LayoutDir.Should().Be("src/layouts/templates");
        config.StaticDir.Should().Be("src/static/files");
    }

    #endregion

    #region 布尔值边界测试

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoadAsync_WithBooleanValues_ShouldLoadCorrectly(bool value)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, $"bool-{value}.toml");
        var boolStr = value.ToString().ToLowerInvariant();
        var content = $"""
            baseURL = "https://example.com/"
            title = "Boolean Test"
            buildDrafts = {boolStr}
            buildFuture = {boolStr}
            buildExpired = {boolStr}
            enableGitInfo = {boolStr}
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.BuildDrafts.Should().Be(value);
        config.BuildFuture.Should().Be(value);
        config.BuildExpired.Should().Be(value);
        config.EnableGitInfo.Should().Be(value);
    }

    [Fact]
    public async Task LoadAsync_WithMixedBooleanValues_ShouldLoadCorrectly()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "mixed-bool.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Mixed Boolean Test"
            buildDrafts = true
            buildFuture = false
            buildExpired = true
            enableGitInfo = false
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.BuildDrafts.Should().BeTrue();
        config.BuildFuture.Should().BeFalse();
        config.BuildExpired.Should().BeTrue();
        config.EnableGitInfo.Should().BeFalse();
    }

    #endregion

    #region 数组边界测试

    [Fact]
    public async Task LoadAsync_WithEmptyArrays_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "empty-arrays.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Empty Arrays Test"
            
            [security]
            allowedDomains = []
            allowedCommands = []
            
            [outputs]
            home = []
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Security.AllowedDomains.Should().BeEmpty();
        config.Security.AllowedCommands.Should().BeEmpty();
        config.Outputs.Home.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_WithSingleElementArrays_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "single-arrays.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Single Element Arrays Test"
            
            [security]
            allowedDomains = ["example.com"]
            allowedCommands = ["npm"]
            
            [outputs]
            home = ["HTML"]
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Security.AllowedDomains.Should().HaveCount(1);
        config.Security.AllowedDomains.Should().Contain("example.com");
        config.Security.AllowedCommands.Should().HaveCount(1);
        config.Outputs.Home.Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadAsync_WithLargeArrays_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "large-arrays.toml");
        var sb = new StringBuilder();
        sb.AppendLine("baseURL = \"https://example.com/\"");
        sb.AppendLine("title = \"Large Arrays Test\"");
        sb.AppendLine();
        sb.AppendLine("[security]");
        sb.Append("allowedDomains = [");
        for (int i = 0; i < 100; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append($"\"domain{i}.com\"");
        }
        sb.AppendLine("]");

        await File.WriteAllTextAsync(configPath, sb.ToString());

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Security.AllowedDomains.Should().HaveCount(100);
    }

    #endregion

    #region 格式兼容性边界测试

    [Fact]
    public async Task LoadAsync_WithTrailingNewlines_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "trailing-newlines.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "Trailing Newlines Test"
            
            
            
            """;
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Title.Should().Be("Trailing Newlines Test");
    }

    [Fact]
    public async Task LoadAsync_WithMixedLineEndings_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "mixed-endings.toml");
        var content = "baseURL = \"https://example.com/\"\r\ntitle = \"Mixed Endings Test\"\n";
        await File.WriteAllTextAsync(configPath, content);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Title.Should().Be("Mixed Endings Test");
    }

    [Fact]
    public async Task LoadAsync_WithBomMarker_ShouldLoadSuccessfully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "bom.toml");
        var content = """
            baseURL = "https://example.com/"
            title = "BOM Test"
            """;
        // 使用带 BOM 的 UTF-8 编码
        await File.WriteAllTextAsync(configPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config.Title.Should().Be("BOM Test");
    }

    #endregion
}

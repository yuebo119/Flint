// Flint 静态站点生成器
// 配置系统属性测试
// 验证配置格式无关性和往返一致性

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.Configuration;

/// <summary>
/// 配置系统属性测试
/// 验证配置格式无关性（Property 10）和往返一致性（Property 11）
/// </summary>
public class ConfigPropertyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigLoader _loader;

    public ConfigPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-config-prop-test-{Guid.NewGuid():N}");
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

    #region Property 10: 配置格式无关性

    /// <summary>
    /// Property 10: 配置格式无关性
    /// 生成随机配置数据，验证三种格式产生等价结果
    /// **Feature: Flint-integration-tests, Property 10: 配置格式无关性**
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConfigPropertyArbitraries) })]
    public bool ConfigFormatIndependence_ShouldProduceEquivalentResults(SiteConfig originalConfig)
    {
        // 将配置序列化为三种格式
        var tomlContent = ConfigParser.Serialize(originalConfig, ConfigFormat.Toml);
        var yamlContent = ConfigParser.Serialize(originalConfig, ConfigFormat.Yaml);
        var jsonContent = ConfigParser.Serialize(originalConfig, ConfigFormat.Json);

        // 从三种格式解析回配置
        var fromToml = ConfigParser.Parse(tomlContent, ConfigFormat.Toml);
        var fromYaml = ConfigParser.Parse(yamlContent, ConfigFormat.Yaml);
        var fromJson = ConfigParser.Parse(jsonContent, ConfigFormat.Json);

        // 验证三种格式产生等价的配置
        // 比较核心字段
        var tomlEqualsYaml = AreConfigsEquivalent(fromToml, fromYaml);
        var yamlEqualsJson = AreConfigsEquivalent(fromYaml, fromJson);
        var tomlEqualsJson = AreConfigsEquivalent(fromToml, fromJson);

        return tomlEqualsYaml && yamlEqualsJson && tomlEqualsJson;
    }

    /// <summary>
    /// Property 10 的显式测试版本（用于调试）
    /// </summary>
    [Fact]
    public void ConfigFormatIndependence_ExplicitTest()
    {
        // Arrange - 创建一个完整的配置
        var originalConfig = new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "zh-CN",
            Theme = "minimal",
            BuildDrafts = true,
            BuildFuture = false,
            BuildExpired = false,
            Paginate = 15,
            PaginatePath = "pages",
            EnableGitInfo = true,
            SummaryLength = 100,
            Copyright = "© 2024 Test"
        };

        // Act - 序列化为三种格式
        var tomlContent = ConfigParser.Serialize(originalConfig, ConfigFormat.Toml);
        var yamlContent = ConfigParser.Serialize(originalConfig, ConfigFormat.Yaml);
        var jsonContent = ConfigParser.Serialize(originalConfig, ConfigFormat.Json);

        // 从三种格式解析回配置
        var fromToml = ConfigParser.Parse(tomlContent, ConfigFormat.Toml);
        var fromYaml = ConfigParser.Parse(yamlContent, ConfigFormat.Yaml);
        var fromJson = ConfigParser.Parse(jsonContent, ConfigFormat.Json);

        // Assert - 验证三种格式产生等价的配置
        AssertConfigsEquivalent(fromToml, fromYaml, "TOML vs YAML");
        AssertConfigsEquivalent(fromYaml, fromJson, "YAML vs JSON");
        AssertConfigsEquivalent(fromToml, fromJson, "TOML vs JSON");
    }

    /// <summary>
    /// 测试配置格式无关性 - 使用文件加载
    /// </summary>
    [Fact]
    public async Task ConfigFormatIndependence_WithFileLoading_ShouldProduceEquivalentResults()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://test.example.com/",
            Title = "File Loading Test",
            LanguageCode = "en",
            Theme = "default",
            BuildDrafts = true,
            Paginate = 20
        };

        var tomlPath = Path.Combine(_tempDir, "config.toml");
        var yamlPath = Path.Combine(_tempDir, "config.yaml");
        var jsonPath = Path.Combine(_tempDir, "config.json");

        // Act - 保存为三种格式
        await _loader.SaveAsync(config, tomlPath, ConfigFormat.Toml);
        await _loader.SaveAsync(config, yamlPath, ConfigFormat.Yaml);
        await _loader.SaveAsync(config, jsonPath, ConfigFormat.Json);

        // 从文件加载
        var fromToml = await _loader.LoadAsync(tomlPath);
        var fromYaml = await _loader.LoadAsync(yamlPath);
        var fromJson = await _loader.LoadAsync(jsonPath);

        // Assert
        AssertConfigsEquivalent(fromToml, fromYaml, "TOML vs YAML (file)");
        AssertConfigsEquivalent(fromYaml, fromJson, "YAML vs JSON (file)");
        AssertConfigsEquivalent(fromToml, fromJson, "TOML vs JSON (file)");
    }

    /// <summary>
    /// 测试配置格式无关性 - 包含特殊字符
    /// </summary>
    [Fact]
    public void ConfigFormatIndependence_WithSpecialCharacters_ShouldProduceEquivalentResults()
    {
        // Arrange - 包含特殊字符的配置
        var config = new SiteConfig
        {
            BaseURL = "https://example.com/path/to/site/",
            Title = "测试站点 - Test Site \"Special\" & <Characters>",
            LanguageCode = "zh-CN",
            Theme = "my-theme",
            Copyright = "© 2024 版权所有 - All Rights Reserved"
        };

        // Act
        var tomlContent = ConfigParser.Serialize(config, ConfigFormat.Toml);
        var yamlContent = ConfigParser.Serialize(config, ConfigFormat.Yaml);
        var jsonContent = ConfigParser.Serialize(config, ConfigFormat.Json);

        var fromToml = ConfigParser.Parse(tomlContent, ConfigFormat.Toml);
        var fromYaml = ConfigParser.Parse(yamlContent, ConfigFormat.Yaml);
        var fromJson = ConfigParser.Parse(jsonContent, ConfigFormat.Json);

        // Assert
        AssertConfigsEquivalent(fromToml, fromYaml, "TOML vs YAML (special chars)");
        AssertConfigsEquivalent(fromYaml, fromJson, "YAML vs JSON (special chars)");
    }

    /// <summary>
    /// 测试配置格式无关性 - 布尔值
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void ConfigFormatIndependence_BooleanValues_ShouldBePreserved(
        bool buildDrafts, bool buildFuture, bool enableGitInfo)
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Boolean Test",
            BuildDrafts = buildDrafts,
            BuildFuture = buildFuture,
            EnableGitInfo = enableGitInfo
        };

        // Act
        var fromToml = ConfigParser.Parse(ConfigParser.Serialize(config, ConfigFormat.Toml), ConfigFormat.Toml);
        var fromYaml = ConfigParser.Parse(ConfigParser.Serialize(config, ConfigFormat.Yaml), ConfigFormat.Yaml);
        var fromJson = ConfigParser.Parse(ConfigParser.Serialize(config, ConfigFormat.Json), ConfigFormat.Json);

        // Assert
        fromToml.BuildDrafts.Should().Be(buildDrafts);
        fromYaml.BuildDrafts.Should().Be(buildDrafts);
        fromJson.BuildDrafts.Should().Be(buildDrafts);

        fromToml.BuildFuture.Should().Be(buildFuture);
        fromYaml.BuildFuture.Should().Be(buildFuture);
        fromJson.BuildFuture.Should().Be(buildFuture);

        fromToml.EnableGitInfo.Should().Be(enableGitInfo);
        fromYaml.EnableGitInfo.Should().Be(enableGitInfo);
        fromJson.EnableGitInfo.Should().Be(enableGitInfo);
    }

    /// <summary>
    /// 测试配置格式无关性 - 数值
    /// </summary>
    [Theory]
    [InlineData(5, 50)]
    [InlineData(10, 70)]
    [InlineData(25, 100)]
    [InlineData(50, 200)]
    public void ConfigFormatIndependence_NumericValues_ShouldBePreserved(int paginate, int summaryLength)
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Numeric Test",
            Paginate = paginate,
            SummaryLength = summaryLength
        };

        // Act
        var fromToml = ConfigParser.Parse(ConfigParser.Serialize(config, ConfigFormat.Toml), ConfigFormat.Toml);
        var fromYaml = ConfigParser.Parse(ConfigParser.Serialize(config, ConfigFormat.Yaml), ConfigFormat.Yaml);
        var fromJson = ConfigParser.Parse(ConfigParser.Serialize(config, ConfigFormat.Json), ConfigFormat.Json);

        // Assert
        fromToml.Paginate.Should().Be(paginate);
        fromYaml.Paginate.Should().Be(paginate);
        fromJson.Paginate.Should().Be(paginate);

        fromToml.SummaryLength.Should().Be(summaryLength);
        fromYaml.SummaryLength.Should().Be(summaryLength);
        fromJson.SummaryLength.Should().Be(summaryLength);
    }

    #endregion

    #region Property 11: 配置往返一致性

    /// <summary>
    /// Property 11: 配置往返一致性 - TOML 格式
    /// 生成随机配置，保存后重新加载，验证等价性
    /// **Feature: Flint-integration-tests, Property 11: 配置往返一致性**
    /// **Validates: Requirements 4.9, 4.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConfigPropertyArbitraries) })]
    public bool ConfigRoundTrip_Toml_ShouldPreserveData(SiteConfig originalConfig)
    {
        // 序列化为 TOML
        var tomlContent = ConfigParser.Serialize(originalConfig, ConfigFormat.Toml);

        // 从 TOML 解析回配置
        var loadedConfig = ConfigParser.Parse(tomlContent, ConfigFormat.Toml);

        // 验证往返一致性
        return AreConfigsEquivalent(originalConfig, loadedConfig);
    }

    /// <summary>
    /// Property 11: 配置往返一致性 - YAML 格式
    /// 生成随机配置，保存后重新加载，验证等价性
    /// **Feature: Flint-integration-tests, Property 11: 配置往返一致性**
    /// **Validates: Requirements 4.9, 4.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConfigPropertyArbitraries) })]
    public bool ConfigRoundTrip_Yaml_ShouldPreserveData(SiteConfig originalConfig)
    {
        // 序列化为 YAML
        var yamlContent = ConfigParser.Serialize(originalConfig, ConfigFormat.Yaml);

        // 从 YAML 解析回配置
        var loadedConfig = ConfigParser.Parse(yamlContent, ConfigFormat.Yaml);

        // 验证往返一致性
        return AreConfigsEquivalent(originalConfig, loadedConfig);
    }

    /// <summary>
    /// Property 11: 配置往返一致性 - JSON 格式
    /// 生成随机配置，保存后重新加载，验证等价性
    /// **Feature: Flint-integration-tests, Property 11: 配置往返一致性**
    /// **Validates: Requirements 4.9, 4.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConfigPropertyArbitraries) })]
    public bool ConfigRoundTrip_Json_ShouldPreserveData(SiteConfig originalConfig)
    {
        // 序列化为 JSON
        var jsonContent = ConfigParser.Serialize(originalConfig, ConfigFormat.Json);

        // 从 JSON 解析回配置
        var loadedConfig = ConfigParser.Parse(jsonContent, ConfigFormat.Json);

        // 验证往返一致性
        return AreConfigsEquivalent(originalConfig, loadedConfig);
    }

    /// <summary>
    /// Property 11 的显式测试版本 - 使用文件系统
    /// </summary>
    [Fact]
    public async Task ConfigRoundTrip_WithFileSystem_ShouldPreserveData()
    {
        // Arrange
        var originalConfig = new SiteConfig
        {
            BaseURL = "https://roundtrip.example.com/",
            Title = "Round Trip Test Site",
            LanguageCode = "zh-CN",
            Theme = "modern",
            BuildDrafts = true,
            BuildFuture = false,
            BuildExpired = true,
            Paginate = 25,
            PaginatePath = "p",
            EnableGitInfo = true,
            SummaryLength = 150,
            Copyright = "© 2024 Round Trip Test"
        };

        // Act & Assert - TOML
        var tomlPath = Path.Combine(_tempDir, "roundtrip.toml");
        await _loader.SaveAsync(originalConfig, tomlPath, ConfigFormat.Toml);
        var fromToml = await _loader.LoadAsync(tomlPath);
        AssertConfigsEquivalent(originalConfig, fromToml, "TOML round-trip");

        // Act & Assert - YAML
        var yamlPath = Path.Combine(_tempDir, "roundtrip.yaml");
        await _loader.SaveAsync(originalConfig, yamlPath, ConfigFormat.Yaml);
        var fromYaml = await _loader.LoadAsync(yamlPath);
        AssertConfigsEquivalent(originalConfig, fromYaml, "YAML round-trip");

        // Act & Assert - JSON
        var jsonPath = Path.Combine(_tempDir, "roundtrip.json");
        await _loader.SaveAsync(originalConfig, jsonPath, ConfigFormat.Json);
        var fromJson = await _loader.LoadAsync(jsonPath);
        AssertConfigsEquivalent(originalConfig, fromJson, "JSON round-trip");
    }

    /// <summary>
    /// 测试配置往返一致性 - 多次往返
    /// </summary>
    [Theory]
    [InlineData(ConfigFormat.Toml)]
    [InlineData(ConfigFormat.Yaml)]
    [InlineData(ConfigFormat.Json)]
    public void ConfigRoundTrip_MultipleRoundTrips_ShouldBeIdempotent(ConfigFormat format)
    {
        // Arrange
        var originalConfig = new SiteConfig
        {
            BaseURL = "https://idempotent.example.com/",
            Title = "Idempotent Test",
            LanguageCode = "en",
            Theme = "default",
            BuildDrafts = true,
            Paginate = 15
        };

        // Act - 执行多次往返
        var config = originalConfig;
        for (int i = 0; i < 5; i++)
        {
            var serialized = ConfigParser.Serialize(config, format);
            config = ConfigParser.Parse(serialized, format);
        }

        // Assert - 多次往返后应该与原始配置等价
        AssertConfigsEquivalent(originalConfig, config, $"{format} multiple round-trips");
    }

    /// <summary>
    /// 测试配置往返一致性 - 默认值处理
    /// </summary>
    [Theory]
    [InlineData(ConfigFormat.Toml)]
    [InlineData(ConfigFormat.Yaml)]
    [InlineData(ConfigFormat.Json)]
    public void ConfigRoundTrip_DefaultValues_ShouldBePreserved(ConfigFormat format)
    {
        // Arrange - 使用默认值的配置
        var originalConfig = new SiteConfig
        {
            BaseURL = "https://defaults.example.com/",
            Title = "Defaults Test"
            // 其他字段使用默认值
        };

        // Act
        var serialized = ConfigParser.Serialize(originalConfig, format);
        var loadedConfig = ConfigParser.Parse(serialized, format);

        // Assert - 默认值应该被正确保留
        loadedConfig.LanguageCode.Should().Be("en", $"Default LanguageCode ({format})");
        loadedConfig.Theme.Should().Be("", $"Default Theme ({format})");
        loadedConfig.BuildDrafts.Should().BeFalse($"Default BuildDrafts ({format})");
        loadedConfig.BuildFuture.Should().BeFalse($"Default BuildFuture ({format})");
        loadedConfig.BuildExpired.Should().BeFalse($"Default BuildExpired ({format})");
        loadedConfig.Paginate.Should().Be(10, $"Default Paginate ({format})");
        loadedConfig.PaginatePath.Should().Be("page", $"Default PaginatePath ({format})");
        loadedConfig.EnableGitInfo.Should().BeFalse($"Default EnableGitInfo ({format})");
        loadedConfig.SummaryLength.Should().Be(70, $"Default SummaryLength ({format})");
        loadedConfig.ContentDir.Should().Be("content", $"Default ContentDir ({format})");
        loadedConfig.LayoutDir.Should().Be("layouts", $"Default LayoutDir ({format})");
        loadedConfig.StaticDir.Should().Be("static", $"Default StaticDir ({format})");
        loadedConfig.AssetDir.Should().Be("assets", $"Default AssetDir ({format})");
        loadedConfig.DataDir.Should().Be("data", $"Default DataDir ({format})");
        loadedConfig.PublishDir.Should().Be("public", $"Default PublishDir ({format})");
        loadedConfig.ArchetypeDir.Should().Be("archetypes", $"Default ArchetypeDir ({format})");
    }

    /// <summary>
    /// 测试配置往返一致性 - 空字符串和 null 处理
    /// </summary>
    [Theory]
    [InlineData(ConfigFormat.Toml)]
    [InlineData(ConfigFormat.Yaml)]
    [InlineData(ConfigFormat.Json)]
    public void ConfigRoundTrip_EmptyAndNullValues_ShouldBeHandledCorrectly(ConfigFormat format)
    {
        // Arrange - 包含空字符串的配置
        var originalConfig = new SiteConfig
        {
            BaseURL = "https://empty.example.com/",
            Title = "Empty Values Test",
            Theme = "", // 空字符串
            Copyright = null // null 值
        };

        // Act
        var serialized = ConfigParser.Serialize(originalConfig, format);
        var loadedConfig = ConfigParser.Parse(serialized, format);

        // Assert
        loadedConfig.Theme.Should().BeEmpty($"Empty Theme ({format})");
        // Copyright 可能是 null 或空字符串，取决于格式处理
        (loadedConfig.Copyright == null || string.IsNullOrEmpty(loadedConfig.Copyright))
            .Should().BeTrue($"Null/Empty Copyright ({format})");
    }

    /// <summary>
    /// 测试配置往返一致性 - 跨格式往返
    /// </summary>
    [Fact]
    public void ConfigRoundTrip_CrossFormat_ShouldPreserveData()
    {
        // Arrange
        var originalConfig = new SiteConfig
        {
            BaseURL = "https://crossformat.example.com/",
            Title = "Cross Format Test",
            LanguageCode = "ja",
            Theme = "japanese",
            BuildDrafts = true,
            Paginate = 30
        };

        // Act - TOML -> YAML -> JSON -> TOML
        var toml1 = ConfigParser.Serialize(originalConfig, ConfigFormat.Toml);
        var fromToml1 = ConfigParser.Parse(toml1, ConfigFormat.Toml);

        var yaml = ConfigParser.Serialize(fromToml1, ConfigFormat.Yaml);
        var fromYaml = ConfigParser.Parse(yaml, ConfigFormat.Yaml);

        var json = ConfigParser.Serialize(fromYaml, ConfigFormat.Json);
        var fromJson = ConfigParser.Parse(json, ConfigFormat.Json);

        var toml2 = ConfigParser.Serialize(fromJson, ConfigFormat.Toml);
        var finalConfig = ConfigParser.Parse(toml2, ConfigFormat.Toml);

        // Assert - 跨格式往返后应该与原始配置等价
        AssertConfigsEquivalent(originalConfig, finalConfig, "Cross-format round-trip");
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 比较两个配置是否等价
    /// </summary>
    private static bool AreConfigsEquivalent(SiteConfig config1, SiteConfig config2)
    {
        // 比较核心字段
        return config1.BaseURL == config2.BaseURL
            && config1.Title == config2.Title
            && config1.LanguageCode == config2.LanguageCode
            && config1.Theme == config2.Theme
            && config1.BuildDrafts == config2.BuildDrafts
            && config1.BuildFuture == config2.BuildFuture
            && config1.BuildExpired == config2.BuildExpired
            && config1.Paginate == config2.Paginate
            && config1.PaginatePath == config2.PaginatePath
            && config1.EnableGitInfo == config2.EnableGitInfo
            && config1.SummaryLength == config2.SummaryLength
            && config1.Copyright == config2.Copyright
            && config1.ContentDir == config2.ContentDir
            && config1.LayoutDir == config2.LayoutDir
            && config1.StaticDir == config2.StaticDir
            && config1.AssetDir == config2.AssetDir
            && config1.DataDir == config2.DataDir
            && config1.PublishDir == config2.PublishDir
            && config1.ArchetypeDir == config2.ArchetypeDir;
    }

    /// <summary>
    /// 断言两个配置等价
    /// </summary>
    private static void AssertConfigsEquivalent(SiteConfig config1, SiteConfig config2, string context)
    {
        config1.BaseURL.Should().Be(config2.BaseURL, $"BaseURL should match ({context})");
        config1.Title.Should().Be(config2.Title, $"Title should match ({context})");
        config1.LanguageCode.Should().Be(config2.LanguageCode, $"LanguageCode should match ({context})");
        config1.Theme.Should().Be(config2.Theme, $"Theme should match ({context})");
        config1.BuildDrafts.Should().Be(config2.BuildDrafts, $"BuildDrafts should match ({context})");
        config1.BuildFuture.Should().Be(config2.BuildFuture, $"BuildFuture should match ({context})");
        config1.BuildExpired.Should().Be(config2.BuildExpired, $"BuildExpired should match ({context})");
        config1.Paginate.Should().Be(config2.Paginate, $"Paginate should match ({context})");
        config1.PaginatePath.Should().Be(config2.PaginatePath, $"PaginatePath should match ({context})");
        config1.EnableGitInfo.Should().Be(config2.EnableGitInfo, $"EnableGitInfo should match ({context})");
        config1.SummaryLength.Should().Be(config2.SummaryLength, $"SummaryLength should match ({context})");
        config1.Copyright.Should().Be(config2.Copyright, $"Copyright should match ({context})");
        config1.ContentDir.Should().Be(config2.ContentDir, $"ContentDir should match ({context})");
        config1.LayoutDir.Should().Be(config2.LayoutDir, $"LayoutDir should match ({context})");
        config1.StaticDir.Should().Be(config2.StaticDir, $"StaticDir should match ({context})");
        config1.AssetDir.Should().Be(config2.AssetDir, $"AssetDir should match ({context})");
        config1.DataDir.Should().Be(config2.DataDir, $"DataDir should match ({context})");
        config1.PublishDir.Should().Be(config2.PublishDir, $"PublishDir should match ({context})");
        config1.ArchetypeDir.Should().Be(config2.ArchetypeDir, $"ArchetypeDir should match ({context})");
    }

    #endregion
}

/// <summary>
/// 配置属性测试专用的 FsCheck 生成器
/// </summary>
public static class ConfigPropertyArbitraries
{
    /// <summary>
    /// 生成用于属性测试的 SiteConfig
    /// 确保生成的配置可以在三种格式之间正确转换
    /// </summary>
    public static Arbitrary<SiteConfig> SiteConfig() =>
        (from baseUrl in Gen.Elements(
            "https://example.com/",
            "https://www.example.org/",
            "https://blog.example.net/",
            "http://localhost:1313/")
         from title in Gen.Elements(
            "My Site",
            "Blog",
            "Documentation",
            "测试站点",
            "Test Site")
         from languageCode in Gen.Elements("en", "zh", "zh-CN", "ja", "de", "fr")
         from theme in Gen.Elements("", "default", "minimal", "modern")
         from buildDrafts in ArbMap.Default.GeneratorFor<bool>()
         from buildFuture in ArbMap.Default.GeneratorFor<bool>()
         from buildExpired in ArbMap.Default.GeneratorFor<bool>()
         from paginate in Gen.Choose(5, 50)
         from paginatePath in Gen.Elements("page", "pages", "p")
         from enableGitInfo in ArbMap.Default.GeneratorFor<bool>()
         from summaryLength in Gen.Choose(50, 200)
         from copyright in Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("© 2024", "All rights reserved", "CC BY 4.0"))
         select new Flint.Core.Abstractions.SiteConfig
         {
             BaseURL = baseUrl,
             Title = title,
             LanguageCode = languageCode,
             Theme = theme,
             BuildDrafts = buildDrafts,
             BuildFuture = buildFuture,
             BuildExpired = buildExpired,
             Paginate = paginate,
             PaginatePath = paginatePath,
             EnableGitInfo = enableGitInfo,
             SummaryLength = summaryLength,
             Copyright = copyright
         }).ToArbitrary();
}

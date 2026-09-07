// Flint 静态站点生成器
// 配置解析器属性测试

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
// 别名引入：Xunit.Property 与 FsCheck.Xunit.Property 冲突，不能整命名空间 using
using Assert = Xunit.Assert;

namespace Flint.Core.Tests.Configuration;

/// <summary>
/// 配置解析器属性测试
/// **验证: 需求 7.1, 7.2, 7.3, 7.9**
/// </summary>
public class ConfigParserPropertyTests
{
    /// <summary>
    /// **Property 3: 配置文件解析往返一致性**
    /// 对于任意有效的站点配置，解析后再序列化应该产生等价的配置数据
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidConfigArbitrary)])]
    public bool TomlRoundTrip_ShouldPreserveConfig(ValidSiteConfig validConfig)
    {
        ArgumentNullException.ThrowIfNull(validConfig);

        // Arrange
        var config = validConfig.Value;

        // Act - 序列化为 TOML，然后解析回来
        var toml = ConfigParser.SerializeToml(config);
        var parsed = ConfigParser.ParseToml(toml);

        // Assert - 核心属性应该保持一致
        return parsed.BaseURL == config.BaseURL &&
               parsed.Title == config.Title &&
               parsed.LanguageCode == config.LanguageCode &&
               parsed.Theme == config.Theme &&
               parsed.BuildDrafts == config.BuildDrafts &&
               parsed.BuildFuture == config.BuildFuture &&
               parsed.Paginate == config.Paginate &&
               parsed.PaginatePath == config.PaginatePath;
    }

    /// <summary>
    /// **Property 3: 配置文件解析往返一致性 (YAML)**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidConfigArbitrary)])]
    public bool YamlRoundTrip_ShouldPreserveConfig(ValidSiteConfig validConfig)
    {
        ArgumentNullException.ThrowIfNull(validConfig);

        // Arrange
        var config = validConfig.Value;

        // Act - 序列化为 YAML，然后解析回来
        var yaml = ConfigParser.SerializeYaml(config);
        var parsed = ConfigParser.ParseYaml(yaml);

        // Assert - 核心属性应该保持一致
        return parsed.BaseURL == config.BaseURL &&
               parsed.Title == config.Title &&
               parsed.LanguageCode == config.LanguageCode &&
               parsed.Theme == config.Theme &&
               parsed.BuildDrafts == config.BuildDrafts &&
               parsed.BuildFuture == config.BuildFuture &&
               parsed.Paginate == config.Paginate &&
               parsed.PaginatePath == config.PaginatePath;
    }

    /// <summary>
    /// **Property 3: 配置文件解析往返一致性 (JSON)**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidConfigArbitrary)])]
    public bool JsonRoundTrip_ShouldPreserveConfig(ValidSiteConfig validConfig)
    {
        ArgumentNullException.ThrowIfNull(validConfig);

        // Arrange
        var config = validConfig.Value;

        // Act - 序列化为 JSON，然后解析回来
        var json = ConfigParser.SerializeJson(config);
        var parsed = ConfigParser.ParseJson(json);

        // Assert - 核心属性应该保持一致
        return parsed.BaseURL == config.BaseURL &&
               parsed.Title == config.Title &&
               parsed.LanguageCode == config.LanguageCode &&
               parsed.Theme == config.Theme &&
               parsed.BuildDrafts == config.BuildDrafts &&
               parsed.BuildFuture == config.BuildFuture &&
               parsed.Paginate == config.Paginate &&
               parsed.PaginatePath == config.PaginatePath;
    }

    /// <summary>
    /// **Property 3: 跨格式一致性**
    /// 同一配置在不同格式间转换应该保持一致
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(ValidConfigArbitrary)])]
    public bool CrossFormatConsistency_ShouldPreserveConfig(ValidSiteConfig validConfig)
    {
        ArgumentNullException.ThrowIfNull(validConfig);

        // Arrange
        var config = validConfig.Value;

        // Act - TOML -> YAML -> JSON -> 解析
        var toml = ConfigParser.SerializeToml(config);
        var fromToml = ConfigParser.ParseToml(toml);

        var yaml = ConfigParser.SerializeYaml(fromToml);
        var fromYaml = ConfigParser.ParseYaml(yaml);

        var json = ConfigParser.SerializeJson(fromYaml);
        var fromJson = ConfigParser.ParseJson(json);

        // Assert - 最终结果应该与原始配置一致
        return fromJson.BaseURL == config.BaseURL &&
               fromJson.Title == config.Title &&
               fromJson.LanguageCode == config.LanguageCode;
    }

    /// <summary>
    /// 格式检测应该正确识别文件扩展名
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(ValidConfigArbitrary)])]
    public bool DetectFormat_ShouldIdentifyCorrectFormat(ValidConfigFileName fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        // Act
        var format = ConfigParser.DetectFormat(fileName.Value);

        // Assert
        var expectedFormat = fileName.Value switch
        {
            var f when f.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) => ConfigFormat.Toml,
            var f when f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) => ConfigFormat.Yaml,
            var f when f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) => ConfigFormat.Yaml,
            var f when f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) => ConfigFormat.Json,
            _ => throw new InvalidOperationException()
        };

        return format == expectedFormat;
    }

    /// <summary>
    /// TOML 嵌套表头必须携带完整父路径（[languages.zh] 而非顶层 [zh]），且往返保留
    /// 回归：曾输出裸 [zh] 被解析为顶层表，languages 整体丢失
    /// </summary>
    [Xunit.Fact]
    public void TomlNestedTable_WithLanguages_ShouldIncludeParentPathAndRoundTrip()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "My Blog",
            Languages = new Dictionary<string, LanguageConfig>
            {
                ["zh"] = new LanguageConfig { LanguageName = "中文", Weight = 1, Title = "中文站" },
                ["en"] = new LanguageConfig { LanguageName = "English", Weight = 2 }
            }
        };

        // Act
        var toml = ConfigParser.SerializeToml(config);
        var parsed = ConfigParser.ParseToml(toml);

        // Assert - 表头形态
        Assert.Contains("[languages]", toml, StringComparison.Ordinal);
        Assert.Contains("[languages.zh]", toml, StringComparison.Ordinal);
        Assert.Contains("[languages.en]", toml, StringComparison.Ordinal);
        // 不允许出现脱离父路径的顶层语言表
        Assert.DoesNotContain("\n[zh]", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("\n[en]", toml, StringComparison.Ordinal);

        // Assert - 往返保留
        Assert.Equal(2, parsed.Languages.Count);
        Assert.Equal("中文", parsed.Languages["zh"].LanguageName);
        Assert.Equal(1, parsed.Languages["zh"].Weight);
        Assert.Equal("中文站", parsed.Languages["zh"].Title);
        Assert.Equal("English", parsed.Languages["en"].LanguageName);
    }

    /// <summary>
    /// 数组内含表必须按 [[array-of-tables]] 输出（内联 [...] 不可表达），且往返保留
    /// 回归：曾输出 "System.Collections.Generic.Dictionary`2[...]" 垃圾文本
    /// </summary>
    [Xunit.Fact]
    public void TomlArrayOfTables_WithModuleImports_ShouldRoundTrip()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "My Blog",
            Module = new ModuleConfig
            {
                Imports =
                [
                    new ModuleImport { Path = "github.com/user/repo" },
                    new ModuleImport { Path = "github.com/user/disabled", Disabled = true }
                ]
            }
        };

        // Act
        var toml = ConfigParser.SerializeToml(config);
        var parsed = ConfigParser.ParseToml(toml);

        // Assert - array-of-tables 形态
        Assert.Contains("[[module.imports]]", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary`2", toml, StringComparison.Ordinal);

        // Assert - 往返保留
        Assert.Equal(2, parsed.Module.Imports.Count);
        Assert.Equal("github.com/user/repo", parsed.Module.Imports[0].Path);
        Assert.False(parsed.Module.Imports[0].Disabled);
        Assert.Equal("github.com/user/disabled", parsed.Module.Imports[1].Path);
        Assert.True(parsed.Module.Imports[1].Disabled);
    }

    /// <summary>
    /// 显式 null 字段三格式语义一致（方案 A）：null ≈ 未设置，统一回退默认值。
    /// 回归：此前 JSON null 归一化为空串（title 变 ""），YAML null 走默认值——
    /// 同一配置语义两种结果
    /// </summary>
    [Xunit.Fact]
    public void JsonNullField_ShouldFallBackToDefault_LikeYaml()
    {
        // Arrange - JSON 与 YAML 表达同一配置：title 显式 null
        const string json = """{ "title": null, "baseURL": "https://example.com/" }""";
        const string yaml = "title:\nbaseURL: https://example.com/\n";

        // Act
        var fromJson = ConfigParser.ParseJson(json);
        var fromYaml = ConfigParser.ParseYaml(yaml);

        // Assert - 两格式一致回退默认值（不再分叉：JSON 为 "" 而 YAML 为默认）
        Assert.Equal(fromYaml.Title, fromJson.Title);
        Assert.Equal("Untitled Site", fromJson.Title);

        // Params 内的 null 同语义：键被跳过而非空串
        const string jsonParams = """{ "baseURL": "https://example.com/", "params": { "foo": null } }""";
        var parsedParams = ConfigParser.ParseJson(jsonParams);
        Assert.False(parsedParams.Params.ContainsKey("foo"));
    }
}


/// <summary>
/// 有效站点配置包装类型
/// </summary>
public sealed class ValidSiteConfig
{
    public SiteConfig Value { get; }

    public ValidSiteConfig(SiteConfig value)
    {
        Value = value;
    }

    public override string ToString() => $"SiteConfig(BaseURL={Value.BaseURL}, Title={Value.Title})";
}

/// <summary>
/// 有效配置文件名包装类型
/// </summary>
public sealed class ValidConfigFileName
{
    public string Value { get; }

    public ValidConfigFileName(string value)
    {
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>
/// 有效配置生成器
/// </summary>
public static class ValidConfigArbitrary
{
    /// <summary>
    /// 生成有效的站点配置
    /// </summary>
    public static Arbitrary<ValidSiteConfig> ValidSiteConfig()
    {
        var gen = from baseUrl in Gen.Elements(
                      "http://localhost:1313/",
                      "https://example.com/",
                      "https://blog.example.org/",
                      "http://test.local:8080/",
                      "https://my-site.github.io/")
                  from title in Gen.Elements(
                      "My Blog",
                      "Tech Notes",
                      "Personal Site",
                      "Documentation",
                      "Project Homepage",
                      "Developer Blog")
                  from languageCode in Gen.Elements("en", "zh", "ja", "de", "fr", "es", "ko", "ru")
                  from theme in Gen.Elements("", "default", "minimal", "docs", "blog")
                  from buildDrafts in ArbMap.Default.GeneratorFor<bool>()
                  from buildFuture in ArbMap.Default.GeneratorFor<bool>()
                  from paginate in Gen.Choose(5, 50)
                  from paginatePath in Gen.Elements("page", "p", "pages")
                  select new ValidSiteConfig(new SiteConfig
                  {
                      BaseURL = baseUrl,
                      Title = title,
                      LanguageCode = languageCode,
                      Theme = theme,
                      BuildDrafts = buildDrafts,
                      BuildFuture = buildFuture,
                      Paginate = paginate,
                      PaginatePath = paginatePath
                  });

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的配置文件名
    /// </summary>
    public static Arbitrary<ValidConfigFileName> ValidConfigFileName()
    {
        var gen = Gen.Elements(
            "Flint.toml",
            "Flint.yaml",
            "Flint.yml",
            "Flint.json",
            "config.toml",
            "config.yaml",
            "config.yml",
            "config.json",
            "hugo.toml",
            "hugo.yaml",
            "hugo.json"
        ).Select(name => new ValidConfigFileName(name));

        return gen.ToArbitrary();
    }
}

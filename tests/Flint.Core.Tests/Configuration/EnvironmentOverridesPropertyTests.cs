// Flint 静态站点生成器
// 环境变量覆盖属性测试

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Configuration;

/// <summary>
/// 环境变量覆盖属性测试
/// **验证: 需求 7.6**
/// </summary>
public sealed class EnvironmentOverridesPropertyTests : IDisposable
{
    private readonly List<string> _setEnvVars = [];

    /// <summary>
    /// **Property 13: 环境变量覆盖**
    /// 对于任意配置项和对应的环境变量，环境变量的值应该覆盖配置文件中的值
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(EnvOverrideArbitrary)])]
    public bool EnvVar_ShouldOverrideBaseURL(ValidEnvOverride envOverride)
    {
        ArgumentNullException.ThrowIfNull(envOverride);

        // Arrange - 设置环境变量
        var envVarName = $"{EnvironmentOverrides.Prefix}BASEURL";
        SetEnvVar(envVarName, envOverride.NewBaseUrl);

        var config = new SiteConfig
        {
            BaseURL = envOverride.OriginalBaseUrl,
            Title = "Test Site"
        };

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert - 环境变量应该覆盖原始值
        var ok = result.BaseURL == envOverride.NewBaseUrl;
        // 迭代级清理：环境变量是进程级全局状态，迭代间不清理会污染并行测试
        ClearAllFlintEnvVars();
        return ok;
    }

    /// <summary>
    /// **Property 13: 环境变量覆盖 - Title**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(EnvOverrideArbitrary)])]
    public bool EnvVar_ShouldOverrideTitle(ValidEnvOverride envOverride)
    {
        ArgumentNullException.ThrowIfNull(envOverride);

        // Arrange
        var envVarName = $"{EnvironmentOverrides.Prefix}TITLE";
        SetEnvVar(envVarName, envOverride.NewTitle);

        var config = new SiteConfig
        {
            BaseURL = "http://localhost/",
            Title = envOverride.OriginalTitle
        };

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        var ok = result.Title == envOverride.NewTitle;
        // 迭代级清理：环境变量是进程级全局状态，迭代间不清理会污染并行测试
        ClearAllFlintEnvVars();
        return ok;
    }

    /// <summary>
    /// **Property 13: 环境变量覆盖 - Paginate**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(EnvOverrideArbitrary)])]
    public bool EnvVar_ShouldOverridePaginate(ValidEnvOverride envOverride)
    {
        ArgumentNullException.ThrowIfNull(envOverride);

        // Arrange
        var envVarName = $"{EnvironmentOverrides.Prefix}PAGINATE";
        SetEnvVar(envVarName, envOverride.NewPaginate.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var config = new SiteConfig
        {
            BaseURL = "http://localhost/",
            Title = "Test Site",
            Paginate = envOverride.OriginalPaginate
        };

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        var ok = result.Paginate == envOverride.NewPaginate;
        // 迭代级清理：环境变量是进程级全局状态，迭代间不清理会污染并行测试
        ClearAllFlintEnvVars();
        return ok;
    }

    /// <summary>
    /// **Property 13: 环境变量覆盖 - BuildDrafts**
    /// </summary>
    [Property(MaxTest = 50)]
    public bool EnvVar_ShouldOverrideBuildDrafts(bool originalValue, bool newValue)
    {
        // Arrange
        var envVarName = $"{EnvironmentOverrides.Prefix}BUILDDRAFTS";
        SetEnvVar(envVarName, newValue ? "true" : "false");

        var config = new SiteConfig
        {
            BaseURL = "http://localhost/",
            Title = "Test Site",
            BuildDrafts = originalValue
        };

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        var ok = result.BuildDrafts == newValue;
        // 迭代级清理：环境变量是进程级全局状态，迭代间不清理会污染并行测试
        ClearAllFlintEnvVars();
        return ok;
    }

    /// <summary>
    /// **Property 13: 环境变量覆盖 - 嵌套属性 (Security.HttpTimeout)**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(EnvOverrideArbitrary)])]
    public bool EnvVar_ShouldOverrideNestedProperty(ValidEnvOverride envOverride)
    {
        ArgumentNullException.ThrowIfNull(envOverride);

        // Arrange
        var envVarName = $"{EnvironmentOverrides.Prefix}SECURITY__HTTPTIMEOUT";
        SetEnvVar(envVarName, envOverride.NewHttpTimeout.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var config = new SiteConfig
        {
            BaseURL = "http://localhost/",
            Title = "Test Site",
            Security = new SecurityConfig
            {
                HttpTimeout = envOverride.OriginalHttpTimeout
            }
        };

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        var ok = result.Security.HttpTimeout == envOverride.NewHttpTimeout;
        // 迭代级清理：环境变量是进程级全局状态，迭代间不清理会污染并行测试
        ClearAllFlintEnvVars();
        return ok;
    }

    /// <summary>
    /// 无环境变量时应该保持原始配置不变
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(ValidConfigArbitrary)])]
    public bool NoEnvVar_ShouldPreserveOriginalConfig(ValidSiteConfig validConfig)
    {
        ArgumentNullException.ThrowIfNull(validConfig);

        // Arrange - 确保没有 Flint_ 开头的环境变量
        ClearAllFlintEnvVars();

        var config = validConfig.Value;

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert - 配置应该保持不变
        return result.BaseURL == config.BaseURL &&
               result.Title == config.Title &&
               result.LanguageCode == config.LanguageCode &&
               result.Paginate == config.Paginate;
    }

    /// <summary>
    /// 设置环境变量并记录以便清理
    /// </summary>
    private void SetEnvVar(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _setEnvVars.Add(name);
    }

    /// <summary>
    /// 清除所有 Flint_ 开头的环境变量
    /// </summary>
    private static void ClearAllFlintEnvVars()
    {
        var envVars = Environment.GetEnvironmentVariables();
        foreach (System.Collections.DictionaryEntry entry in envVars)
        {
            if (entry.Key is string key && key.StartsWith(EnvironmentOverrides.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }

    /// <summary>
    /// 清理测试设置的环境变量
    /// </summary>
    public void Dispose()
    {
        foreach (var name in _setEnvVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        _setEnvVars.Clear();
    }
}


/// <summary>
/// 有效环境变量覆盖包装类型
/// </summary>
#pragma warning disable CA1056 // URL 属性使用 string 是有意的，因为环境变量是字符串
public sealed class ValidEnvOverride
{
    public string OriginalBaseUrl { get; init; } = "http://localhost/";
    public string NewBaseUrl { get; init; } = "https://example.com/";
    public string OriginalTitle { get; init; } = "Original";
    public string NewTitle { get; init; } = "New Title";
    public int OriginalPaginate { get; init; } = 10;
    public int NewPaginate { get; init; } = 20;
    public int OriginalHttpTimeout { get; init; } = 30;
    public int NewHttpTimeout { get; init; } = 60;

    public override string ToString() =>
        $"EnvOverride(BaseUrl: {OriginalBaseUrl}->{NewBaseUrl}, Title: {OriginalTitle}->{NewTitle})";
}
#pragma warning restore CA1056

/// <summary>
/// 环境变量覆盖生成器
/// </summary>
public static class EnvOverrideArbitrary
{
    /// <summary>
    /// 生成有效的环境变量覆盖
    /// </summary>
    public static Arbitrary<ValidEnvOverride> ValidEnvOverride()
    {
        var gen = from originalUrl in Gen.Elements(
                      "http://localhost:1313/",
                      "https://example.com/",
                      "https://blog.example.org/",
                      "http://test.local:8080/",
                      "https://my-site.github.io/")
                  from newUrl in Gen.Elements(
                      "http://localhost:1313/",
                      "https://example.com/",
                      "https://blog.example.org/",
                      "http://test.local:8080/",
                      "https://my-site.github.io/")
                  from originalTitle in Gen.Elements(
                      "My Blog",
                      "Tech Notes",
                      "Personal Site",
                      "Documentation",
                      "Project Homepage")
                  from newTitle in Gen.Elements(
                      "My Blog",
                      "Tech Notes",
                      "Personal Site",
                      "Documentation",
                      "Project Homepage")
                  from originalPaginate in Gen.Choose(5, 100)
                  from newPaginate in Gen.Choose(5, 100)
                  from originalTimeout in Gen.Choose(10, 120)
                  from newTimeout in Gen.Choose(10, 120)
                  where originalUrl != newUrl && originalTitle != newTitle
                  select new ValidEnvOverride
                  {
                      OriginalBaseUrl = originalUrl,
                      NewBaseUrl = newUrl,
                      OriginalTitle = originalTitle,
                      NewTitle = newTitle,
                      OriginalPaginate = originalPaginate,
                      NewPaginate = newPaginate,
                      OriginalHttpTimeout = originalTimeout,
                      NewHttpTimeout = newTimeout
                  };

        return gen.ToArbitrary();
    }
}

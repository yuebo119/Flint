// Flint 静态站点生成器
// 环境变量覆盖处理器单元测试

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Xunit;

namespace Flint.Core.Tests.Configuration;

/// <summary>
/// EnvironmentOverrides 单元测试
/// </summary>
public class EnvironmentOverridesTests : IDisposable
{
    private readonly List<string> _setEnvVars = [];

    public void Dispose()
    {
        // 清理测试设置的环境变量
        foreach (var key in _setEnvVars)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
        GC.SuppressFinalize(this);
    }

    private void SetEnvVar(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        _setEnvVars.Add(key);
    }

    #region 常量测试

    [Fact]
    public void Prefix_应该是Flint_()
    {
        Assert.Equal("Flint_", EnvironmentOverrides.Prefix);
    }

    [Fact]
    public void Separator_应该是双下划线()
    {
        Assert.Equal("__", EnvironmentOverrides.Separator);
    }

    #endregion

    #region ApplyOverrides 基本测试

    [Fact]
    public void ApplyOverrides_无环境变量时应该返回原配置()
    {
        // Arrange
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal(config.BaseURL, result.BaseURL);
        Assert.Equal(config.Title, result.Title);
    }

    [Fact]
    public void ApplyOverrides_null配置应该抛出异常()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => EnvironmentOverrides.ApplyOverrides(null!));
    }

    #endregion

    #region 基本属性覆盖测试

    [Fact]
    public void ApplyOverrides_应该覆盖BaseURL()
    {
        // Arrange
        SetEnvVar("Flint_BASEURL", "https://new-site.com");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("https://new-site.com", result.BaseURL);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖Title()
    {
        // Arrange
        SetEnvVar("Flint_TITLE", "新标题");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("新标题", result.Title);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖LanguageCode()
    {
        // Arrange
        SetEnvVar("Flint_LANGUAGECODE", "zh-CN");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("zh-CN", result.LanguageCode);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖Theme()
    {
        // Arrange
        SetEnvVar("Flint_THEME", "dark-theme");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("dark-theme", result.Theme);
    }

    #endregion

    #region 布尔属性覆盖测试

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    public void ApplyOverrides_应该正确解析布尔值(string envValue, bool expected)
    {
        // Arrange
        SetEnvVar("Flint_BUILDDRAFTS", envValue);
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal(expected, result.BuildDrafts);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖BuildFuture()
    {
        // Arrange
        SetEnvVar("Flint_BUILDFUTURE", "true");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.True(result.BuildFuture);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖BuildExpired()
    {
        // Arrange
        SetEnvVar("Flint_BUILDEXPIRED", "true");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.True(result.BuildExpired);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖EnableGitInfo()
    {
        // Arrange
        SetEnvVar("Flint_ENABLEGITINFO", "true");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.True(result.EnableGitInfo);
    }

    #endregion

    #region 整数属性覆盖测试

    [Fact]
    public void ApplyOverrides_应该覆盖Paginate()
    {
        // Arrange
        SetEnvVar("Flint_PAGINATE", "20");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal(20, result.Paginate);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖SummaryLength()
    {
        // Arrange
        SetEnvVar("Flint_SUMMARYLENGTH", "100");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal(100, result.SummaryLength);
    }

    [Fact]
    public void ApplyOverrides_无效整数应该保留原值()
    {
        // Arrange
        SetEnvVar("Flint_PAGINATE", "invalid");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal(config.Paginate, result.Paginate);
    }

    #endregion

    #region 目录配置覆盖测试

    [Fact]
    public void ApplyOverrides_应该覆盖ContentDir()
    {
        // Arrange
        SetEnvVar("Flint_CONTENTDIR", "posts");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("posts", result.ContentDir);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖PublishDir()
    {
        // Arrange
        SetEnvVar("Flint_PUBLISHDIR", "dist");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("dist", result.PublishDir);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖LayoutDir()
    {
        // Arrange
        SetEnvVar("Flint_LAYOUTDIR", "templates");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("templates", result.LayoutDir);
    }

    #endregion

    #region 安全配置覆盖测试

    [Fact]
    public void ApplyOverrides_应该覆盖Security_HttpTimeout()
    {
        // Arrange
        SetEnvVar("Flint_SECURITY__HTTPTIMEOUT", "60");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal(60, result.Security.HttpTimeout);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖Security_AllowedDomains()
    {
        // Arrange
        SetEnvVar("Flint_SECURITY__ALLOWEDDOMAINS", "example.com,test.com");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal(2, result.Security.AllowedDomains.Count);
        Assert.Contains("example.com", result.Security.AllowedDomains);
        Assert.Contains("test.com", result.Security.AllowedDomains);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖Security_AllowedCommands()
    {
        // Arrange
        SetEnvVar("Flint_SECURITY__ALLOWEDCOMMANDS", "git,npm");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal(2, result.Security.AllowedCommands.Count);
        Assert.Contains("git", result.Security.AllowedCommands);
        Assert.Contains("npm", result.Security.AllowedCommands);
    }

    #endregion

    #region 缓存配置覆盖测试

    [Fact]
    public void ApplyOverrides_应该覆盖Caches_Enabled()
    {
        // Arrange
        SetEnvVar("Flint_CACHES__ENABLED", "false");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.False(result.Caches.Enabled);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖Caches_Dir()
    {
        // Arrange
        SetEnvVar("Flint_CACHES__DIR", "/tmp/cache");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("/tmp/cache", result.Caches.Dir);
    }

    [Fact]
    public void ApplyOverrides_应该覆盖Caches_MaxSize()
    {
        // Arrange
        SetEnvVar("Flint_CACHES__MAXSIZE", "500");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal(500, result.Caches.MaxSize);
    }

    #endregion

    #region 多个覆盖测试

    [Fact]
    public void ApplyOverrides_应该同时覆盖多个属性()
    {
        // Arrange
        SetEnvVar("Flint_BASEURL", "https://new-site.com");
        SetEnvVar("Flint_TITLE", "新标题");
        SetEnvVar("Flint_PAGINATE", "20");
        SetEnvVar("Flint_BUILDDRAFTS", "true");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("https://new-site.com", result.BaseURL);
        Assert.Equal("新标题", result.Title);
        Assert.Equal(20, result.Paginate);
        Assert.True(result.BuildDrafts);
    }

    #endregion

    #region 保留原值测试

    [Fact]
    public void ApplyOverrides_应该保留未覆盖的属性()
    {
        // Arrange
        SetEnvVar("Flint_TITLE", "新标题");
        var config = CreateDefaultConfig();

        // Act
        var result = EnvironmentOverrides.ApplyOverrides(config);

        // Assert
        Assert.Equal("新标题", result.Title);
        Assert.Equal(config.BaseURL, result.BaseURL);
        Assert.Equal(config.LanguageCode, result.LanguageCode);
        Assert.Equal(config.Paginate, result.Paginate);
    }

    #endregion

    #region 辅助方法

    private static SiteConfig CreateDefaultConfig()
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com",
            Title = "测试站点",
            LanguageCode = "en",
            Theme = "default",
            BuildDrafts = false,
            BuildFuture = false,
            BuildExpired = false,
            Paginate = 10,
            PaginatePath = "page",
            EnableGitInfo = false,
            SummaryLength = 70,
            Copyright = "© 2024",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            DisablePathToLower = false,
            DisableKinds = [],
            Permalinks = new PermalinkConfig(),
            Taxonomies = new Flint.Core.Abstractions.TaxonomyConfig(),
            Menus = new MenuConfig(),
            Params = new Dictionary<string, object>(),
            Markup = new MarkupConfig(),
            Outputs = new OutputConfig(),
            Languages = new Dictionary<string, Flint.Core.Abstractions.LanguageConfig>(),
            Module = new ModuleConfig(),
            Security = new SecurityConfig
            {
                HttpTimeout = 30,
                AllowedDomains = [],
                AllowedCommands = []
            },
            Caches = new CacheConfig
            {
                Enabled = true,
                Dir = ".cache",
                MaxSize = 100
            },
            Author = new AuthorConfig { Name = "Test Author" }
        };
    }

    #endregion
}

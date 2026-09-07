// Flint 静态站点生成器
// 配置加载器单元测试

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Xunit;

namespace Flint.Core.Tests.Configuration;

/// <summary>
/// ConfigLoader 单元测试
/// </summary>
public class ConfigLoaderTests : IDisposable
{
    private readonly string _testDir;
    private readonly ConfigLoader _loader;

    public ConfigLoaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"Flint_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _loader = new ConfigLoader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    #region Load 测试

    [Fact]
    public async Task LoadAsync_应该加载TOML配置文件()
    {
        // Arrange
        var configPath = Path.Combine(_testDir, "Flint.toml");
        var tomlContent = """
            baseURL = "https://example.com/"
            title = "测试站点"
            languageCode = "zh-CN"
            """;
        await File.WriteAllTextAsync(configPath, tomlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        Assert.Equal("https://example.com/", config.BaseURL);
        Assert.Equal("测试站点", config.Title);
        Assert.Equal("zh-CN", config.LanguageCode);
    }

    [Fact]
    public async Task LoadAsync_应该加载YAML配置文件()
    {
        // Arrange
        var configPath = Path.Combine(_testDir, "Flint.yaml");
        var yamlContent = """
            baseURL: "https://example.com/"
            title: "YAML测试站点"
            languageCode: "en"
            """;
        await File.WriteAllTextAsync(configPath, yamlContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        Assert.Equal("https://example.com/", config.BaseURL);
        Assert.Equal("YAML测试站点", config.Title);
    }

    [Fact]
    public async Task LoadAsync_应该加载JSON配置文件()
    {
        // Arrange
        var configPath = Path.Combine(_testDir, "Flint.json");
        var jsonContent = """
            {
                "baseURL": "https://example.com/",
                "title": "JSON测试站点",
                "languageCode": "en"
            }
            """;
        await File.WriteAllTextAsync(configPath, jsonContent);

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        Assert.Equal("https://example.com/", config.BaseURL);
        Assert.Equal("JSON测试站点", config.Title);
    }

    [Fact]
    public async Task LoadAsync_文件不存在时应该抛出异常()
    {
        // Arrange
        var configPath = Path.Combine(_testDir, "nonexistent.toml");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public void Load_应该同步加载配置文件()
    {
        // Arrange
        var configPath = Path.Combine(_testDir, "Flint.toml");
        var tomlContent = """
            baseURL = "https://sync.example.com/"
            title = "同步测试"
            """;
        File.WriteAllText(configPath, tomlContent);

        // Act
        var config = _loader.Load(configPath);

        // Assert
        Assert.Equal("https://sync.example.com/", config.BaseURL);
        Assert.Equal("同步测试", config.Title);
    }

    #endregion

    #region AutoLoad 测试

    [Fact]
    public async Task AutoLoadAsync_应该自动检测Flint_toml()
    {
        // Arrange
        var configPath = Path.Combine(_testDir, "Flint.toml");
        await File.WriteAllTextAsync(configPath, """
            baseURL = "https://auto.example.com/"
            title = "自动检测"
            """);

        // Act
        var config = await _loader.AutoLoadAsync(_testDir);

        // Assert
        Assert.Equal("https://auto.example.com/", config.BaseURL);
    }

    [Fact]
    public async Task AutoLoadAsync_应该按优先级检测配置文件()
    {
        // Arrange - 创建多个配置文件，Flint.toml 优先级最高
        await File.WriteAllTextAsync(Path.Combine(_testDir, "config.toml"), """
            baseURL = "https://config.example.com/"
            title = "config.toml"
            """);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "Flint.toml"), """
            baseURL = "https://Flint.example.com/"
            title = "Flint.toml"
            """);

        // Act
        var config = await _loader.AutoLoadAsync(_testDir);

        // Assert - 应该加载 Flint.toml
        Assert.Equal("https://Flint.example.com/", config.BaseURL);
        Assert.Equal("Flint.toml", config.Title);
    }

    [Fact]
    public async Task AutoLoadAsync_没有配置文件时应该抛出异常()
    {
        // Arrange - 空目录

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _loader.AutoLoadAsync(_testDir).AsTask());
    }

    #endregion

    #region Save 测试

    [Fact]
    public async Task SaveAsync_应该保存TOML格式配置()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://save.example.com/",
            Title = "保存测试"
        };
        var configPath = Path.Combine(_testDir, "saved.toml");

        // Act
        await _loader.SaveAsync(config, configPath, ConfigFormat.Toml);

        // Assert
        Assert.True(File.Exists(configPath));
        var content = await File.ReadAllTextAsync(configPath);
        Assert.Contains("https://save.example.com/", content);
        Assert.Contains("保存测试", content);
    }

    [Fact]
    public async Task SaveAsync_应该创建不存在的目录()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "测试"
        };
        var subDir = Path.Combine(_testDir, "subdir", "config");
        var configPath = Path.Combine(subDir, "Flint.toml");

        // Act
        await _loader.SaveAsync(config, configPath, ConfigFormat.Toml);

        // Assert
        Assert.True(Directory.Exists(subDir));
        Assert.True(File.Exists(configPath));
    }

    #endregion

    #region 静态方法测试

    [Fact]
    public void FindConfigFile_应该找到配置文件()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDir, "Flint.toml"), "baseURL = \"test\"");

        // Act
        var result = ConfigLoader.FindConfigFile(_testDir);

        // Assert
        Assert.NotNull(result);
        Assert.EndsWith("Flint.toml", result);
    }

    [Fact]
    public void FindConfigFile_没有配置文件时应该返回null()
    {
        // Act
        var result = ConfigLoader.FindConfigFile(_testDir);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void HasConfigFile_存在配置文件时应该返回true()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDir, "config.yaml"), "baseURL: test");

        // Act
        var result = ConfigLoader.HasConfigFile(_testDir);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasConfigFile_不存在配置文件时应该返回false()
    {
        // Act
        var result = ConfigLoader.HasConfigFile(_testDir);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetDefaultConfig_应该返回有效的默认配置()
    {
        // Act
        var config = ConfigLoader.GetDefaultConfig("https://test.com/", "Test Site");

        // Assert
        Assert.Equal("https://test.com/", config.BaseURL);
        Assert.Equal("Test Site", config.Title);
        Assert.Equal("en", config.LanguageCode);
        Assert.Equal("content", config.ContentDir);
        Assert.Equal("layouts", config.LayoutDir);
        Assert.Equal("public", config.PublishDir);
    }

    [Fact]
    public void GetDefaultConfig_baseUrl为null时应该抛出异常()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ConfigLoader.GetDefaultConfig(null!));
    }

    #endregion
}

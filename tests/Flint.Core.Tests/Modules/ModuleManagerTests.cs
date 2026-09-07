// Flint 静态站点生成器
// 模块管理器单元测试

using Flint.Core.Modules;
using Xunit;

namespace Flint.Core.Tests.Modules;

/// <summary>
/// ModuleManager 单元测试
/// </summary>
public class ModuleManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly ModuleManager _manager;

    public ModuleManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"Flint_module_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _manager = new ModuleManager(_testDir);
    }

    public void Dispose()
    {
        _manager.Dispose();
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    #region InitAsync 测试

    [Fact]
    public async Task InitAsync_应该创建配置文件和主题目录()
    {
        // Act
        await _manager.InitAsync(_testDir);

        // Assert
        Assert.True(File.Exists(Path.Combine(_testDir, "Flint.toml")));
        Assert.True(Directory.Exists(Path.Combine(_testDir, "themes")));
    }

    [Fact]
    public async Task InitAsync_已存在配置文件时不应覆盖()
    {
        // Arrange
        var configPath = Path.Combine(_testDir, "Flint.toml");
        var originalContent = "# 原始配置\ntitle = \"测试\"";
        await File.WriteAllTextAsync(configPath, originalContent);

        // Act
        await _manager.InitAsync(_testDir);

        // Assert
        var content = await File.ReadAllTextAsync(configPath);
        Assert.Equal(originalContent, content);
    }

    #endregion

    #region ListAsync 测试

    [Fact]
    public async Task ListAsync_无模块时应该返回空列表()
    {
        // Act
        var modules = await _manager.ListAsync();

        // Assert
        Assert.Empty(modules);
    }

    [Fact]
    public async Task ListAsync_应该列出已安装的模块()
    {
        // Arrange
        var themesDir = Path.Combine(_testDir, "themes");
        Directory.CreateDirectory(themesDir);

        var themeDir = Path.Combine(themesDir, "test-theme");
        Directory.CreateDirectory(themeDir);

        var themeConfig = """
            name = "测试主题"
            version = "1.0.0"
            repository = "github.com/test/theme"
            """;
        await File.WriteAllTextAsync(Path.Combine(themeDir, "theme.toml"), themeConfig);

        // Act
        var modules = await _manager.ListAsync();

        // Assert
        Assert.Single(modules);
        Assert.Equal("测试主题", modules[0].Name);
        Assert.Equal("1.0.0", modules[0].Version);
    }

    [Fact]
    public async Task ListAsync_应该处理无配置文件的模块()
    {
        // Arrange
        var themesDir = Path.Combine(_testDir, "themes");
        Directory.CreateDirectory(themesDir);

        var themeDir = Path.Combine(themesDir, "no-config-theme");
        Directory.CreateDirectory(themeDir);

        // Act
        var modules = await _manager.ListAsync();

        // Assert
        Assert.Single(modules);
        Assert.Equal("no-config-theme", modules[0].Name);
        Assert.Equal("unknown", modules[0].Version);
    }

    #endregion

    #region RemoveAsync 测试

    [Fact]
    public async Task RemoveAsync_应该删除已安装的模块()
    {
        // Arrange
        var themesDir = Path.Combine(_testDir, "themes");
        Directory.CreateDirectory(themesDir);

        var themeDir = Path.Combine(themesDir, "to-remove");
        Directory.CreateDirectory(themeDir);
        await File.WriteAllTextAsync(Path.Combine(themeDir, "theme.toml"), "name = \"to-remove\"");

        // Act
        await _manager.RemoveAsync("to-remove");

        // Assert
        Assert.False(Directory.Exists(themeDir));
    }

    [Fact]
    public async Task RemoveAsync_模块不存在时应该抛出异常()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.RemoveAsync("nonexistent").AsTask());
    }

    #endregion

    #region UpdateAsync 测试

    [Fact]
    public async Task UpdateAsync_模块不存在时应该抛出异常()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.UpdateAsync("nonexistent").AsTask());
    }

    #endregion
}

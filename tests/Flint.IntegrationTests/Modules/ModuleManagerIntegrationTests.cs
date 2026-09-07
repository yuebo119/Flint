// Flint 静态站点生成器
// 模块管理器集成测试
// 验证模块的安装、更新、列表和删除功能

using Flint.Core.Modules;
using Flint.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Modules;

/// <summary>
/// 模块管理器集成测试
/// 验证模块的安装、更新、列表和删除功能
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 7.1: 模块初始化
/// - Requirements 7.4: 模块列表
/// - Requirements 7.5: 模块移除
/// </remarks>
[Collection("Modules")]
public class ModuleManagerIntegrationTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private ModuleManager? _moduleManager;

    public ModuleManagerIntegrationTests()
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

    #region 模块初始化测试

    /// <summary>
    /// 测试模块初始化创建配置文件
    /// </summary>
    [Fact]
    public async Task InitAsync_CreatesConfigFile()
    {
        // Arrange
        var manager = CreateModuleManager();
        var testPath = Path.Combine(_fixture.SiteRoot, "test-init");
        Directory.CreateDirectory(testPath);

        // Act
        await manager.InitAsync(testPath);

        // Assert
        var configPath = Path.Combine(testPath, "Flint.toml");
        File.Exists(configPath).Should().BeTrue("应该创建配置文件");
    }

    /// <summary>
    /// 测试模块初始化创建 themes 目录
    /// </summary>
    [Fact]
    public async Task InitAsync_CreatesThemesDirectory()
    {
        // Arrange
        var manager = CreateModuleManager();
        var testPath = Path.Combine(_fixture.SiteRoot, "test-init-themes");
        Directory.CreateDirectory(testPath);

        // Act
        await manager.InitAsync(testPath);

        // Assert
        var themesPath = Path.Combine(testPath, "themes");
        Directory.Exists(themesPath).Should().BeTrue("应该创建 themes 目录");
    }

    /// <summary>
    /// 测试重复初始化不会覆盖现有配置
    /// </summary>
    [Fact]
    public async Task InitAsync_DoesNotOverwriteExistingConfig()
    {
        // Arrange
        var manager = CreateModuleManager();
        var testPath = Path.Combine(_fixture.SiteRoot, "test-init-existing");
        Directory.CreateDirectory(testPath);

        var configPath = Path.Combine(testPath, "Flint.toml");
        var originalContent = "# 原始配置\ntitle = \"测试站点\"";
        await File.WriteAllTextAsync(configPath, originalContent);

        // Act
        await manager.InitAsync(testPath);

        // Assert
        var content = await File.ReadAllTextAsync(configPath);
        content.Should().Be(originalContent, "不应覆盖现有配置");
    }

    #endregion

    #region 模块列表测试

    /// <summary>
    /// 测试空站点返回空模块列表
    /// </summary>
    [Fact]
    public async Task ListAsync_EmptySite_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateModuleManager();

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().BeEmpty();
    }

    /// <summary>
    /// 测试列出已安装的模块
    /// </summary>
    [Fact]
    public async Task ListAsync_WithInstalledModules_ReturnsModuleList()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        Directory.CreateDirectory(themesPath);

        // 创建模拟模块
        var modulePath = Path.Combine(themesPath, "test-theme");
        Directory.CreateDirectory(modulePath);
        await File.WriteAllTextAsync(
            Path.Combine(modulePath, "theme.toml"),
            """
            name = "test-theme"
            version = "1.0.0"
            repository = "https://github.com/test/test-theme"
            """);

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().HaveCount(1);
        modules[0].Name.Should().Be("test-theme");
        modules[0].Version.Should().Be("1.0.0");
    }

    /// <summary>
    /// 测试列出多个模块
    /// </summary>
    [Fact]
    public async Task ListAsync_MultipleModules_ReturnsAllModules()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        Directory.CreateDirectory(themesPath);

        // 创建多个模拟模块
        for (var i = 1; i <= 3; i++)
        {
            var modulePath = Path.Combine(themesPath, $"theme-{i}");
            Directory.CreateDirectory(modulePath);
            await File.WriteAllTextAsync(
                Path.Combine(modulePath, "theme.toml"),
                $"""
                name = "theme-{i}"
                version = "{i}.0.0"
                repository = "https://github.com/test/theme-{i}"
                """);
        }

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().HaveCount(3);
    }

    /// <summary>
    /// 测试列出没有配置文件的模块
    /// </summary>
    [Fact]
    public async Task ListAsync_ModuleWithoutConfig_ReturnsDefaultDescriptor()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "no-config-theme");
        Directory.CreateDirectory(modulePath);

        // 不创建配置文件

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().HaveCount(1);
        modules[0].Name.Should().Be("no-config-theme");
        modules[0].Version.Should().Be("unknown");
    }

    #endregion

    #region 模块移除测试

    /// <summary>
    /// 测试移除已安装的模块
    /// </summary>
    [Fact]
    public async Task RemoveAsync_InstalledModule_RemovesModule()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "to-remove");
        Directory.CreateDirectory(modulePath);
        await File.WriteAllTextAsync(
            Path.Combine(modulePath, "theme.toml"),
            """
            name = "to-remove"
            version = "1.0.0"
            """);

        // Act
        await manager.RemoveAsync("to-remove");

        // Assert
        Directory.Exists(modulePath).Should().BeFalse("模块目录应该被删除");
    }

    /// <summary>
    /// 测试移除不存在的模块抛出异常
    /// </summary>
    [Fact]
    public async Task RemoveAsync_NonExistentModule_ThrowsException()
    {
        // Arrange
        var manager = CreateModuleManager();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.RemoveAsync("non-existent").AsTask());
    }

    /// <summary>
    /// 测试移除模块后更新锁文件
    /// </summary>
    [Fact]
    public async Task RemoveAsync_UpdatesLockFile()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "locked-module");
        Directory.CreateDirectory(modulePath);
        await File.WriteAllTextAsync(
            Path.Combine(modulePath, "theme.toml"),
            """
            name = "locked-module"
            version = "1.0.0"
            """);

        // 创建锁文件
        var lockFilePath = Path.Combine(_fixture.SiteRoot, "Flint.lock");
        await File.WriteAllTextAsync(lockFilePath, """
            [locked-module]
            version = "1.0.0"
            repository = ""
            """);

        // Act
        await manager.RemoveAsync("locked-module");

        // Assert
        if (File.Exists(lockFilePath))
        {
            var lockContent = await File.ReadAllTextAsync(lockFilePath);
            lockContent.Should().NotContain("locked-module");
        }
    }

    #endregion

    #region 模块版本管理测试

    /// <summary>
    /// 测试模块描述符包含版本信息
    /// </summary>
    [Fact]
    public async Task ModuleDescriptor_ContainsVersionInfo()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "versioned-theme");
        Directory.CreateDirectory(modulePath);
        await File.WriteAllTextAsync(
            Path.Combine(modulePath, "theme.toml"),
            """
            name = "versioned-theme"
            version = "2.5.3"
            repository = "https://github.com/test/versioned-theme"
            """);

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().HaveCount(1);
        modules[0].Version.Should().Be("2.5.3");
    }

    #endregion

    #region IDisposable 测试

    /// <summary>
    /// 测试 ModuleManager 实现 IDisposable
    /// </summary>
    [Fact]
    public void ModuleManager_ImplementsIDisposable()
    {
        // Arrange
        var manager = CreateModuleManager();

        // Assert
        manager.Should().BeAssignableTo<IDisposable>();
    }

    /// <summary>
    /// 测试多次 Dispose 不会抛出异常
    /// </summary>
    [Fact]
    public void MultipleDispose_DoesNotThrow()
    {
        // Arrange
        var manager = CreateModuleManager();

        // Act & Assert
        manager.Dispose();
        manager.Dispose();
        manager.Dispose();
    }

    #endregion
}

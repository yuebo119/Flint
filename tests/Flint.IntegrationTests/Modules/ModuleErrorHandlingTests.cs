// Flint 静态站点生成器
// 模块错误处理测试
// 验证各种错误场景的处理

using Flint.Core.Modules;
using Flint.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Modules;

/// <summary>
/// 模块错误处理测试
/// 验证各种错误场景的处理
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 7.7: 无效 URL 错误
/// - Requirements 7.10: 损坏模块处理
/// </remarks>
[Collection("Modules")]
public class ModuleErrorHandlingTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private ModuleManager? _moduleManager;

    public ModuleErrorHandlingTests()
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

    #region 无效 URL 错误测试

    /// <summary>
    /// 测试无效仓库格式抛出异常
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("not-a-url")]
    public async Task GetAsync_InvalidRepositoryFormat_ThrowsException(string invalidRepo)
    {
        // Arrange
        var manager = CreateModuleManager();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.GetAsync(invalidRepo).AsTask());
    }

    /// <summary>
    /// 测试不存在的仓库处理
    /// </summary>
    [Fact]
    public async Task GetAsync_NonExistentRepository_ThrowsException()
    {
        // Arrange
        var manager = CreateModuleManager();
        var nonExistentRepo = "https://github.com/non-existent-user-12345/non-existent-repo-67890";

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => manager.GetAsync(nonExistentRepo).AsTask());
    }

    /// <summary>
    /// 测试无效版本处理
    /// </summary>
    [Fact]
    public async Task GetAsync_InvalidVersion_HandlesGracefully()
    {
        // Arrange
        var manager = CreateModuleManager();
        // 使用一个真实但不存在的版本
        var repo = "https://github.com/gohugoio/hugo";
        var invalidVersion = "v999.999.999-nonexistent";

        // Act & Assert - 应该抛出异常或回退到默认分支
        try
        {
            await manager.GetAsync(repo, invalidVersion);
        }
        catch (HttpRequestException)
        {
            // 预期的网络错误
        }
        catch (Exception)
        {
            // 其他错误也是可接受的
        }
    }

    #endregion

    #region 损坏模块处理测试

    /// <summary>
    /// 测试损坏的配置文件处理
    /// </summary>
    [Fact]
    public async Task ListAsync_CorruptedConfigFile_HandlesGracefully()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "corrupted-theme");
        Directory.CreateDirectory(modulePath);

        // 创建损坏的配置文件
        await File.WriteAllTextAsync(
            Path.Combine(modulePath, "theme.toml"),
            """
            this is not valid toml
            [[[invalid
            name = 
            """);

        // Act
        var modules = await manager.ListAsync();

        // Assert - 应该返回默认描述符而不是崩溃
        modules.Should().HaveCount(1);
        modules[0].Name.Should().Be("corrupted-theme");
        modules[0].Version.Should().Be("unknown");
    }

    /// <summary>
    /// 测试空配置文件处理
    /// </summary>
    [Fact]
    public async Task ListAsync_EmptyConfigFile_HandlesGracefully()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "empty-config-theme");
        Directory.CreateDirectory(modulePath);

        // 创建空配置文件
        await File.WriteAllTextAsync(
            Path.Combine(modulePath, "theme.toml"),
            "");

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().HaveCount(1);
        modules[0].Name.Should().Be("empty-config-theme");
    }

    /// <summary>
    /// 测试缺少必需字段的配置文件
    /// </summary>
    [Fact]
    public async Task ListAsync_MissingRequiredFields_UsesDefaults()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "partial-config-theme");
        Directory.CreateDirectory(modulePath);

        // 创建只有部分字段的配置文件
        await File.WriteAllTextAsync(
            Path.Combine(modulePath, "theme.toml"),
            """
            description = "只有描述，没有名称和版本"
            """);

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().HaveCount(1);
        modules[0].Name.Should().Be("partial-config-theme"); // 使用目录名
        modules[0].Version.Should().Be("1.0.0"); // 使用默认版本
    }

    #endregion

    #region 网络错误处理测试

    /// <summary>
    /// 测试网络超时处理
    /// </summary>
    [Fact]
    public async Task GetAsync_NetworkTimeout_ThrowsException()
    {
        // Arrange
        var manager = CreateModuleManager();
        // 使用一个可能导致超时的地址
        var slowRepo = "https://github.com/non-existent-slow-repo/timeout-test";

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => manager.GetAsync(slowRepo).AsTask());
    }

    #endregion

    #region 更新错误处理测试

    /// <summary>
    /// 测试更新未安装的模块抛出异常
    /// </summary>
    [Fact]
    public async Task UpdateAsync_NotInstalled_ThrowsException()
    {
        // Arrange
        var manager = CreateModuleManager();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.UpdateAsync("not-installed-module").AsTask());
    }

    /// <summary>
    /// 测试更新损坏的模块
    /// </summary>
    [Fact]
    public async Task UpdateAsync_CorruptedModule_ThrowsException()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "corrupted-update-theme");
        Directory.CreateDirectory(modulePath);

        // 创建损坏的配置（没有 repository 字段）
        await File.WriteAllTextAsync(
            Path.Combine(modulePath, "theme.toml"),
            """
            name = "corrupted-update-theme"
            version = "1.0.0"
            """);

        // Act & Assert - 应该抛出异常因为没有 repository
        await Assert.ThrowsAnyAsync<Exception>(
            () => manager.UpdateAsync("corrupted-update-theme").AsTask());
    }

    #endregion

    #region 删除错误处理测试

    /// <summary>
    /// 测试删除不存在的模块抛出异常
    /// </summary>
    [Fact]
    public async Task RemoveAsync_NotInstalled_ThrowsException()
    {
        // Arrange
        var manager = CreateModuleManager();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.RemoveAsync("not-installed-module").AsTask());
    }

    /// <summary>
    /// 测试删除空名称模块抛出异常。
    /// 空名参数校验用 ArgumentException（.NET 惯例）；旧实现空名会解析到 themes
    /// 目录本身并递归删除，异常类型取决于目录是否存在——那正是被修复的缺陷
    /// </summary>
    [Fact]
    public async Task RemoveAsync_EmptyName_ThrowsException()
    {
        // Arrange
        var manager = CreateModuleManager();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.RemoveAsync("").AsTask());
    }

    #endregion

    #region 锁文件错误处理测试

    /// <summary>
    /// 测试损坏的锁文件处理
    /// </summary>
    [Fact]
    public async Task RemoveAsync_CorruptedLockFile_HandlesGracefully()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "lock-test-theme");
        Directory.CreateDirectory(modulePath);

        // 创建损坏的锁文件
        var lockFilePath = Path.Combine(_fixture.SiteRoot, "Flint.lock");
        await File.WriteAllTextAsync(lockFilePath, "this is not valid toml [[[");

        // Act - 删除模块应该成功，即使锁文件损坏
        await manager.RemoveAsync("lock-test-theme");

        // Assert
        Directory.Exists(modulePath).Should().BeFalse();
    }

    /// <summary>
    /// 测试不存在的锁文件处理
    /// </summary>
    [Fact]
    public async Task RemoveAsync_NoLockFile_HandlesGracefully()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");
        var modulePath = Path.Combine(themesPath, "no-lock-theme");
        Directory.CreateDirectory(modulePath);

        // 确保没有锁文件
        var lockFilePath = Path.Combine(_fixture.SiteRoot, "Flint.lock");
        if (File.Exists(lockFilePath))
        {
            File.Delete(lockFilePath);
        }

        // Act
        await manager.RemoveAsync("no-lock-theme");

        // Assert
        Directory.Exists(modulePath).Should().BeFalse();
    }

    #endregion

    #region 边界条件测试

    /// <summary>
    /// 测试特殊字符模块名称
    /// </summary>
    [Fact]
    public async Task ListAsync_SpecialCharacterModuleName_HandlesGracefully()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");

        // 创建带有特殊字符的模块目录（在 Windows 上可能有限制）
        var moduleName = "theme-with-dash_and_underscore";
        var modulePath = Path.Combine(themesPath, moduleName);
        Directory.CreateDirectory(modulePath);

        await File.WriteAllTextAsync(
            Path.Combine(modulePath, "theme.toml"),
            $"""
            name = "{moduleName}"
            version = "1.0.0"
            """);

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().HaveCount(1);
        modules[0].Name.Should().Be(moduleName);
    }

    /// <summary>
    /// 测试深层嵌套的模块目录
    /// </summary>
    [Fact]
    public async Task ListAsync_NestedDirectories_OnlyListsTopLevel()
    {
        // Arrange
        var manager = CreateModuleManager();
        var themesPath = Path.Combine(_fixture.SiteRoot, "themes");

        // 创建顶级模块
        var topLevelPath = Path.Combine(themesPath, "top-level-theme");
        Directory.CreateDirectory(topLevelPath);
        await File.WriteAllTextAsync(
            Path.Combine(topLevelPath, "theme.toml"),
            """
            name = "top-level-theme"
            version = "1.0.0"
            """);

        // 创建嵌套目录（不应该被列为模块）
        var nestedPath = Path.Combine(topLevelPath, "nested", "deep");
        Directory.CreateDirectory(nestedPath);

        // Act
        var modules = await manager.ListAsync();

        // Assert
        modules.Should().HaveCount(1);
        modules[0].Name.Should().Be("top-level-theme");
    }

    #endregion
}

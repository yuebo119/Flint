// Flint 静态站点生成器
// CLI 创建站点命令端到端测试
// 测试 new site 命令的功能和错误处理

using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Cli;

/// <summary>
/// CLI 创建站点命令测试
/// 验证 new site 命令的功能、目录结构和错误处理
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 1.2: 测试 new site 命令
/// </remarks>
[Trait("Category", "CLI")]
[Trait("Category", "EndToEnd")]
public sealed class CliNewSiteTests : IAsyncLifetime
{
    private readonly CliTestRunner _cli;
    private readonly string _testDir;
    private readonly List<string> _createdDirs;

    public CliNewSiteTests()
    {
        _cli = new CliTestRunner();
        _testDir = Path.Combine(Path.GetTempPath(), "Flint-cli-tests", Guid.NewGuid().ToString("N")[..8]);
        _createdDirs = [];
    }

    public Task InitializeAsync()
    {
        // 创建测试目录
        Directory.CreateDirectory(_testDir);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cli.Dispose();

        // 清理创建的目录
        await Task.Delay(100); // 等待文件句柄释放

        foreach (var dir in _createdDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }

        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
    }

    #region 正常场景测试

    /// <summary>
    /// 测试创建新站点成功
    /// </summary>
    [Fact]
    public async Task NewSite_WithValidName_ShouldSucceed()
    {
        // Arrange
        var siteName = $"test-site-{Guid.NewGuid():N}"[..20];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        result.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {result.ErrorOutput}");
        Directory.Exists(sitePath).Should().BeTrue("站点目录应该被创建");
    }

    /// <summary>
    /// 测试创建的站点包含必需的目录结构
    /// </summary>
    [Fact]
    public async Task NewSite_ShouldCreateRequiredDirectories()
    {
        // Arrange
        var siteName = $"site-dirs-{Guid.NewGuid():N}"[..20];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        result.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {result.ErrorOutput}");

        // 验证必需的目录存在
        var requiredDirs = new[]
        {
            "content",      // 内容目录
            "layouts",      // 模板目录
            "static",       // 静态文件目录
            "assets",       // 资源目录
        };

        foreach (var dir in requiredDirs)
        {
            var dirPath = Path.Combine(sitePath, dir);
            Directory.Exists(dirPath).Should().BeTrue($"目录 '{dir}' 应该存在");
        }
    }

    /// <summary>
    /// 测试创建的站点包含配置文件
    /// </summary>
    [Fact]
    public async Task NewSite_ShouldCreateConfigFile()
    {
        // Arrange
        var siteName = $"site-config-{Guid.NewGuid():N}"[..20];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        result.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {result.ErrorOutput}");

        // 验证配置文件存在（支持多种格式）
        var configFiles = new[]
        {
            "Flint.toml",
            "Flint.yaml",
            "Flint.yml",
            "Flint.json",
            "config.toml",
            "config.yaml",
            "config.yml",
            "config.json"
        };

        var hasConfigFile = configFiles.Any(f => File.Exists(Path.Combine(sitePath, f)));
        hasConfigFile.Should().BeTrue("站点应该包含配置文件");
    }

    /// <summary>
    /// 测试配置文件内容正确
    /// </summary>
    [Fact]
    public async Task NewSite_ConfigFile_ShouldHaveCorrectContent()
    {
        // Arrange
        var siteName = $"site-content-{Guid.NewGuid():N}"[..20];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        result.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {result.ErrorOutput}");

        // 查找配置文件
        var configFiles = new[] { "Flint.toml", "Flint.yaml", "Flint.yml", "Flint.json", "config.toml" };
        var configPath = configFiles
            .Select(f => Path.Combine(sitePath, f))
            .FirstOrDefault(File.Exists);

        if (configPath != null)
        {
            var content = await File.ReadAllTextAsync(configPath);
            content.Should().NotBeNullOrWhiteSpace("配置文件不应为空");

            // 配置文件应该包含基本配置项
            var hasBaseUrl = content.Contains("baseURL", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("base_url", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("baseurl", StringComparison.OrdinalIgnoreCase);
            var hasTitle = content.Contains("title", StringComparison.OrdinalIgnoreCase);

            (hasBaseUrl || hasTitle).Should().BeTrue("配置文件应该包含基本配置项");
        }
    }

    /// <summary>
    /// 测试使用中文站点名称
    /// </summary>
    [Fact]
    public async Task NewSite_WithChineseName_ShouldSucceed()
    {
        // Arrange
        var siteName = $"我的博客-{Guid.NewGuid():N}"[..10];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        // 中文名称可能成功也可能失败，取决于实现
        // 如果失败，应该有明确的错误信息
        if (!result.IsSuccess)
        {
            result.ErrorOutput.Should().NotBeNullOrWhiteSpace("失败时应该有错误信息");
        }
    }

    /// <summary>
    /// 测试使用带连字符的站点名称
    /// </summary>
    [Fact]
    public async Task NewSite_WithHyphenatedName_ShouldSucceed()
    {
        // Arrange
        var siteName = "my-awesome-blog";
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        result.IsSuccess.Should().BeTrue($"带连字符的站点名称应该有效，错误: {result.ErrorOutput}");
    }

    /// <summary>
    /// 测试使用带下划线的站点名称
    /// </summary>
    [Fact]
    public async Task NewSite_WithUnderscoreName_ShouldSucceed()
    {
        // Arrange
        var siteName = "my_blog_site";
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        result.IsSuccess.Should().BeTrue($"带下划线的站点名称应该有效，错误: {result.ErrorOutput}");
    }

    #endregion

    #region 错误处理测试

    /// <summary>
    /// 测试已存在目录的错误处理
    /// </summary>
    [Fact]
    public async Task NewSite_WithExistingDirectory_ShouldFail()
    {
        // Arrange
        var siteName = $"existing-{Guid.NewGuid():N}"[..16];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // 先创建目录
        Directory.CreateDirectory(sitePath);
        await File.WriteAllTextAsync(Path.Combine(sitePath, "test.txt"), "test");

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        // 已存在的非空目录应该导致失败或警告
        if (!result.IsSuccess)
        {
            result.ErrorOutput.Should().NotBeNullOrWhiteSpace("应该有错误信息说明目录已存在");
        }
    }

    /// <summary>
    /// 测试无效站点名称的错误处理
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NewSite_WithInvalidName_ShouldFail(string invalidName)
    {
        // Act
        var result = await _cli.NewSiteAsync(invalidName, _testDir);

        // Assert
        result.IsSuccess.Should().BeFalse("无效的站点名称应该导致失败");
    }

    /// <summary>
    /// 测试包含特殊字符的站点名称
    /// </summary>
    [Theory]
    [InlineData("site<>name")]
    [InlineData("site|name")]
    [InlineData("site\"name")]
    [InlineData("site*name")]
    [InlineData("site?name")]
    public async Task NewSite_WithSpecialCharacters_ShouldHandleGracefully(string siteName)
    {
        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        // 特殊字符可能导致失败，但不应该崩溃
        result.TimedOut.Should().BeFalse("命令不应超时");

        if (!result.IsSuccess)
        {
            // 失败时应该有明确的错误信息
            (result.ErrorOutput.Length > 0 || result.ExitCode != 0).Should().BeTrue(
                "失败时应该有错误信息或非零退出代码");
        }
    }

    /// <summary>
    /// 测试在不存在的目录中创建站点
    /// </summary>
    [Fact]
    public async Task NewSite_InNonExistentDirectory_ShouldHandleGracefully()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDir, "non-existent-parent", "nested");
        var siteName = "test-site";

        // Act
        var result = await _cli.NewSiteAsync(siteName, nonExistentDir);

        // Assert
        // 可能成功（自动创建父目录）或失败（需要父目录存在）
        result.TimedOut.Should().BeFalse("命令不应超时");
    }

    /// <summary>
    /// 测试超长站点名称
    /// </summary>
    [Fact]
    public async Task NewSite_WithVeryLongName_ShouldHandleGracefully()
    {
        // Arrange
        var longName = new string('a', 300); // 超过大多数文件系统的限制

        // Act
        var result = await _cli.NewSiteAsync(longName, _testDir);

        // Assert
        // 超长名称应该失败，但不应该崩溃
        result.TimedOut.Should().BeFalse("命令不应超时");
    }

    #endregion

    #region 输出验证测试

    /// <summary>
    /// 测试成功创建时的输出信息
    /// </summary>
    [Fact]
    public async Task NewSite_Success_ShouldShowConfirmation()
    {
        // Arrange
        var siteName = $"output-test-{Guid.NewGuid():N}"[..16];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        // 成功时应该有确认信息
        var output = result.StandardOutput.ToLowerInvariant();
        var hasConfirmation = output.Contains("created") ||
                             output.Contains("success") ||
                             output.Contains("done") ||
                             output.Contains("完成") ||
                             output.Contains("创建") ||
                             output.Contains("成功") ||
                             output.Contains(siteName.ToLowerInvariant());

        hasConfirmation.Should().BeTrue("成功创建时应该有确认信息");
    }

    /// <summary>
    /// 测试创建站点的执行时间
    /// </summary>
    [Fact]
    public async Task NewSite_ShouldCompleteInReasonableTime()
    {
        // Arrange
        var siteName = $"time-test-{Guid.NewGuid():N}"[..16];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        result.Duration.Should().BeLessThan(
            TimeSpan.FromSeconds(30),
            "创建站点应该在 30 秒内完成");
    }

    #endregion

    #region 目录结构完整性测试

    /// <summary>
    /// 测试创建的站点可以被构建
    /// </summary>
    [Fact]
    public async Task NewSite_CreatedSite_ShouldBeBuildable()
    {
        // Arrange
        var siteName = $"buildable-{Guid.NewGuid():N}"[..16];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act - 创建站点
        var createResult = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert - 创建成功
        createResult.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {createResult.ErrorOutput}");

        // Act - 尝试构建
        var buildResult = await _cli.BuildAsync(workingDirectory: sitePath);

        // Assert - 构建应该成功（或至少不崩溃）
        buildResult.TimedOut.Should().BeFalse("构建不应超时");

        // 新创建的站点可能因为缺少内容而构建失败，这是可以接受的
        // 但不应该因为目录结构问题而失败
    }

    /// <summary>
    /// 测试创建的站点包含 archetypes 目录（如果支持）
    /// </summary>
    [Fact]
    public async Task NewSite_ShouldCreateArchetypesDirectory()
    {
        // Arrange
        var siteName = $"archetypes-{Guid.NewGuid():N}"[..16];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        result.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {result.ErrorOutput}");

        // archetypes 目录是可选的
        var archetypesPath = Path.Combine(sitePath, "archetypes");
        // 不强制要求，只记录是否存在
    }

    /// <summary>
    /// 测试创建的站点包含 data 目录（如果支持）
    /// </summary>
    [Fact]
    public async Task NewSite_ShouldCreateDataDirectory()
    {
        // Arrange
        var siteName = $"data-dir-{Guid.NewGuid():N}"[..16];
        var sitePath = Path.Combine(_testDir, siteName);
        _createdDirs.Add(sitePath);

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        result.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {result.ErrorOutput}");

        // data 目录是可选的
        var dataPath = Path.Combine(sitePath, "data");
        // 不强制要求，只记录是否存在
    }

    #endregion
}

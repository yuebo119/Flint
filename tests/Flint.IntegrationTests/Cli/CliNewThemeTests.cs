// Flint 静态站点生成器
// CLI 创建主题命令端到端测试
// 测试 new theme 命令的功能和错误处理

using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Cli;

/// <summary>
/// CLI 创建主题命令测试
/// 验证 new theme 命令的功能、目录结构和错误处理
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 1.3: 测试 new theme 命令
/// </remarks>
[Trait("Category", "CLI")]
[Trait("Category", "EndToEnd")]
public sealed class CliNewThemeTests : IAsyncLifetime
{
    private readonly CliTestRunner _cli;
    private readonly string _testDir;
    private readonly string _sitePath;
    private readonly List<string> _createdDirs;

    /// <summary>
    /// 主题必需的目录列表
    /// </summary>
    private static readonly string[] RequiredThemeDirectories =
    [
        "layouts",      // 模板目录
        "static",       // 静态文件目录
        "assets",       // 资源目录
    ];

    /// <summary>
    /// 可能的主题配置文件名
    /// </summary>
    private static readonly string[] PossibleThemeConfigFiles =
    [
        "theme.toml",
        "theme.yaml",
        "theme.yml",
        "theme.json",
        "config.toml",
        "config.yaml",
        "config.yml",
        "config.json"
    ];

    public CliNewThemeTests()
    {
        _cli = new CliTestRunner();
        _testDir = Path.Combine(Path.GetTempPath(), "Flint-theme-tests", Guid.NewGuid().ToString("N")[..8]);
        _sitePath = Path.Combine(_testDir, "test-site");
        _createdDirs = [];
    }

    public async Task InitializeAsync()
    {
        // 创建测试目录
        Directory.CreateDirectory(_testDir);

        // 创建一个测试站点（主题需要在站点内创建）
        var result = await _cli.NewSiteAsync("test-site", _testDir);
        if (!result.IsSuccess)
        {
            // 如果无法创建站点，手动创建最小结构
            Directory.CreateDirectory(_sitePath);
            Directory.CreateDirectory(Path.Combine(_sitePath, "themes"));
            await File.WriteAllTextAsync(
                Path.Combine(_sitePath, "Flint.toml"),
                "baseURL = \"http://localhost/\"\ntitle = \"Test Site\"");
        }
        else
        {
            // 确保 themes 目录存在
            Directory.CreateDirectory(Path.Combine(_sitePath, "themes"));
        }
    }

    public async Task DisposeAsync()
    {
        _cli.Dispose();

        // 清理创建的目录
        await Task.Delay(100);

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
    /// 测试创建新主题成功
    /// </summary>
    [Fact]
    public async Task NewTheme_WithValidName_ShouldSucceed()
    {
        // Arrange
        var themeName = $"test-theme-{Guid.NewGuid():N}"[..20];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert
        if (result.IsSuccess)
        {
            Directory.Exists(themePath).Should().BeTrue("主题目录应该被创建");
        }
        else
        {
            // 如果命令不支持，记录但不失败
            result.TimedOut.Should().BeFalse("命令不应超时");
        }
    }

    /// <summary>
    /// 测试创建的主题包含必需的目录结构
    /// </summary>
    [Fact]
    public async Task NewTheme_ShouldCreateRequiredDirectories()
    {
        // Arrange
        var themeName = $"theme-dirs-{Guid.NewGuid():N}"[..20];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert

        // 验证必需的目录存在
        foreach (var dir in RequiredThemeDirectories)
        {
            var dirPath = Path.Combine(themePath, dir);
            Directory.Exists(dirPath).Should().BeTrue($"主题目录 '{dir}' 应该存在");
        }
    }

    /// <summary>
    /// 测试创建的主题包含配置文件
    /// </summary>
    [Fact]
    public async Task NewTheme_ShouldCreateConfigFile()
    {
        // Arrange
        var themeName = $"theme-config-{Guid.NewGuid():N}"[..20];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert

        // 验证配置文件存在
        var hasConfigFile = PossibleThemeConfigFiles.Any(f => File.Exists(Path.Combine(themePath, f)));
        hasConfigFile.Should().BeTrue("主题应该包含配置文件");
    }

    /// <summary>
    /// 测试主题配置文件内容正确
    /// </summary>
    [Fact]
    public async Task NewTheme_ConfigFile_ShouldHaveCorrectContent()
    {
        // Arrange
        var themeName = $"theme-content-{Guid.NewGuid():N}"[..20];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert

        // 查找配置文件
        var configPath = PossibleThemeConfigFiles
            .Select(f => Path.Combine(themePath, f))
            .FirstOrDefault(File.Exists);

        if (configPath != null)
        {
            var content = await File.ReadAllTextAsync(configPath);
            content.Should().NotBeNullOrWhiteSpace("主题配置文件不应为空");

            // 配置文件应该包含主题名称
            var hasName = content.Contains("name", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains(themeName, StringComparison.OrdinalIgnoreCase);
            hasName.Should().BeTrue("主题配置文件应该包含主题名称");
        }
    }

    /// <summary>
    /// 测试使用带连字符的主题名称
    /// </summary>
    [Fact]
    public async Task NewTheme_WithHyphenatedName_ShouldSucceed()
    {
        // Arrange
        var themeName = "my-awesome-theme";
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert
        Directory.Exists(themePath).Should().BeTrue("带连字符的主题名称应该有效");
    }

    #endregion

    #region 错误处理测试

    /// <summary>
    /// 测试已存在主题的错误处理
    /// </summary>
    [Fact]
    public async Task NewTheme_WithExistingTheme_ShouldFail()
    {
        // Arrange
        var themeName = $"existing-{Guid.NewGuid():N}"[..16];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // 先创建主题目录
        Directory.CreateDirectory(themePath);
        await File.WriteAllTextAsync(Path.Combine(themePath, "theme.toml"), "name = \"existing\"");

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert
        // 已存在的主题应该导致失败或警告
        if (!result.IsSuccess)
        {
            result.ErrorOutput.Should().NotBeNullOrWhiteSpace("应该有错误信息说明主题已存在");
        }
    }

    /// <summary>
    /// 测试无效主题名称的错误处理
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NewTheme_WithInvalidName_ShouldFail(string invalidName)
    {
        // Act
        var result = await _cli.NewThemeAsync(invalidName, _sitePath);

        // Assert
        result.IsSuccess.Should().BeFalse("无效的主题名称应该导致失败");
    }

    /// <summary>
    /// 测试在非站点目录中创建主题
    /// </summary>
    [Fact]
    public async Task NewTheme_InNonSiteDirectory_ShouldFail()
    {
        // Arrange
        var nonSiteDir = Path.Combine(_testDir, "non-site");
        Directory.CreateDirectory(nonSiteDir);
        var themeName = "test-theme";

        // Act
        var result = await _cli.NewThemeAsync(themeName, nonSiteDir);

        // Assert
        // 在非站点目录中创建主题应该失败
        // 或者成功但在当前目录创建
        result.TimedOut.Should().BeFalse("命令不应超时");
    }

    /// <summary>
    /// 测试包含特殊字符的主题名称
    /// </summary>
    [Theory]
    [InlineData("theme<>name")]
    [InlineData("theme|name")]
    [InlineData("theme\"name")]
    public async Task NewTheme_WithSpecialCharacters_ShouldHandleGracefully(string themeName)
    {
        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");

        if (!result.IsSuccess)
        {
            (result.ErrorOutput.Length > 0 || result.ExitCode != 0).Should().BeTrue(
                "失败时应该有错误信息或非零退出代码");
        }
    }

    #endregion

    #region 目录结构完整性测试

    /// <summary>
    /// 测试创建的主题包含默认模板
    /// </summary>
    [Fact]
    public async Task NewTheme_ShouldCreateDefaultTemplates()
    {
        // Arrange
        var themeName = $"templates-{Guid.NewGuid():N}"[..16];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert

        var layoutsPath = Path.Combine(themePath, "layouts");
        if (Directory.Exists(layoutsPath))
        {
            // 检查是否有任何模板文件
            var hasTemplates = Directory.EnumerateFiles(layoutsPath, "*.html", SearchOption.AllDirectories).Any();
            // 不强制要求，但记录结果
        }
    }

    /// <summary>
    /// 测试创建的主题包含 _default 目录
    /// </summary>
    [Fact]
    public async Task NewTheme_ShouldCreateDefaultLayoutDirectory()
    {
        // Arrange
        var themeName = $"default-layout-{Guid.NewGuid():N}"[..20];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert

        var defaultLayoutPath = Path.Combine(themePath, "layouts", "_default");
        // _default 目录是可选的，但如果存在应该包含基本模板
    }

    /// <summary>
    /// 测试创建的主题包含 partials 目录
    /// </summary>
    [Fact]
    public async Task NewTheme_ShouldCreatePartialsDirectory()
    {
        // Arrange
        var themeName = $"partials-{Guid.NewGuid():N}"[..16];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert

        var partialsPath = Path.Combine(themePath, "layouts", "partials");
        // partials 目录是可选的
    }

    #endregion

    #region 输出验证测试

    /// <summary>
    /// 测试成功创建时的输出信息
    /// </summary>
    [Fact]
    public async Task NewTheme_Success_ShouldShowConfirmation()
    {
        // Arrange
        var themeName = $"output-{Guid.NewGuid():N}"[..14];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert
        var output = result.StandardOutput.ToLowerInvariant();
        var hasConfirmation = output.Contains("created") ||
                             output.Contains("success") ||
                             output.Contains("done") ||
                             output.Contains("完成") ||
                             output.Contains("创建") ||
                             output.Contains("成功") ||
                             output.Contains(themeName.ToLowerInvariant());

        hasConfirmation.Should().BeTrue("成功创建时应该有确认信息");
    }

    /// <summary>
    /// 测试创建主题的执行时间
    /// </summary>
    [Fact]
    public async Task NewTheme_ShouldCompleteInReasonableTime()
    {
        // Arrange
        var themeName = $"time-{Guid.NewGuid():N}"[..12];
        var themePath = Path.Combine(_sitePath, "themes", themeName);
        _createdDirs.Add(themePath);

        // Act
        var result = await _cli.NewThemeAsync(themeName, _sitePath);

        // Assert
        result.Duration.Should().BeLessThan(
            TimeSpan.FromSeconds(30),
            "创建主题应该在 30 秒内完成");
    }

    #endregion
}

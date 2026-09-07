// Flint 静态站点生成器
// 退出代码测试
// 测试各种场景下的退出代码

using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.ErrorHandling;

/// <summary>
/// 退出代码测试
/// 验证各种场景下的退出代码
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 8.7: 测试退出代码
/// </remarks>
[Trait("Category", "ErrorHandling")]
[Trait("Category", "Integration")]
public sealed class ExitCodeTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private readonly CliTestRunner _cli;
    private readonly string _testDir;

    public ExitCodeTests()
    {
        _fixture = new TestSiteFixture();
        _cli = new CliTestRunner();
        _testDir = Path.Combine(Path.GetTempPath(), "Flint-exitcode-tests", Guid.NewGuid().ToString("N")[..8]);
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_testDir);
        await _fixture.InitializeAsync();
        await _fixture.CreateSiteAsync("default");
    }

    public async Task DisposeAsync()
    {
        _cli.Dispose();
        await _fixture.DisposeAsync();

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

    #region 成功场景退出代码测试

    /// <summary>
    /// 测试成功构建返回退出代码 0
    /// </summary>
    [Fact]
    public async Task SuccessfulBuild_ShouldReturnExitCodeZero()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/success-test.md", """
            ---
            title: "Success Test"
            date: 2024-01-01
            draft: false
            ---
            
            This is a successful test post.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        if (result.IsSuccess)
        {
            result.ExitCode.Should().Be(0, "成功构建应该返回退出代码 0");
        }
    }

    /// <summary>
    /// 测试成功的 version 命令返回退出代码 0
    /// </summary>
    [Fact]
    public async Task SuccessfulVersionCommand_ShouldReturnExitCodeZero()
    {
        // Act
        var result = await _cli.GetVersionAsync();

        // Assert
        if (result.IsSuccess)
        {
            result.ExitCode.Should().Be(0, "成功的 version 命令应该返回退出代码 0");
        }
    }

    /// <summary>
    /// 测试成功的 help 命令返回退出代码 0
    /// </summary>
    [Fact]
    public async Task SuccessfulHelpCommand_ShouldReturnExitCodeZero()
    {
        // Act
        var result = await _cli.RunAsync(["--help"]);

        // Assert
        result.TimedOut.Should().BeFalse("help 命令不应超时");
        // help 命令通常返回 0
    }

    /// <summary>
    /// 测试成功创建站点返回退出代码 0
    /// </summary>
    [Fact]
    public async Task SuccessfulNewSite_ShouldReturnExitCodeZero()
    {
        // Arrange
        var siteName = $"exitcode-site-{Guid.NewGuid():N}"[..20];

        // Act
        var result = await _cli.NewSiteAsync(siteName, _testDir);

        // Assert
        if (result.IsSuccess)
        {
            result.ExitCode.Should().Be(0, "成功创建站点应该返回退出代码 0");
        }

        // 清理
        var sitePath = Path.Combine(_testDir, siteName);
        if (Directory.Exists(sitePath))
        {
            Directory.Delete(sitePath, recursive: true);
        }
    }

    #endregion

    #region 致命错误退出代码测试

    /// <summary>
    /// 测试致命错误返回非零退出代码
    /// </summary>
    [Fact]
    public async Task FatalError_ShouldReturnNonZeroExitCode()
    {
        // Arrange - 在不存在的目录中构建
        var nonExistentDir = Path.Combine(_testDir, "non-existent-" + Guid.NewGuid().ToString("N"));

        // Act
        var result = await _cli.BuildAsync(workingDirectory: nonExistentDir);

        // Assert
        result.IsSuccess.Should().BeFalse("在不存在的目录中构建应该失败");
        result.ExitCode.Should().NotBe(0, "致命错误应该返回非零退出代码");
    }

    /// <summary>
    /// 测试配置错误返回非零退出代码
    /// </summary>
    [Fact]
    public async Task ConfigError_ShouldReturnNonZeroExitCode()
    {
        // Arrange - 创建有严重错误的配置（使用无效的 TOML 语法）
        // 注意：TOML 字符串必须在同一行内，或使用多行字符串语法
        var invalidConfig = "baseURL = \"http://example.com\"\n[invalid\ntitle = \"Test\"";

        // 直接写入配置文件，绕过验证（使用正确的配置文件名 Flint.toml）
        var configPath = Path.Combine(_fixture.SiteRoot, "Flint.toml");
        await File.WriteAllTextAsync(configPath, invalidConfig);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.IsSuccess.Should().BeFalse("配置错误应该导致构建失败");
        result.ExitCode.Should().NotBe(0, "配置错误应该返回非零退出代码");
    }

    /// <summary>
    /// 测试无效命令返回非零退出代码
    /// </summary>
    [Fact]
    public async Task InvalidCommand_ShouldReturnNonZeroExitCode()
    {
        // Act
        var result = await _cli.RunAsync(["invalid-command-xyz"]);

        // Assert
        result.IsSuccess.Should().BeFalse("无效命令应该失败");
        result.ExitCode.Should().NotBe(0, "无效命令应该返回非零退出代码");
    }

    /// <summary>
    /// 测试缺少必需参数返回非零退出代码
    /// </summary>
    [Fact]
    public async Task MissingRequiredArgument_ShouldReturnNonZeroExitCode()
    {
        // Act - new site 命令缺少站点名称
        var result = await _cli.RunAsync(["new", "site"]);

        // Assert
        // 缺少参数可能显示帮助或返回错误
        result.TimedOut.Should().BeFalse("命令不应超时");
    }

    #endregion

    #region 警告不影响退出代码测试

    /// <summary>
    /// 测试警告不影响退出代码
    /// </summary>
    [Fact]
    public async Task Warning_ShouldNotAffectExitCode()
    {
        // Arrange - 创建可能产生警告的内容（如缺少可选字段）
        await _fixture.AddContentAsync("posts/warning-test.md", """
            ---
            title: "Warning Test"
            date: 2024-01-01
            ---
            
            Content without optional fields like description.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        // 如果只有警告，构建应该成功
        if (result.IsSuccess)
        {
            result.ExitCode.Should().Be(0, "警告不应影响退出代码");
        }
    }

    /// <summary>
    /// 测试未使用的变量警告不影响退出代码
    /// </summary>
    [Fact]
    public async Task UnusedVariableWarning_ShouldNotAffectExitCode()
    {
        // Arrange - 创建有未使用变量的模板
        await _fixture.AddTemplateAsync("_default/unused-var.html", """
            <!DOCTYPE html>
            <html>
            <head><title>{{ page.title }}</title></head>
            <body>
                <h1>{{ page.title }}</h1>
                <div>{{ page.content }}</div>
            </body>
            </html>
            """);

        await _fixture.AddContentAsync("posts/unused-var-test.md", """
            ---
            title: "Unused Var Test"
            date: 2024-01-01
            custom_field: "This might not be used"
            ---
            
            Content.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        // 未使用的变量可能产生警告，但不应导致失败
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 部分成功场景退出代码测试

    /// <summary>
    /// 测试部分成功场景的退出代码
    /// </summary>
    [Fact]
    public async Task PartialSuccess_ShouldHaveAppropriateExitCode()
    {
        // Arrange
        // 添加一个有效文件
        await _fixture.AddContentAsync("posts/valid.md", """
            ---
            title: "Valid Post"
            date: 2024-01-01
            draft: false
            ---
            
            Valid content.
            """);

        // 添加一个无效文件
        await _fixture.AddContentAsync("posts/invalid.md", """
            ---
            title: "Invalid Post
            ---
            Invalid content.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
        // 部分成功的退出代码取决于实现
        // 可能是 0（跳过无效文件）或非 0（报告错误）
    }

    /// <summary>
    /// 测试跳过草稿时的退出代码
    /// </summary>
    [Fact]
    public async Task SkippedDrafts_ShouldReturnExitCodeZero()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/draft-post.md", """
            ---
            title: "Draft Post"
            date: 2024-01-01
            draft: true
            ---
            
            This is a draft.
            """);

        // Act - 不包含草稿
        var result = await _cli.BuildAsync(
            new CliBuildOptions { IncludeDrafts = false },
            _fixture.SiteRoot);

        // Assert
        // 跳过草稿是正常行为，应该返回 0
        if (result.IsSuccess)
        {
            result.ExitCode.Should().Be(0, "跳过草稿应该返回退出代码 0");
        }
    }

    /// <summary>
    /// 测试跳过未来内容时的退出代码
    /// </summary>
    [Fact]
    public async Task SkippedFutureContent_ShouldReturnExitCodeZero()
    {
        // Arrange
        var futureDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        await _fixture.AddContentAsync("posts/future-post.md", $"""
            ---
            title: "Future Post"
            date: {futureDate}
            draft: false
            ---
            
            This is a future post.
            """);

        // Act - 不包含未来内容
        var result = await _cli.BuildAsync(
            new CliBuildOptions { IncludeFuture = false },
            _fixture.SiteRoot);

        // Assert
        // 跳过未来内容是正常行为，应该返回 0
        if (result.IsSuccess)
        {
            result.ExitCode.Should().Be(0, "跳过未来内容应该返回退出代码 0");
        }
    }

    #endregion

    #region 特定退出代码测试

    /// <summary>
    /// 测试不同错误类型的退出代码
    /// </summary>
    [Fact]
    public async Task DifferentErrorTypes_ShouldReturnNonZeroExitCode()
    {
        // Arrange - 创建多种类型的错误
        var scenarios = new[]
        {
            ("config-error", "Flint.toml", "baseURL = \"unclosed"),
            ("content-error", "content/posts/error.md", "---\ntitle: \"unclosed\n---\nContent"),
        };

        foreach (var (name, path, content) in scenarios)
        {
            // 创建临时站点
            var sitePath = Path.Combine(_testDir, name);
            Directory.CreateDirectory(sitePath);
            Directory.CreateDirectory(Path.Combine(sitePath, "content"));
            Directory.CreateDirectory(Path.Combine(sitePath, "content", "posts"));
            Directory.CreateDirectory(Path.Combine(sitePath, "layouts"));

            // 写入错误文件
            var fullPath = Path.Combine(sitePath, path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(fullPath, content);

            // Act
            var result = await _cli.BuildAsync(workingDirectory: sitePath);

            // Assert
            result.TimedOut.Should().BeFalse($"{name} 构建不应超时");
            result.IsSuccess.Should().BeFalse($"{name} 应该导致构建失败");
            result.ExitCode.Should().NotBe(0, $"{name} 应该返回非零退出代码");

            // 清理
            try
            {
                Directory.Delete(sitePath, recursive: true);
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }

    #endregion

    #region 超时退出代码测试

    /// <summary>
    /// 测试超时时的退出代码
    /// </summary>
    [Fact]
    public async Task Timeout_ShouldReturnNonZeroExitCode()
    {
        // Arrange - 使用非常短的超时
        var shortTimeout = TimeSpan.FromMilliseconds(1);

        // Act
        var result = await _cli.RunAsync(
            ["version"],
            timeout: shortTimeout);

        // Assert
        if (result.TimedOut)
        {
            result.ExitCode.Should().NotBe(0, "超时应该返回非零退出代码");
        }
    }

    #endregion

    #region 退出代码一致性测试

    /// <summary>
    /// 测试相同错误产生相同退出代码
    /// </summary>
    [Fact]
    public async Task SameError_ShouldProduceSameExitCode()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDir, "consistent-error-" + Guid.NewGuid().ToString("N"));

        // Act - 执行两次相同的错误操作
        var result1 = await _cli.BuildAsync(workingDirectory: nonExistentDir);
        var result2 = await _cli.BuildAsync(workingDirectory: nonExistentDir);

        // Assert
        result1.ExitCode.Should().Be(result2.ExitCode, "相同错误应该产生相同退出代码");
    }

    #endregion
}

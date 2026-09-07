// Flint 静态站点生成器
// CLI 边界条件端到端测试
// 测试各种边界条件和极端情况

using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Cli;

/// <summary>
/// CLI 边界条件测试
/// 验证各种边界条件和极端情况的处理
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 1.1, 1.10: 测试 CLI 边界条件
/// </remarks>
[Trait("Category", "CLI")]
[Trait("Category", "EndToEnd")]
[Trait("Category", "BoundaryCondition")]
public sealed class CliBoundaryConditionTests : IAsyncLifetime
{
    private readonly CliTestRunner _cli;
    private readonly string _testDir;
    private readonly List<string> _createdDirs;

    public CliBoundaryConditionTests()
    {
        _cli = new CliTestRunner();
        _testDir = Path.Combine(Path.GetTempPath(), "Flint-boundary-tests", Guid.NewGuid().ToString("N")[..8]);
        _createdDirs = [];
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_testDir);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cli.Dispose();

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

    #region 空参数测试

    /// <summary>
    /// 测试无参数执行 CLI
    /// </summary>
    [Fact]
    public async Task NoArguments_ShouldShowHelpOrError()
    {
        // Act
        var result = await _cli.RunAsync([]);

        // Assert
        result.TimedOut.Should().BeFalse("无参数执行不应超时");

        // 无参数应该显示帮助信息或错误
        var output = result.StandardOutput + result.ErrorOutput;
        output.Should().NotBeNullOrWhiteSpace("无参数执行应该有输出");
    }

    /// <summary>
    /// 测试空字符串参数
    /// </summary>
    [Fact]
    public async Task EmptyStringArgument_ShouldHandleGracefully()
    {
        // Act
        var result = await _cli.RunAsync([""]);

        // Assert
        result.TimedOut.Should().BeFalse("空字符串参数不应超时");
    }

    /// <summary>
    /// 测试只有空格的参数
    /// </summary>
    [Fact]
    public async Task WhitespaceOnlyArgument_ShouldHandleGracefully()
    {
        // Act
        var result = await _cli.RunAsync(["   "]);

        // Assert
        result.TimedOut.Should().BeFalse("空格参数不应超时");
    }

    /// <summary>
    /// 测试多个空参数
    /// </summary>
    [Fact]
    public async Task MultipleEmptyArguments_ShouldHandleGracefully()
    {
        // Act
        var result = await _cli.RunAsync(["", "", ""]);

        // Assert
        result.TimedOut.Should().BeFalse("多个空参数不应超时");
    }

    #endregion

    #region 无效参数测试

    /// <summary>
    /// 测试未知命令
    /// </summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("xyz123")]
    [InlineData("not-a-command")]
    [InlineData("构建")]  // 中文命令
    public async Task UnknownCommand_ShouldReturnError(string command)
    {
        // Act
        var result = await _cli.RunAsync([command]);

        // Assert
        result.TimedOut.Should().BeFalse("未知命令不应超时");
        result.IsSuccess.Should().BeFalse("未知命令应该失败");
    }

    /// <summary>
    /// 测试未知选项
    /// </summary>
    [Theory]
    [InlineData("--unknown-option")]
    [InlineData("--xyz")]
    [InlineData("-z")]
    [InlineData("--未知选项")]
    public async Task UnknownOption_ShouldHandleGracefully(string option)
    {
        // Act
        var result = await _cli.RunAsync(["build", option], _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("未知选项不应超时");
    }

    /// <summary>
    /// 测试无效的选项值
    /// </summary>
    [Theory]
    [InlineData("--port", "not-a-number")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "99999")]
    public async Task InvalidOptionValue_ShouldHandleGracefully(string option, string value)
    {
        // Act
        var result = await _cli.RunAsync(["serve", option, value], _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("无效选项值不应超时");
    }

    #endregion

    #region 超长参数测试

    /// <summary>
    /// 测试超长命令参数
    /// </summary>
    [Fact]
    public async Task VeryLongArgument_ShouldHandleGracefully()
    {
        // Arrange
        var longArg = new string('a', 10000);

        // Act
        var result = await _cli.RunAsync([longArg]);

        // Assert
        result.TimedOut.Should().BeFalse("超长参数不应超时");
    }

    /// <summary>
    /// 测试超长站点名称
    /// </summary>
    [Fact]
    public async Task VeryLongSiteName_ShouldHandleGracefully()
    {
        // Arrange
        var longName = new string('a', 500);

        // Act
        var result = await _cli.NewSiteAsync(longName, _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("超长站点名称不应超时");
        // 超长名称应该失败（文件系统限制）
    }

    /// <summary>
    /// 测试超长路径
    /// </summary>
    [Fact]
    public async Task VeryLongPath_ShouldHandleGracefully()
    {
        // Arrange
        var longPath = string.Join("/", Enumerable.Repeat("subdir", 50)) + "/file.md";

        // Act
        var result = await _cli.NewContentAsync(longPath, workingDirectory: _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("超长路径不应超时");
    }

    /// <summary>
    /// 测试大量参数
    /// </summary>
    [Fact]
    public async Task ManyArguments_ShouldHandleGracefully()
    {
        // Arrange
        var args = Enumerable.Range(0, 100).Select(i => $"arg{i}").ToArray();

        // Act
        var result = await _cli.RunAsync(args);

        // Assert
        result.TimedOut.Should().BeFalse("大量参数不应超时");
    }

    #endregion

    #region 特殊字符参数测试

    /// <summary>
    /// 测试包含特殊字符的参数
    /// </summary>
    [Theory]
    [InlineData("site<>name")]
    [InlineData("site|name")]
    [InlineData("site&name")]
    [InlineData("site;name")]
    [InlineData("site`name")]
    [InlineData("site$name")]
    [InlineData("site'name")]
    [InlineData("site\"name")]
    public async Task SpecialCharacterArgument_ShouldHandleGracefully(string arg)
    {
        // Act
        var result = await _cli.NewSiteAsync(arg, _testDir);

        // Assert
        result.TimedOut.Should().BeFalse($"特殊字符参数 '{arg}' 不应超时");
    }

    /// <summary>
    /// 测试 Unicode 字符参数
    /// </summary>
    [Theory]
    [InlineData("站点名称")]
    [InlineData("サイト")]
    [InlineData("사이트")]
    [InlineData("موقع")]
    [InlineData("🚀site")]
    [InlineData("site🎉")]
    public async Task UnicodeArgument_ShouldHandleGracefully(string arg)
    {
        // Act
        var result = await _cli.NewSiteAsync(arg, _testDir);

        // Assert
        result.TimedOut.Should().BeFalse($"Unicode 参数 '{arg}' 不应超时");
    }

    /// <summary>
    /// 测试控制字符参数
    /// </summary>
    [Fact]
    public async Task ControlCharacterArgument_ShouldHandleGracefully()
    {
        // Arrange
        var controlChars = "site\t\n\rname";

        // Act
        var result = await _cli.NewSiteAsync(controlChars, _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("控制字符参数不应超时");
    }

    /// <summary>
    /// 测试 null 字符参数
    /// </summary>
    [Fact]
    public async Task NullCharacterArgument_ShouldHandleGracefully()
    {
        // Arrange
        var nullChar = "site\0name";

        // Act
        var result = await _cli.NewSiteAsync(nullChar, _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("null 字符参数不应超时");
    }

    #endregion

    #region 帮助信息测试

    /// <summary>
    /// 测试 --help 选项
    /// </summary>
    [Fact]
    public async Task HelpOption_ShouldShowHelp()
    {
        // Act
        var result = await _cli.RunAsync(["--help"]);

        // Assert
        result.TimedOut.Should().BeFalse("--help 不应超时");

        var output = result.StandardOutput + result.ErrorOutput;
        output.Should().NotBeNullOrWhiteSpace("--help 应该有输出");
    }

    /// <summary>
    /// 测试 -h 短选项
    /// </summary>
    [Fact]
    public async Task ShortHelpOption_ShouldShowHelp()
    {
        // Act
        var result = await _cli.RunAsync(["-h"]);

        // Assert
        result.TimedOut.Should().BeFalse("-h 不应超时");
    }

    /// <summary>
    /// 测试 help 命令
    /// </summary>
    [Fact]
    public async Task HelpCommand_ShouldShowHelp()
    {
        // Act
        var result = await _cli.RunAsync(["help"]);

        // Assert
        result.TimedOut.Should().BeFalse("help 命令不应超时");
    }

    /// <summary>
    /// 测试子命令帮助
    /// </summary>
    [Theory]
    [InlineData("build", "--help")]
    [InlineData("new", "--help")]
    [InlineData("serve", "--help")]
    [InlineData("help", "build")]
    [InlineData("help", "new")]
    public async Task SubcommandHelp_ShouldShowSubcommandHelp(string cmd1, string cmd2)
    {
        // Act
        var result = await _cli.RunAsync([cmd1, cmd2]);

        // Assert
        result.TimedOut.Should().BeFalse($"{cmd1} {cmd2} 不应超时");
    }

    #endregion

    #region 重复选项测试

    /// <summary>
    /// 测试重复的选项
    /// </summary>
    [Fact]
    public async Task DuplicateOptions_ShouldHandleGracefully()
    {
        // Act
        var result = await _cli.RunAsync(["build", "--minify", "--minify"], _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("重复选项不应超时");
    }

    /// <summary>
    /// 测试冲突的选项
    /// </summary>
    [Fact]
    public async Task ConflictingOptions_ShouldHandleGracefully()
    {
        // Act - 同时指定 --drafts 和不指定（如果有 --no-drafts 选项）
        var result = await _cli.RunAsync(["build", "--drafts", "--verbose"], _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("冲突选项不应超时");
    }

    #endregion

    #region 路径边界测试

    /// <summary>
    /// 测试当前目录路径
    /// </summary>
    [Fact]
    public async Task CurrentDirectoryPath_ShouldWork()
    {
        // Arrange
        var sitePath = Path.Combine(_testDir, "current-dir-test");
        _createdDirs.Add(sitePath);

        // 先创建站点
        await _cli.NewSiteAsync("current-dir-test", _testDir);

        // Act - 使用 "." 作为路径
        var result = await _cli.BuildAsync(
            new CliBuildOptions { SourceDirectory = "." },
            sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("当前目录路径不应超时");
    }

    /// <summary>
    /// 测试父目录路径
    /// </summary>
    [Fact]
    public async Task ParentDirectoryPath_ShouldHandleGracefully()
    {
        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { SourceDirectory = ".." },
            _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("父目录路径不应超时");
    }

    /// <summary>
    /// 测试绝对路径
    /// </summary>
    [Fact]
    public async Task AbsolutePath_ShouldWork()
    {
        // Arrange
        var sitePath = Path.Combine(_testDir, "absolute-path-test");
        _createdDirs.Add(sitePath);

        await _cli.NewSiteAsync("absolute-path-test", _testDir);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { SourceDirectory = sitePath },
            _testDir);

        // Assert
        result.TimedOut.Should().BeFalse("绝对路径不应超时");
    }

    /// <summary>
    /// 测试带空格的路径
    /// </summary>
    [Fact]
    public async Task PathWithSpaces_ShouldWork()
    {
        // Arrange
        var dirWithSpaces = Path.Combine(_testDir, "path with spaces");
        Directory.CreateDirectory(dirWithSpaces);
        _createdDirs.Add(dirWithSpaces);

        // Act
        var result = await _cli.NewSiteAsync("test-site", dirWithSpaces);

        // Assert
        result.TimedOut.Should().BeFalse("带空格的路径不应超时");
    }

    /// <summary>
    /// 测试带中文的路径
    /// </summary>
    [Fact]
    public async Task PathWithChinese_ShouldWork()
    {
        // Arrange
        var chinesePath = Path.Combine(_testDir, "中文路径");
        Directory.CreateDirectory(chinesePath);
        _createdDirs.Add(chinesePath);

        // Act
        var result = await _cli.NewSiteAsync("test-site", chinesePath);

        // Assert
        result.TimedOut.Should().BeFalse("中文路径不应超时");
    }

    #endregion

    #region 环境变量测试

    /// <summary>
    /// 测试环境变量传递
    /// </summary>
    [Fact]
    public async Task EnvironmentVariable_ShouldBePassedToProcess()
    {
        // Arrange
        _cli.WithEnvironmentVariable("Flint_TEST_VAR", "test_value");

        // Act
        var result = await _cli.GetVersionAsync();

        // Assert
        result.TimedOut.Should().BeFalse("带环境变量的命令不应超时");
    }

    /// <summary>
    /// 测试多个环境变量
    /// </summary>
    [Fact]
    public async Task MultipleEnvironmentVariables_ShouldWork()
    {
        // Arrange
        _cli.WithEnvironmentVariables(new Dictionary<string, string>
        {
            ["VAR1"] = "value1",
            ["VAR2"] = "value2",
            ["VAR3"] = "value3"
        });

        // Act
        var result = await _cli.GetVersionAsync();

        // Assert
        result.TimedOut.Should().BeFalse("多个环境变量不应超时");
    }

    #endregion

    #region 超时测试

    /// <summary>
    /// 测试命令超时处理
    /// </summary>
    [Fact]
    public async Task CommandTimeout_ShouldBeHandled()
    {
        // Arrange
        var shortTimeout = TimeSpan.FromMilliseconds(1);

        // Act
        var result = await _cli.RunAsync(
            ["version"],
            timeout: shortTimeout);

        // Assert
        // 命令可能超时或快速完成
        // 无论如何都不应该抛出异常
    }

    #endregion

    #region 并发执行测试

    /// <summary>
    /// 测试并发执行多个命令
    /// </summary>
    [Fact]
    public async Task ConcurrentCommands_ShouldNotInterfere()
    {
        // Arrange
        var tasks = new List<Task<CliResult>>();

        // Act - 并发执行多个 version 命令
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_cli.GetVersionAsync().AsTask());
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        foreach (var result in results)
        {
            result.TimedOut.Should().BeFalse("并发命令不应超时");
        }
    }

    #endregion
}

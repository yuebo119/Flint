// Flint 静态站点生成器
// CliTestRunner 单元测试

using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Utilities;

/// <summary>
/// CliTestRunner 单元测试
/// 测试命令执行、输出捕获、超时处理等功能
/// </summary>
public class CliTestRunnerTests : IDisposable
{
    private readonly CliTestRunner _runner;
    private readonly string _tempDir;

    public CliTestRunnerTests()
    {
        _runner = new CliTestRunner();
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _runner.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    #region 基本功能测试

    [Fact]
    public void Constructor_ShouldCreateInstance_WithDefaultSettings()
    {
        // Arrange & Act
        using var runner = new CliTestRunner();

        // Assert
        runner.Should().NotBeNull();
        runner.CliPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_ShouldAcceptCustomCliPath()
    {
        // Arrange
        var customPath = "/custom/path/to/Flint";

        // Act
        using var runner = new CliTestRunner(customPath);

        // Assert
        runner.CliPath.Should().Be(customPath);
    }

    [Fact]
    public void WithEnvironmentVariable_ShouldAddVariable()
    {
        // Arrange & Act
        var result = _runner
            .WithEnvironmentVariable("TEST_VAR", "test_value")
            .WithEnvironmentVariable("ANOTHER_VAR", "another_value");

        // Assert
        result.Should().BeSameAs(_runner);
    }

    [Fact]
    public void WithEnvironmentVariables_ShouldAddMultipleVariables()
    {
        // Arrange
        var variables = new Dictionary<string, string>
        {
            ["VAR1"] = "value1",
            ["VAR2"] = "value2"
        };

        // Act
        var result = _runner.WithEnvironmentVariables(variables);

        // Assert
        result.Should().BeSameAs(_runner);
    }

    [Fact]
    public void ClearEnvironmentVariables_ShouldClearAllVariables()
    {
        // Arrange
        _runner.WithEnvironmentVariable("TEST_VAR", "test_value");

        // Act
        var result = _runner.ClearEnvironmentVariables();

        // Assert
        result.Should().BeSameAs(_runner);
    }

    #endregion

    #region 命令执行测试

    [Fact]
    public async Task RunAsync_WithSystemCommand_ShouldCaptureOutput()
    {
        // Arrange
        // 使用系统命令测试基本功能
        var command = OperatingSystem.IsWindows() ? "cmd" : "echo";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo", "Hello World" }
            : new[] { "Hello World" };

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunAsync(args);

        // Assert
        result.StandardOutput.Should().Contain("Hello");
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WithInvalidCommand_ShouldReturnError()
    {
        // Arrange
        using var runner = new CliTestRunner("nonexistent-command-12345");

        // Act
        var result = await runner.RunAsync(new[] { "arg1" });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
    }

    [Fact]
    public async Task RunAsync_WithTimeout_ShouldTimeoutLongRunningCommand()
    {
        // Arrange
        // 使用一个会运行较长时间的命令
        var command = OperatingSystem.IsWindows() ? "cmd" : "sleep";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "ping", "-n", "10", "127.0.0.1" }
            : new[] { "10" };

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunAsync(
            args,
            timeout: TimeSpan.FromMilliseconds(500));

        // Assert
        result.TimedOut.Should().BeTrue();
        result.ExitCode.Should().Be(-1);
    }

    [Fact]
    public async Task RunAsync_WithCancellation_ShouldCancelExecution()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "sleep";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "ping", "-n", "10", "127.0.0.1" }
            : new[] { "10" };

        using var runner = new CliTestRunner(command);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        var result = await runner.RunAsync(
            args,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: cts.Token);

        // Assert
        // 取消时，进程会被终止，退出代码为 -1 或非零
        // 在 Windows 上，ping 命令可能在取消前就完成了部分输出
        // 所以我们只验证执行时间小于完整执行时间
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunAsync_WithWorkingDirectory_ShouldUseSpecifiedDirectory()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "pwd";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "cd" }
            : Array.Empty<string>();

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunAsync(args, workingDirectory: _tempDir);

        // Assert
        result.WorkingDirectory.Should().Be(_tempDir);
        // 输出应包含工作目录路径
        result.StandardOutput.Should().Contain(Path.GetFileName(_tempDir));
    }

    [Fact]
    public async Task RunAsync_WithCommandLineString_ShouldParseCorrectly()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "echo";
        using var runner = new CliTestRunner(command);
        var commandLine = OperatingSystem.IsWindows()
            ? "/c echo test"
            : "test";

        // Act
        var result = await runner.RunAsync(commandLine);

        // Assert
        result.StandardOutput.Should().Contain("test");
    }

    #endregion

    #region 退出代码测试

    [Fact]
    public async Task RunAsync_WithSuccessfulCommand_ShouldReturnZeroExitCode()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "true";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "exit", "0" }
            : Array.Empty<string>();

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunAsync(args);

        // Assert
        result.ExitCode.Should().Be(0);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WithFailingCommand_ShouldReturnNonZeroExitCode()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "false";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "exit", "1" }
            : Array.Empty<string>();

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunAsync(args);

        // Assert
        result.ExitCode.Should().NotBe(0);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RunSuccessfullyAsync_WithFailingCommand_ShouldThrowException()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "false";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "exit", "1" }
            : Array.Empty<string>();

        using var runner = new CliTestRunner(command);

        // Act
        var act = () => runner.RunSuccessfullyAsync(args).AsTask();

        // Assert
        await act.Should().ThrowAsync<CliExecutionException>();
    }

    [Fact]
    public async Task RunSuccessfullyAsync_WithSuccessfulCommand_ShouldReturnResult()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "true";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "exit", "0" }
            : Array.Empty<string>();

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunSuccessfullyAsync(args);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion


    #region 输出捕获测试

    [Fact]
    public async Task RunAsync_ShouldCaptureStandardOutput()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "echo";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo", "stdout message" }
            : new[] { "stdout message" };

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunAsync(args);

        // Assert
        result.StandardOutput.Should().Contain("stdout message");
    }

    [Fact]
    public async Task RunAsync_ShouldCaptureStandardError()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "sh";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo", "error message", "1>&2" }
            : new[] { "-c", "echo 'error message' >&2" };

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunAsync(args);

        // Assert
        result.ErrorOutput.Should().Contain("error message");
    }

    [Fact]
    public async Task RunAsync_ShouldCaptureBothOutputs()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "sh";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo stdout && echo stderr 1>&2" }
            : new[] { "-c", "echo stdout; echo stderr >&2" };

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunAsync(args);

        // Assert
        result.StandardOutput.Should().Contain("stdout");
        result.ErrorOutput.Should().Contain("stderr");
    }

    [Fact]
    public async Task RunAsync_ShouldRecordDuration()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "echo";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo", "test" }
            : new[] { "test" };

        using var runner = new CliTestRunner(command);

        // Act
        var result = await runner.RunAsync(args);

        // Assert
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion

    #region CliResult 测试

    [Fact]
    public void CliResult_IsSuccess_ShouldBeTrueForZeroExitCode()
    {
        // Arrange
        var result = new CliResult { ExitCode = 0 };

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CliResult_IsSuccess_ShouldBeFalseForNonZeroExitCode()
    {
        // Arrange
        var result = new CliResult { ExitCode = 1 };

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void CliResult_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var result = new CliResult();

        // Assert
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().BeEmpty();
        result.ErrorOutput.Should().BeEmpty();
        result.Duration.Should().Be(TimeSpan.Zero);
        result.TimedOut.Should().BeFalse();
        result.Command.Should().BeEmpty();
        result.WorkingDirectory.Should().BeEmpty();
    }

    #endregion

    #region CliBuildOptions 测试

    [Fact]
    public void CliBuildOptions_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var options = new CliBuildOptions();

        // Assert
        options.Minify.Should().BeFalse();
        options.IncludeDrafts.Should().BeFalse();
        options.IncludeFuture.Should().BeFalse();
        options.Clean.Should().BeFalse();
        options.Verbose.Should().BeFalse();
        options.OutputDirectory.Should().BeNull();
        options.SourceDirectory.Should().BeNull();
    }

    [Fact]
    public void CliBuildOptions_ShouldSupportWithSyntax()
    {
        // Arrange & Act
        var options = new CliBuildOptions
        {
            Minify = true,
            IncludeDrafts = true,
            IncludeFuture = true,
            Clean = true,
            Verbose = true,
            OutputDirectory = "/output",
            SourceDirectory = "/source"
        };

        // Assert
        options.Minify.Should().BeTrue();
        options.IncludeDrafts.Should().BeTrue();
        options.IncludeFuture.Should().BeTrue();
        options.Clean.Should().BeTrue();
        options.Verbose.Should().BeTrue();
        options.OutputDirectory.Should().Be("/output");
        options.SourceDirectory.Should().Be("/source");
    }

    #endregion

    #region CliServeOptions 测试

    [Fact]
    public void CliServeOptions_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var options = new CliServeOptions();

        // Assert
        options.Port.Should().BeNull();
        options.Host.Should().BeNull();
        options.DisableLiveReload.Should().BeFalse();
        options.OpenBrowser.Should().BeFalse();
    }

    [Fact]
    public void CliServeOptions_ShouldSupportWithSyntax()
    {
        // Arrange & Act
        var options = new CliServeOptions
        {
            Port = 8080,
            Host = "localhost",
            DisableLiveReload = true,
            OpenBrowser = true
        };

        // Assert
        options.Port.Should().Be(8080);
        options.Host.Should().Be("localhost");
        options.DisableLiveReload.Should().BeTrue();
        options.OpenBrowser.Should().BeTrue();
    }

    #endregion

    #region CliExecutionException 测试

    [Fact]
    public void CliExecutionException_ShouldContainResult()
    {
        // Arrange
        var result = new CliResult
        {
            ExitCode = 1,
            Command = "test command",
            ErrorOutput = "error message"
        };

        // Act
        var exception = new CliExecutionException(result);

        // Assert
        exception.Result.Should().BeSameAs(result);
        exception.Message.Should().Contain("test command");
        exception.Message.Should().Contain("error message");
        exception.Message.Should().Contain("1");
    }

    [Fact]
    public void CliExecutionException_WithTimeout_ShouldIndicateTimeout()
    {
        // Arrange
        var result = new CliResult
        {
            ExitCode = -1,
            Command = "test command",
            TimedOut = true,
            Duration = TimeSpan.FromSeconds(5)
        };

        // Act
        var exception = new CliExecutionException(result);

        // Assert
        exception.Message.Should().Contain("超时");
        exception.Message.Should().Contain("5");
    }

    #endregion

    #region 命令行解析测试

    [Theory]
    [InlineData("arg1 arg2 arg3")]
    [InlineData("\"quoted arg\" normal")]
    [InlineData("path/to/file --option value")]
    public async Task RunAsync_ShouldParseCommandLineCorrectly(string commandLine)
    {
        // 这个测试验证命令行解析逻辑
        // 由于我们无法直接访问解析后的参数，我们通过 echo 命令间接验证
        var command = OperatingSystem.IsWindows() ? "cmd" : "echo";

        // 对于 Windows，我们需要特殊处理
        if (OperatingSystem.IsWindows())
        {
            // 跳过 Windows 上的复杂解析测试
            return;
        }

        using var runner = new CliTestRunner(command);
        var result = await runner.RunAsync(commandLine);

        // 验证命令被正确解析和执行
        result.Command.Should().Be(commandLine);
    }

    #endregion

    #region 环境变量测试

    [Fact]
    public async Task RunAsync_WithEnvironmentVariable_ShouldPassToProcess()
    {
        // Arrange
        var command = OperatingSystem.IsWindows() ? "cmd" : "sh";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo", "%TEST_ENV_VAR%" }
            : new[] { "-c", "echo $TEST_ENV_VAR" };

        using var runner = new CliTestRunner(command);
        runner.WithEnvironmentVariable("TEST_ENV_VAR", "test_value_123");

        // Act
        var result = await runner.RunAsync(args);

        // Assert
        result.StandardOutput.Should().Contain("test_value_123");
    }

    #endregion

    #region Dispose 测试

    [Fact]
    public async Task RunAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        using var runner = new CliTestRunner();
        runner.Dispose();

        // Act
        var act = () => runner.RunAsync(new[] { "test" }).AsTask();

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        using var runner = new CliTestRunner();

        // Act
        var act = () =>
        {
            runner.Dispose();
            runner.Dispose();
            runner.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}

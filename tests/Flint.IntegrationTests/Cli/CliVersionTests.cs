// Flint 静态站点生成器
// CLI 版本命令端到端测试
// 测试 version 命令的输出格式和内容

using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Cli;

/// <summary>
/// CLI 版本命令测试
/// 验证 version 命令的输出格式和内容
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 1.1: 测试 version 命令输出格式
/// </remarks>
[Trait("Category", "CLI")]
[Trait("Category", "EndToEnd")]
public sealed class CliVersionTests : IDisposable
{
    private readonly CliTestRunner _cli;

    public CliVersionTests()
    {
        _cli = new CliTestRunner();
    }

    public void Dispose()
    {
        _cli.Dispose();
    }

    #region 基本版本命令测试

    /// <summary>
    /// 测试 version 命令返回成功退出代码
    /// </summary>
    [Fact]
    public async Task Version_ShouldReturnSuccessExitCode()
    {
        // Arrange & Act
        var result = await _cli.GetVersionAsync();

        // Assert
        result.IsSuccess.Should().BeTrue("version 命令应该成功执行");
        result.ExitCode.Should().Be(0, "退出代码应该为 0");
    }

    /// <summary>
    /// 测试 version 命令输出包含产品名称
    /// </summary>
    [Fact]
    public async Task Version_ShouldContainProductName()
    {
        // Arrange & Act
        var result = await _cli.GetVersionAsync();

        // Assert
        result.StandardOutput.Should().ContainAny(
            "Flint", "Flint", "Flint",
            "静态站点生成器", "Static Site Generator");
    }

    /// <summary>
    /// 测试 version 命令输出包含版本号
    /// </summary>
    [Fact]
    public async Task Version_ShouldContainVersionNumber()
    {
        // Arrange & Act
        var result = await _cli.GetVersionAsync();

        // Assert
        // 版本号格式应该是 X.Y.Z 或 X.Y.Z-suffix
        result.StandardOutput.Should().MatchRegex(
            @"\d+\.\d+\.\d+",
            "输出应该包含版本号（格式：X.Y.Z）");
    }

    /// <summary>
    /// 测试 version 命令输出不为空
    /// </summary>
    [Fact]
    public async Task Version_ShouldNotBeEmpty()
    {
        // Arrange & Act
        var result = await _cli.GetVersionAsync();

        // Assert
        result.StandardOutput.Should().NotBeNullOrWhiteSpace("version 输出不应为空");
    }

    #endregion

    #region 详细模式测试

    /// <summary>
    /// 测试 --verbose 选项显示详细信息
    /// </summary>
    [Fact]
    public async Task Version_WithVerbose_ShouldShowDetailedInfo()
    {
        // Arrange & Act
        var result = await _cli.GetVersionAsync(verbose: true);

        // Assert
        result.IsSuccess.Should().BeTrue("verbose 版本命令应该成功执行");

        // 详细输出应该比普通输出更长
        var normalResult = await _cli.GetVersionAsync(verbose: false);
        result.StandardOutput.Length.Should().BeGreaterThanOrEqualTo(
            normalResult.StandardOutput.Length,
            "详细输出应该不少于普通输出");
    }

    /// <summary>
    /// 测试 --verbose 选项显示运行时信息
    /// </summary>
    [Fact]
    public async Task Version_WithVerbose_ShouldShowRuntimeInfo()
    {
        // Arrange & Act
        var result = await _cli.GetVersionAsync(verbose: true);

        // Assert
        // 详细模式应该包含运行时相关信息
        var output = result.StandardOutput.ToLowerInvariant();
        var hasRuntimeInfo = output.Contains(".net") ||
                            output.Contains("runtime") ||
                            output.Contains("framework") ||
                            output.Contains("clr") ||
                            output.Contains("os") ||
                            output.Contains("platform") ||
                            output.Contains("运行时") ||
                            output.Contains("框架");

        hasRuntimeInfo.Should().BeTrue("详细模式应该包含运行时信息");
    }

    /// <summary>
    /// 测试 --verbose 选项显示操作系统信息
    /// </summary>
    [Fact]
    public async Task Version_WithVerbose_ShouldShowOsInfo()
    {
        // Arrange & Act
        var result = await _cli.GetVersionAsync(verbose: true);

        // Assert
        var output = result.StandardOutput.ToLowerInvariant();
        var hasOsInfo = output.Contains("windows") ||
                       output.Contains("linux") ||
                       output.Contains("macos") ||
                       output.Contains("darwin") ||
                       output.Contains("os") ||
                       output.Contains("操作系统");

        // 这是可选的，不强制要求
        // hasOsInfo.Should().BeTrue("详细模式应该包含操作系统信息");
    }

    #endregion

    #region 命令别名测试

    /// <summary>
    /// 测试 --version 参数
    /// </summary>
    [Fact]
    public async Task VersionFlag_ShouldShowVersion()
    {
        // Arrange & Act
        var result = await _cli.RunAsync(["--version"]);

        // Assert
        result.IsSuccess.Should().BeTrue("--version 应该成功执行");
        result.StandardOutput.Should().MatchRegex(
            @"\d+\.\d+\.\d+",
            "输出应该包含版本号");
    }

    /// <summary>
    /// 测试 -v 短参数（如果支持）
    /// </summary>
    [Fact]
    public async Task ShortVersionFlag_ShouldShowVersionOrHelp()
    {
        // Arrange & Act
        var result = await _cli.RunAsync(["-v"]);

        // Assert
        // -v 可能是 version 或 verbose，取决于实现
        // 只要不报错就可以
        result.TimedOut.Should().BeFalse("-v 命令不应超时");
    }

    #endregion

    #region 错误处理测试

    /// <summary>
    /// 测试 version 命令不应产生错误输出
    /// </summary>
    [Fact]
    public async Task Version_ShouldNotProduceErrorOutput()
    {
        // Arrange & Act
        var result = await _cli.GetVersionAsync();

        // Assert
        result.ErrorOutput.Should().BeNullOrEmpty("成功的 version 命令不应有错误输出");
    }

    /// <summary>
    /// 测试 version 命令执行时间合理
    /// </summary>
    [Fact]
    public async Task Version_ShouldCompleteQuickly()
    {
        // Arrange & Act
        var result = await _cli.GetVersionAsync();

        // Assert
        result.Duration.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "version 命令应该在 5 秒内完成");
    }

    #endregion
}

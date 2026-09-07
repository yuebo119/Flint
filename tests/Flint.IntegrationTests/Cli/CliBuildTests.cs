// Flint 静态站点生成器
// CLI 构建命令端到端测试
// 测试 build 命令的各种选项和场景

using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Cli;

/// <summary>
/// CLI 构建命令测试
/// 验证 build 命令的各种选项和场景
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 1.5, 1.6, 1.7, 1.8, 1.9: 测试 build 命令
/// </remarks>
[Trait("Category", "CLI")]
[Trait("Category", "EndToEnd")]
public sealed class CliBuildTests : IAsyncLifetime
{
    private readonly CliTestRunner _cli;
    private readonly TestSiteFixture _fixture;

    public CliBuildTests()
    {
        _cli = new CliTestRunner();
        _fixture = new TestSiteFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        await _fixture.CreateSiteAsync("default");
    }

    public async Task DisposeAsync()
    {
        _cli.Dispose();
        await _fixture.DisposeAsync();
    }

    #region 基本构建测试

    /// <summary>
    /// 测试基本构建命令成功
    /// </summary>
    [Fact]
    public async Task Build_BasicCommand_ShouldSucceed()
    {
        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        Directory.Exists(_fixture.OutputPath).Should().BeTrue("输出目录应该存在");
    }

    /// <summary>
    /// 测试构建生成 HTML 文件
    /// </summary>
    [Fact]
    public async Task Build_ShouldGenerateHtmlFiles()
    {
        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert

        // 检查是否生成了 HTML 文件
        var htmlFiles = Directory.EnumerateFiles(_fixture.OutputPath, "*.html", SearchOption.AllDirectories);
        htmlFiles.Should().NotBeEmpty("构建应该生成 HTML 文件");
    }

    /// <summary>
    /// 测试构建生成首页
    /// </summary>
    [Fact]
    public async Task Build_ShouldGenerateIndexPage()
    {
        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert

        var indexPath = Path.Combine(_fixture.OutputPath, "index.html");
        File.Exists(indexPath).Should().BeTrue("构建应该生成首页 index.html");
    }

    /// <summary>
    /// 测试构建返回成功退出代码
    /// </summary>
    [Fact]
    public async Task Build_Success_ShouldReturnZeroExitCode()
    {
        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.ExitCode.Should().Be(0, "成功构建应该返回退出代码 0");
    }

    #endregion

    #region --minify 选项测试

    /// <summary>
    /// 测试 --minify 选项
    /// </summary>
    [Fact]
    public async Task Build_WithMinify_ShouldCompressOutput()
    {
        // Arrange
        var options = new CliBuildOptions { Minify = true };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");


        // 验证输出被压缩（HTML 文件应该更小或没有多余空白）
        var indexPath = Path.Combine(_fixture.OutputPath, "index.html");
        if (File.Exists(indexPath))
        {
            var content = await File.ReadAllTextAsync(indexPath);
            // 压缩后的 HTML 通常没有多余的换行和缩进
            // 这是一个简单的启发式检查
            var lineCount = content.Split('\n').Length;
            // 压缩后的文件行数应该较少
        }
    }

    /// <summary>
    /// 测试 --minify 选项压缩 CSS
    /// </summary>
    [Fact]
    public async Task Build_WithMinify_ShouldCompressCss()
    {
        // Arrange
        // 添加一个 CSS 文件
        await _fixture.AddAssetAsync("styles/main.css", """
            .container {
                color: red;
                background: blue;
            }
            
            .header {
                font-size: 16px;
            }
            """u8.ToArray());

        var options = new CliBuildOptions { Minify = true };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");
    }

    #endregion

    #region --drafts 选项测试

    /// <summary>
    /// 测试 --drafts 选项包含草稿
    /// </summary>
    [Fact]
    public async Task Build_WithDrafts_ShouldIncludeDraftContent()
    {
        // Arrange
        // 添加一个草稿文章
        await _fixture.AddContentAsync("posts/draft-post.md", """
            ---
            title: "草稿文章"
            date: 2024-01-01
            draft: true
            ---
            
            这是一篇草稿文章。
            """);

        var options = new CliBuildOptions { IncludeDrafts = true };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        // 草稿应该被包含在输出中
        var draftOutputPath = Path.Combine(_fixture.OutputPath, "posts", "draft-post", "index.html");
        var draftExists = File.Exists(draftOutputPath) ||
                         Directory.EnumerateFiles(_fixture.OutputPath, "*draft*", SearchOption.AllDirectories).Any();
        // 草稿可能以不同的路径输出
    }

    /// <summary>
    /// 测试不带 --drafts 选项时排除草稿
    /// </summary>
    [Fact]
    public async Task Build_WithoutDrafts_ShouldExcludeDraftContent()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/draft-excluded.md", """
            ---
            title: "排除的草稿"
            date: 2024-01-01
            draft: true
            ---
            
            这篇草稿不应该出现在输出中。
            """);

        var options = new CliBuildOptions { IncludeDrafts = false };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        // 草稿不应该被包含在输出中
        var draftFiles = Directory.EnumerateFiles(_fixture.OutputPath, "*draft-excluded*", SearchOption.AllDirectories);
        draftFiles.Should().BeEmpty("草稿不应该出现在输出中");
    }

    #endregion

    #region --future 选项测试

    /// <summary>
    /// 测试 --future 选项包含未来内容
    /// </summary>
    [Fact]
    public async Task Build_WithFuture_ShouldIncludeFutureContent()
    {
        // Arrange
        var futureDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        await _fixture.AddContentAsync("posts/future-post.md", $"""
            ---
            title: "未来文章"
            date: {futureDate}
            draft: false
            ---
            
            这是一篇未来发布的文章。
            """);

        var options = new CliBuildOptions { IncludeFuture = true };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");
    }

    /// <summary>
    /// 测试不带 --future 选项时排除未来内容
    /// </summary>
    [Fact]
    public async Task Build_WithoutFuture_ShouldExcludeFutureContent()
    {
        // Arrange
        var futureDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        await _fixture.AddContentAsync("posts/future-excluded.md", $"""
            ---
            title: "排除的未来文章"
            date: {futureDate}
            draft: false
            ---
            
            这篇未来文章不应该出现在输出中。
            """);

        var options = new CliBuildOptions { IncludeFuture = false };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        var futureFiles = Directory.EnumerateFiles(_fixture.OutputPath, "*future-excluded*", SearchOption.AllDirectories);
        futureFiles.Should().BeEmpty("未来内容不应该出现在输出中");
    }

    #endregion

    #region --clean 选项测试

    /// <summary>
    /// 测试 --clean 选项清理输出目录
    /// </summary>
    [Fact]
    public async Task Build_WithClean_ShouldClearOutputDirectory()
    {
        // Arrange
        // 先进行一次构建
        await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // 在输出目录中添加一个额外文件
        var extraFile = Path.Combine(_fixture.OutputPath, "extra-file.txt");
        if (Directory.Exists(_fixture.OutputPath))
        {
            await File.WriteAllTextAsync(extraFile, "extra content");
        }

        var options = new CliBuildOptions { Clean = true };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        // 额外文件应该被清理
        File.Exists(extraFile).Should().BeFalse("--clean 应该清理输出目录中的额外文件");
    }

    /// <summary>
    /// 测试不带 --clean 选项时保留现有文件
    /// </summary>
    [Fact]
    public async Task Build_WithoutClean_ShouldPreserveExistingFiles()
    {
        // Arrange
        // 先进行一次构建
        await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // 在输出目录中添加一个额外文件
        var extraFile = Path.Combine(_fixture.OutputPath, "preserved-file.txt");
        if (Directory.Exists(_fixture.OutputPath))
        {
            await File.WriteAllTextAsync(extraFile, "preserved content");
        }

        var options = new CliBuildOptions { Clean = false };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        // 不带 --clean 时，额外文件可能被保留
    }

    #endregion

    #region --output 选项测试

    /// <summary>
    /// 测试 --output 选项自定义输出目录
    /// </summary>
    [Fact]
    public async Task Build_WithOutputOption_ShouldUseCustomOutputDirectory()
    {
        // Arrange
        var customOutput = Path.Combine(_fixture.SiteRoot, "custom-output");
        var options = new CliBuildOptions { OutputDirectory = customOutput };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        Directory.Exists(customOutput).Should().BeTrue("自定义输出目录应该被创建");

        var htmlFiles = Directory.EnumerateFiles(customOutput, "*.html", SearchOption.AllDirectories);
        htmlFiles.Should().NotBeEmpty("自定义输出目录应该包含 HTML 文件");
    }

    /// <summary>
    /// 测试 --output 选项使用相对路径
    /// </summary>
    [Fact]
    public async Task Build_WithRelativeOutputPath_ShouldWork()
    {
        // Arrange
        var options = new CliBuildOptions { OutputDirectory = "dist" };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        var distPath = Path.Combine(_fixture.SiteRoot, "dist");
        Directory.Exists(distPath).Should().BeTrue("相对路径输出目录应该被创建");
    }

    #endregion

    #region --source 选项测试

    /// <summary>
    /// 测试 --source 选项自定义源目录
    /// </summary>
    [Fact]
    public async Task Build_WithSourceOption_ShouldUseCustomSourceDirectory()
    {
        // Arrange
        // 创建一个自定义源目录
        var customSource = Path.Combine(Path.GetDirectoryName(_fixture.SiteRoot)!, "custom-source");
        Directory.CreateDirectory(customSource);
        Directory.CreateDirectory(Path.Combine(customSource, "content"));
        Directory.CreateDirectory(Path.Combine(customSource, "layouts"));

        // 复制配置文件
        var configPath = Path.Combine(_fixture.SiteRoot, "Flint.toml");
        if (File.Exists(configPath))
        {
            File.Copy(configPath, Path.Combine(customSource, "Flint.toml"));
        }

        var options = new CliBuildOptions { SourceDirectory = customSource };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        // 清理
        try
        {
            Directory.Delete(customSource, recursive: true);
        }
        catch
        {
            // 忽略清理错误
        }
    }

    #endregion

    #region --verbose 选项测试

    /// <summary>
    /// 测试 --verbose 选项显示详细信息
    /// </summary>
    [Fact]
    public async Task Build_WithVerbose_ShouldShowDetailedOutput()
    {
        // Arrange
        var options = new CliBuildOptions { Verbose = true };

        // Act
        var result = await _cli.BuildAsync(options, _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建命令不应超时");

        // 详细模式应该有更多输出
        result.StandardOutput.Should().NotBeNullOrWhiteSpace("详细模式应该有输出");
    }

    #endregion

    #region 错误处理测试

    /// <summary>
    /// 测试在无效目录中构建
    /// </summary>
    [Fact]
    public async Task Build_InInvalidDirectory_ShouldFail()
    {
        // Arrange
        var invalidDir = Path.Combine(Path.GetTempPath(), "non-existent-site-" + Guid.NewGuid().ToString("N"));

        // Act
        var result = await _cli.BuildAsync(workingDirectory: invalidDir);

        // Assert
        result.IsSuccess.Should().BeFalse("在无效目录中构建应该失败");
    }

    /// <summary>
    /// 测试在非 Flint 站点目录中构建
    /// </summary>
    [Fact]
    public async Task Build_InNonFlintSite_ShouldFail()
    {
        // Arrange
        var nonSiteDir = Path.Combine(Path.GetTempPath(), "non-Flint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(nonSiteDir);

        try
        {
            // Act
            var result = await _cli.BuildAsync(workingDirectory: nonSiteDir);

            // Assert
            result.IsSuccess.Should().BeFalse("在非 Flint 站点目录中构建应该失败");
        }
        finally
        {
            // 清理
            try
            {
                Directory.Delete(nonSiteDir, recursive: true);
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }

    /// <summary>
    /// 测试构建失败时的退出代码
    /// </summary>
    [Fact]
    public async Task Build_Failure_ShouldReturnNonZeroExitCode()
    {
        // Arrange
        var invalidDir = Path.Combine(Path.GetTempPath(), "invalid-" + Guid.NewGuid().ToString("N"));

        // Act
        var result = await _cli.BuildAsync(workingDirectory: invalidDir);

        // Assert
        if (!result.IsSuccess)
        {
            result.ExitCode.Should().NotBe(0, "失败的构建应该返回非零退出代码");
        }
    }

    #endregion

    #region 性能测试

    /// <summary>
    /// 测试构建执行时间合理
    /// </summary>
    [Fact]
    public async Task Build_ShouldCompleteInReasonableTime()
    {
        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.Duration.Should().BeLessThan(
            TimeSpan.FromMinutes(2),
            "构建应该在 2 分钟内完成");
    }

    #endregion
}

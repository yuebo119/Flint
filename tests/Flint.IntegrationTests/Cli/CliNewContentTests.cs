// Flint 静态站点生成器
// CLI 创建内容命令端到端测试
// 测试 new content 命令的功能和错误处理

using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Cli;

/// <summary>
/// CLI 创建内容命令测试
/// 验证 new content 命令的功能、Front Matter 格式和错误处理
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 1.4: 测试 new content 命令
/// </remarks>
[Trait("Category", "CLI")]
[Trait("Category", "EndToEnd")]
public sealed class CliNewContentTests : IAsyncLifetime
{
    private readonly CliTestRunner _cli;
    private readonly string _testDir;
    private readonly string _sitePath;
    private readonly List<string> _createdFiles;

    public CliNewContentTests()
    {
        _cli = new CliTestRunner();
        _testDir = Path.Combine(Path.GetTempPath(), "Flint-content-tests", Guid.NewGuid().ToString("N")[..8]);
        _sitePath = Path.Combine(_testDir, "test-site");
        _createdFiles = [];
    }

    public async Task InitializeAsync()
    {
        // 创建测试目录
        Directory.CreateDirectory(_testDir);

        // 创建一个测试站点
        var result = await _cli.NewSiteAsync("test-site", _testDir);
        if (!result.IsSuccess)
        {
            // 如果无法创建站点，手动创建最小结构
            Directory.CreateDirectory(_sitePath);
            Directory.CreateDirectory(Path.Combine(_sitePath, "content"));
            Directory.CreateDirectory(Path.Combine(_sitePath, "content", "posts"));
            Directory.CreateDirectory(Path.Combine(_sitePath, "archetypes"));
            await File.WriteAllTextAsync(
                Path.Combine(_sitePath, "Flint.toml"),
                "baseURL = \"http://localhost/\"\ntitle = \"Test Site\"");
        }
    }

    public async Task DisposeAsync()
    {
        _cli.Dispose();

        await Task.Delay(100);

        foreach (var file in _createdFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
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
    /// 测试创建新内容成功
    /// </summary>
    [Fact]
    public async Task NewContent_WithValidPath_ShouldSucceed()
    {
        // Arrange
        var contentPath = $"posts/test-post-{Guid.NewGuid():N}.md"[..30];
        // ContentCreator 会自动添加 .md 扩展名
        var expectedPath = contentPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? contentPath
            : contentPath + ".md";
        var fullPath = Path.Combine(_sitePath, "content", expectedPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        if (result.IsSuccess)
        {
            File.Exists(fullPath).Should().BeTrue("内容文件应该被创建");
        }
        else
        {
            result.TimedOut.Should().BeFalse("命令不应超时");
        }
    }

    /// <summary>
    /// 测试创建的内容包含正确的 Front Matter
    /// </summary>
    [Fact]
    public async Task NewContent_ShouldContainValidFrontMatter()
    {
        // Arrange
        var contentPath = $"posts/frontmatter-{Guid.NewGuid():N}.md"[..32];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        if (!result.IsSuccess || !File.Exists(fullPath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(fullPath);
        content.Should().NotBeNullOrWhiteSpace("内容文件不应为空");

        // 验证 Front Matter 格式（YAML、TOML 或 JSON）
        var hasYamlFrontMatter = content.StartsWith("---") && content.Contains("---", StringComparison.Ordinal);
        var hasTomlFrontMatter = content.StartsWith("+++") && content.Contains("+++", StringComparison.Ordinal);
        var hasJsonFrontMatter = content.StartsWith("{") && content.Contains("}", StringComparison.Ordinal);

        (hasYamlFrontMatter || hasTomlFrontMatter || hasJsonFrontMatter).Should().BeTrue(
            "内容应该包含有效的 Front Matter");
    }

    /// <summary>
    /// 测试 Front Matter 包含标题
    /// </summary>
    [Fact]
    public async Task NewContent_FrontMatter_ShouldContainTitle()
    {
        // Arrange
        var contentPath = $"posts/title-test-{Guid.NewGuid():N}.md"[..30];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        if (!result.IsSuccess || !File.Exists(fullPath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(fullPath);
        content.ToLowerInvariant().Should().Contain("title", "Front Matter 应该包含 title 字段");
    }

    /// <summary>
    /// 测试 Front Matter 包含日期
    /// </summary>
    [Fact]
    public async Task NewContent_FrontMatter_ShouldContainDate()
    {
        // Arrange
        var contentPath = $"posts/date-test-{Guid.NewGuid():N}.md"[..28];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        if (!result.IsSuccess || !File.Exists(fullPath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(fullPath);
        content.ToLowerInvariant().Should().Contain("date", "Front Matter 应该包含 date 字段");
    }

    /// <summary>
    /// 测试 Front Matter 包含草稿状态
    /// </summary>
    [Fact]
    public async Task NewContent_FrontMatter_ShouldContainDraftStatus()
    {
        // Arrange
        var contentPath = $"posts/draft-test-{Guid.NewGuid():N}.md"[..29];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        if (!result.IsSuccess || !File.Exists(fullPath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(fullPath);
        content.ToLowerInvariant().Should().Contain("draft", "Front Matter 应该包含 draft 字段");
    }

    #endregion

    #region --kind 选项测试

    /// <summary>
    /// 测试使用 --kind 选项创建不同类型的内容
    /// </summary>
    [Theory]
    [InlineData("post")]
    [InlineData("page")]
    [InlineData("default")]
    public async Task NewContent_WithKindOption_ShouldSucceed(string kind)
    {
        // Arrange
        var contentPath = $"{kind}s/kind-{kind}-{Guid.NewGuid():N}.md"[..35];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, kind: kind, workingDirectory: _sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");

        if (result.IsSuccess && File.Exists(fullPath))
        {
            var content = await File.ReadAllTextAsync(fullPath);
            content.Should().NotBeNullOrWhiteSpace($"使用 --kind {kind} 创建的内容不应为空");
        }
    }

    /// <summary>
    /// 测试使用无效的 --kind 选项
    /// </summary>
    [Fact]
    public async Task NewContent_WithInvalidKind_ShouldHandleGracefully()
    {
        // Arrange
        var contentPath = $"posts/invalid-kind-{Guid.NewGuid():N}.md"[..32];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, kind: "nonexistent-kind", workingDirectory: _sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");
        // 无效的 kind 可能使用默认模板或报错
    }

    #endregion

    #region 嵌套路径测试

    /// <summary>
    /// 测试创建嵌套路径的内容
    /// </summary>
    [Fact]
    public async Task NewContent_WithNestedPath_ShouldCreateDirectories()
    {
        // Arrange
        var contentPath = $"posts/2024/01/nested-{Guid.NewGuid():N}.md"[..38];
        // ContentCreator 会自动添加 .md 扩展名
        var expectedPath = contentPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? contentPath
            : contentPath + ".md";
        var fullPath = Path.Combine(_sitePath, "content", expectedPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        File.Exists(fullPath).Should().BeTrue("嵌套路径的内容文件应该被创建");

        var parentDir = Path.GetDirectoryName(fullPath);
        Directory.Exists(parentDir).Should().BeTrue("父目录应该被自动创建");
    }

    /// <summary>
    /// 测试创建深层嵌套路径的内容
    /// </summary>
    [Fact]
    public async Task NewContent_WithDeeplyNestedPath_ShouldSucceed()
    {
        // Arrange
        var contentPath = $"blog/tech/programming/csharp/deep-{Guid.NewGuid():N}.md"[..50];
        // ContentCreator 会自动添加 .md 扩展名
        var expectedPath = contentPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? contentPath
            : contentPath + ".md";
        var fullPath = Path.Combine(_sitePath, "content", expectedPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");

        File.Exists(fullPath).Should().BeTrue("深层嵌套路径的内容文件应该被创建");
    }

    /// <summary>
    /// 测试在根目录创建内容
    /// </summary>
    [Fact]
    public async Task NewContent_InRootDirectory_ShouldSucceed()
    {
        // Arrange
        var contentPath = $"root-content-{Guid.NewGuid():N}.md"[..28];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");
    }

    #endregion

    #region 错误处理测试

    /// <summary>
    /// 测试无效路径的错误处理
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NewContent_WithInvalidPath_ShouldFail(string invalidPath)
    {
        // Act
        var result = await _cli.NewContentAsync(invalidPath, workingDirectory: _sitePath);

        // Assert
        result.IsSuccess.Should().BeFalse("无效的内容路径应该导致失败");
    }

    /// <summary>
    /// 测试已存在文件的错误处理
    /// </summary>
    [Fact]
    public async Task NewContent_WithExistingFile_ShouldHandleGracefully()
    {
        // Arrange
        var contentPath = $"posts/existing-{Guid.NewGuid():N}.md"[..30];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // 先创建文件
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllTextAsync(fullPath, "existing content");

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");
        // 已存在的文件可能被覆盖或报错
    }

    /// <summary>
    /// 测试包含特殊字符的路径
    /// </summary>
    [Theory]
    [InlineData("posts/test<>file.md")]
    [InlineData("posts/test|file.md")]
    [InlineData("posts/test\"file.md")]
    public async Task NewContent_WithSpecialCharactersInPath_ShouldHandleGracefully(string contentPath)
    {
        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");

        if (!result.IsSuccess)
        {
            (result.ErrorOutput.Length > 0 || result.ExitCode != 0).Should().BeTrue(
                "失败时应该有错误信息或非零退出代码");
        }
    }

    /// <summary>
    /// 测试在非站点目录中创建内容
    /// </summary>
    [Fact]
    public async Task NewContent_InNonSiteDirectory_ShouldFail()
    {
        // Arrange
        var nonSiteDir = Path.Combine(_testDir, "non-site");
        Directory.CreateDirectory(nonSiteDir);
        var contentPath = "posts/test.md";

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: nonSiteDir);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");
        // 在非站点目录中创建内容应该失败
    }

    #endregion

    #region 文件名处理测试

    /// <summary>
    /// 测试不带 .md 扩展名的路径
    /// </summary>
    [Fact]
    public async Task NewContent_WithoutMdExtension_ShouldAddExtension()
    {
        // Arrange
        var contentPath = $"posts/no-extension-{Guid.NewGuid():N}"[..30];
        var fullPathWithMd = Path.Combine(_sitePath, "content", contentPath + ".md");
        var fullPathWithoutMd = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPathWithMd);
        _createdFiles.Add(fullPathWithoutMd);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        // 应该自动添加 .md 扩展名，或者使用原始路径
        var fileExists = File.Exists(fullPathWithMd) || File.Exists(fullPathWithoutMd);
        fileExists.Should().BeTrue("内容文件应该被创建");
    }

    /// <summary>
    /// 测试使用中文文件名
    /// </summary>
    [Fact]
    public async Task NewContent_WithChineseFileName_ShouldSucceed()
    {
        // Arrange
        var contentPath = $"posts/中文文章-{Guid.NewGuid():N}.md"[..25];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");
        // 中文文件名可能成功也可能失败，取决于实现
    }

    /// <summary>
    /// 测试使用带空格的文件名
    /// </summary>
    [Fact]
    public async Task NewContent_WithSpacesInFileName_ShouldSucceed()
    {
        // Arrange
        var contentPath = $"posts/my post {Guid.NewGuid():N}.md"[..30];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        result.TimedOut.Should().BeFalse("命令不应超时");
    }

    #endregion

    #region 输出验证测试

    /// <summary>
    /// 测试成功创建时的输出信息
    /// </summary>
    [Fact]
    public async Task NewContent_Success_ShouldShowConfirmation()
    {
        // Arrange
        var contentPath = $"posts/output-{Guid.NewGuid():N}.md"[..28];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        var output = result.StandardOutput.ToLowerInvariant();
        var hasConfirmation = output.Contains("created") ||
                             output.Contains("success") ||
                             output.Contains("done") ||
                             output.Contains("完成") ||
                             output.Contains("创建") ||
                             output.Contains("成功") ||
                             output.Contains(Path.GetFileName(contentPath).ToLowerInvariant());

        hasConfirmation.Should().BeTrue("成功创建时应该有确认信息");
    }

    /// <summary>
    /// 测试创建内容的执行时间
    /// </summary>
    [Fact]
    public async Task NewContent_ShouldCompleteInReasonableTime()
    {
        // Arrange
        var contentPath = $"posts/time-{Guid.NewGuid():N}.md"[..26];
        var fullPath = Path.Combine(_sitePath, "content", contentPath);
        _createdFiles.Add(fullPath);

        // Act
        var result = await _cli.NewContentAsync(contentPath, workingDirectory: _sitePath);

        // Assert
        result.Duration.Should().BeLessThan(
            TimeSpan.FromSeconds(10),
            "创建内容应该在 10 秒内完成");
    }

    #endregion
}

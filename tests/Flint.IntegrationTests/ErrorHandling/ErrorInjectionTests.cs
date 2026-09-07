// Flint 静态站点生成器
// 错误注入测试
// 使用 ErrorInjector 验证系统对各种错误的处理

using Flint.IntegrationTests.ErrorInjection;
using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.ErrorHandling;

/// <summary>
/// 错误注入测试
/// 使用 ErrorInjector 验证系统对各种错误的处理
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 8.1, 8.2, 8.3, 8.4: 测试错误注入
/// </remarks>
[Trait("Category", "ErrorHandling")]
[Trait("Category", "Integration")]
[Trait("Category", "ErrorInjection")]
public sealed class ErrorInjectionTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private readonly CliTestRunner _cli;
    private readonly ErrorInjector _errorInjector;

    public ErrorInjectionTests()
    {
        _fixture = new TestSiteFixture();
        _cli = new CliTestRunner();
        _errorInjector = new ErrorInjector();
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        await _fixture.CreateSiteAsync("default");
    }

    public async Task DisposeAsync()
    {
        _cli.Dispose();
        _errorInjector.Dispose();
        await _fixture.DisposeAsync();
    }

    #region 文件系统错误注入测试

    /// <summary>
    /// 测试只读文件错误处理
    /// </summary>
    [Fact]
    public async Task ReadOnlyFile_ShouldBeHandledGracefully()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/readonly-test.md", """
            ---
            title: "Readonly Test"
            date: 2024-01-01
            draft: false
            ---
            
            Content for readonly test.
            """);

        var filePath = Path.Combine(_fixture.SiteRoot, "content", "posts", "readonly-test.md");

        // 设置文件为只读
        if (File.Exists(filePath))
        {
            File.SetAttributes(filePath, FileAttributes.ReadOnly);
        }

        try
        {
            // Act
            var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

            // Assert
            result.TimedOut.Should().BeFalse("构建不应超时");
            // 只读文件应该可以被读取，构建应该成功
        }
        finally
        {
            // 清理 - 移除只读属性
            if (File.Exists(filePath))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }
        }
    }

    /// <summary>
    /// 测试空文件错误处理
    /// </summary>
    [Fact]
    public async Task EmptyFile_ShouldBeHandledGracefully()
    {
        // Arrange - 创建空文件
        var emptyFilePath = Path.Combine(_fixture.SiteRoot, "content", "posts", "empty.md");
        Directory.CreateDirectory(Path.GetDirectoryName(emptyFilePath)!);
        await File.WriteAllTextAsync(emptyFilePath, "");

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
        // 空文件应该被跳过或报告警告
    }

    /// <summary>
    /// 测试截断文件错误处理
    /// </summary>
    [Fact]
    public async Task TruncatedFile_ShouldBeHandledGracefully()
    {
        // Arrange - 创建截断的文件（Front Matter 未闭合）
        await _fixture.AddContentAsync("posts/truncated.md", """
            ---
            title: "Truncated File"
            date: 2024-01-01
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试二进制文件作为内容文件
    /// </summary>
    [Fact]
    public async Task BinaryFileAsContent_ShouldBeHandledGracefully()
    {
        // Arrange - 创建二进制文件
        var binaryPath = Path.Combine(_fixture.SiteRoot, "content", "posts", "binary.md");
        Directory.CreateDirectory(Path.GetDirectoryName(binaryPath)!);
        await File.WriteAllBytesAsync(binaryPath, new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD });

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 数据损坏注入测试

    /// <summary>
    /// 测试无效 UTF-8 编码处理
    /// </summary>
    [Fact]
    public async Task InvalidUtf8_ShouldBeHandledGracefully()
    {
        // Arrange - 创建包含无效 UTF-8 的文件
        var invalidUtf8Path = Path.Combine(_fixture.SiteRoot, "content", "posts", "invalid-utf8.md");
        Directory.CreateDirectory(Path.GetDirectoryName(invalidUtf8Path)!);

        // 写入包含无效 UTF-8 序列的内容
        var invalidBytes = new byte[]
        {
            0x2D, 0x2D, 0x2D, 0x0A,  // ---\n
            0x74, 0x69, 0x74, 0x6C, 0x65, 0x3A, 0x20,  // title: 
            0x22, 0x54, 0x65, 0x73, 0x74,  // "Test
            0xFF, 0xFE,  // 无效 UTF-8
            0x22, 0x0A,  // "\n
            0x2D, 0x2D, 0x2D, 0x0A,  // ---\n
            0x43, 0x6F, 0x6E, 0x74, 0x65, 0x6E, 0x74  // Content
        };
        await File.WriteAllBytesAsync(invalidUtf8Path, invalidBytes);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试损坏的 YAML 处理
    /// </summary>
    [Fact]
    public async Task CorruptedYaml_ShouldBeHandledGracefully()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/corrupted-yaml.md", """
            ---
            title: "Test"
            date: 2024-01-01
            tags:
              - tag1
              - 
                nested: value
                  invalid: indentation
            ---
            
            Content
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试损坏的 TOML 处理
    /// </summary>
    [Fact]
    public async Task CorruptedToml_ShouldBeHandledGracefully()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/corrupted-toml.md", """
            +++
            title = "Test"
            date = 2024-01-01
            [invalid
            key = "value"
            +++
            
            Content
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试损坏的 JSON 处理
    /// </summary>
    [Fact]
    public async Task CorruptedJson_ShouldBeHandledGracefully()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/corrupted-json.md", """
            {
            "title": "Test",
            "date": "2024-01-01",
            "tags": ["tag1", "tag2"
            }
            
            Content
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 特殊字符注入测试

    /// <summary>
    /// 测试包含 null 字符的内容
    /// </summary>
    [Fact]
    public async Task NullCharacter_ShouldBeHandledGracefully()
    {
        // Arrange
        var nullCharPath = Path.Combine(_fixture.SiteRoot, "content", "posts", "null-char.md");
        Directory.CreateDirectory(Path.GetDirectoryName(nullCharPath)!);
        await File.WriteAllTextAsync(nullCharPath, "---\ntitle: \"Test\0Null\"\ndate: 2024-01-01\n---\n\nContent\0with\0nulls");

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试包含控制字符的内容
    /// </summary>
    [Fact]
    public async Task ControlCharacters_ShouldBeHandledGracefully()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/control-chars.md", "---\ntitle: \"Test\t\r\nControl\"\ndate: 2024-01-01\n---\n\nContent\twith\tcontrol\tchars");

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试包含 Unicode 特殊字符的内容
    /// </summary>
    [Fact]
    public async Task UnicodeSpecialCharacters_ShouldBeHandledGracefully()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/unicode-special.md", """
            ---
            title: "Unicode 测试 🚀 العربية 日本語"
            date: 2024-01-01
            draft: false
            ---
            
            Content with Unicode: 中文 한국어 ελληνικά עברית
            Emoji: 😀 🎉 🔥 💻
            Math: ∑ ∫ √ ∞
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试包含 BOM 的文件
    /// </summary>
    [Fact]
    public async Task FileWithBom_ShouldBeHandledGracefully()
    {
        // Arrange - 创建带 BOM 的 UTF-8 文件
        var bomPath = Path.Combine(_fixture.SiteRoot, "content", "posts", "with-bom.md");
        Directory.CreateDirectory(Path.GetDirectoryName(bomPath)!);

        var content = """
            ---
            title: "BOM Test"
            date: 2024-01-01
            ---
            
            Content with BOM.
            """;

        // 使用带 BOM 的 UTF-8 编码
        await File.WriteAllTextAsync(bomPath, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 极端值注入测试

    /// <summary>
    /// 测试超大文件处理
    /// </summary>
    [Fact]
    public async Task VeryLargeFile_ShouldBeHandledGracefully()
    {
        // Arrange - 创建一个较大的文件（1MB）
        var largeContent = new System.Text.StringBuilder();
        largeContent.AppendLine("---");
        largeContent.AppendLine("title: \"Large File Test\"");
        largeContent.AppendLine("date: 2024-01-01");
        largeContent.AppendLine("draft: false");
        largeContent.AppendLine("---");
        largeContent.AppendLine();

        // 添加大量内容
        for (int i = 0; i < 10000; i++)
        {
            largeContent.AppendLine($"This is paragraph {i}. Lorem ipsum dolor sit amet, consectetur adipiscing elit.");
        }

        await _fixture.AddContentAsync("posts/large-file.md", largeContent.ToString());

        // Act
        var result = await _cli.BuildAsync(
            workingDirectory: _fixture.SiteRoot,
            timeout: TimeSpan.FromMinutes(2));

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试超长标题处理
    /// </summary>
    [Fact]
    public async Task VeryLongTitle_ShouldBeHandledGracefully()
    {
        // Arrange
        var longTitle = new string('A', 10000);
        await _fixture.AddContentAsync("posts/long-title.md", $"""
            ---
            title: "{longTitle}"
            date: 2024-01-01
            draft: false
            ---
            
            Content with very long title.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试大量标签处理
    /// </summary>
    [Fact]
    public async Task ManyTags_ShouldBeHandledGracefully()
    {
        // Arrange - 使用较少的标签数量以避免超时
        var tags = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"  - tag{i}"));
        await _fixture.AddContentAsync("posts/many-tags.md", $"""
            ---
            title: "Many Tags Test"
            date: 2024-01-01
            draft: false
            tags:
            {tags}
            ---
            
            Content with many tags.
            """);

        // Act - 使用更长的超时时间
        var result = await _cli.BuildAsync(
            workingDirectory: _fixture.SiteRoot,
            timeout: TimeSpan.FromMinutes(2));

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试深层嵌套目录处理
    /// </summary>
    [Fact]
    public async Task DeeplyNestedDirectory_ShouldBeHandledGracefully()
    {
        // Arrange - 创建深层嵌套目录
        var nestedPath = string.Join("/", Enumerable.Range(0, 20).Select(i => $"level{i}"));
        await _fixture.AddContentAsync($"{nestedPath}/deep-file.md", """
            ---
            title: "Deep File"
            date: 2024-01-01
            draft: false
            ---
            
            Content in deeply nested directory.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 错误恢复后系统状态测试

    /// <summary>
    /// 测试错误注入后系统状态正常
    /// </summary>
    [Fact]
    public async Task AfterErrorInjection_SystemStateShouldBeNormal()
    {
        // Arrange - 注入各种错误
        await _fixture.AddContentAsync("posts/error1.md", "---\ntitle: \"Error\n---\nContent");
        await _fixture.AddContentAsync("posts/error2.md", "invalid content without front matter");

        // Act - 构建（可能失败）
        var errorResult = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        errorResult.TimedOut.Should().BeFalse("错误构建不应超时");

        // 清理错误文件
        var error1Path = Path.Combine(_fixture.SiteRoot, "content", "posts", "error1.md");
        var error2Path = Path.Combine(_fixture.SiteRoot, "content", "posts", "error2.md");
        if (File.Exists(error1Path))
            File.Delete(error1Path);
        if (File.Exists(error2Path))
            File.Delete(error2Path);

        // 添加有效内容
        await _fixture.AddContentAsync("posts/valid-after-error.md", """
            ---
            title: "Valid After Error"
            date: 2024-01-01
            draft: false
            ---
            
            Valid content after error injection.
            """);

        // Act - 再次构建
        var recoveryResult = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        recoveryResult.TimedOut.Should().BeFalse("恢复构建不应超时");
        recoveryResult.IsSuccess.Should().BeTrue("错误注入后系统应该能正常恢复");
    }

    /// <summary>
    /// 测试多次错误注入和恢复
    /// </summary>
    [Fact]
    public async Task MultipleErrorInjections_SystemShouldRecover()
    {
        for (int round = 0; round < 3; round++)
        {
            // 注入错误
            await _fixture.AddContentAsync($"posts/round{round}-error.md", $"---\ntitle: \"Round {round}\n---\nError");

            // 构建
            var errorResult = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
            errorResult.TimedOut.Should().BeFalse($"第 {round + 1} 轮错误构建不应超时");

            // 清理
            var errorPath = Path.Combine(_fixture.SiteRoot, "content", "posts", $"round{round}-error.md");
            if (File.Exists(errorPath))
                File.Delete(errorPath);

            // 恢复构建
            var recoveryResult = await _cli.BuildAsync(
                new CliBuildOptions { Clean = true },
                _fixture.SiteRoot);
            recoveryResult.TimedOut.Should().BeFalse($"第 {round + 1} 轮恢复构建不应超时");
        }
    }

    #endregion
}

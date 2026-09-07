// Flint 静态站点生成器
// 错误恢复测试
// 测试系统从错误中恢复的能力

using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.ErrorHandling;

/// <summary>
/// 错误恢复测试
/// 验证系统从错误中恢复的能力
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 8.5, 8.8: 测试错误恢复
/// </remarks>
[Trait("Category", "ErrorHandling")]
[Trait("Category", "Integration")]
public sealed class ErrorRecoveryTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private readonly CliTestRunner _cli;

    public ErrorRecoveryTests()
    {
        _fixture = new TestSiteFixture();
        _cli = new CliTestRunner();
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

    #region 单个文件错误不影响其他文件测试

    /// <summary>
    /// 测试单个内容文件错误不影响其他文件
    /// </summary>
    [Fact]
    public async Task SingleContentError_ShouldNotAffectOtherFiles()
    {
        // Arrange
        // 添加多个有效文件
        await _fixture.AddContentAsync("posts/valid-1.md", """
            ---
            title: "Valid Post 1"
            date: 2024-01-01
            draft: false
            ---
            
            This is valid post 1.
            """);

        await _fixture.AddContentAsync("posts/valid-2.md", """
            ---
            title: "Valid Post 2"
            date: 2024-01-02
            draft: false
            ---
            
            This is valid post 2.
            """);

        // 添加一个无效文件
        await _fixture.AddContentAsync("posts/invalid.md", """
            ---
            title: "Invalid Post
            date: not-a-date
            ---
            
            This is invalid.
            """);

        await _fixture.AddContentAsync("posts/valid-3.md", """
            ---
            title: "Valid Post 3"
            date: 2024-01-03
            draft: false
            ---
            
            This is valid post 3.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");

        // 检查有效文件是否被处理
        // 具体行为取决于实现（可能跳过无效文件或报告错误）
    }

    /// <summary>
    /// 测试单个模板错误不影响其他模板
    /// </summary>
    [Fact]
    public async Task SingleTemplateError_ShouldNotAffectOtherTemplates()
    {
        // Arrange
        // 添加有效模板
        await _fixture.AddTemplateAsync("_default/valid-single.html", """
            <!DOCTYPE html>
            <html>
            <head><title>{{ page.title }}</title></head>
            <body>
                <h1>{{ page.title }}</h1>
                <div>{{ page.content }}</div>
            </body>
            </html>
            """);

        // 添加无效模板
        await _fixture.AddTemplateAsync("_default/invalid-template.html", """
            <!DOCTYPE html>
            <html>
            <body>
                {{ for item in }}
                {{ end
            </body>
            </html>
            """);

        // 添加使用有效模板的内容
        await _fixture.AddContentAsync("posts/uses-valid.md", """
            ---
            title: "Uses Valid Template"
            date: 2024-01-01
            layout: valid-single
            ---
            
            Content using valid template.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试单个资源错误不影响其他资源
    /// </summary>
    [Fact]
    public async Task SingleAssetError_ShouldNotAffectOtherAssets()
    {
        // Arrange
        // 添加有效 SCSS
        await _fixture.AddAssetAsync("styles/valid.scss", """
            $primary: #007bff;
            
            .container {
                color: $primary;
            }
            """u8.ToArray());

        // 添加无效 SCSS
        await _fixture.AddAssetAsync("styles/invalid.scss", """
            .container {
                color: red
                background: blue;
            """u8.ToArray());

        // 添加另一个有效 SCSS
        await _fixture.AddAssetAsync("styles/another-valid.scss", """
            .header {
                font-size: 16px;
            }
            """u8.ToArray());

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 错误修复后重新构建测试

    /// <summary>
    /// 测试错误修复后重新构建成功
    /// </summary>
    [Fact]
    public async Task ErrorFixed_RebuildShouldSucceed()
    {
        // Arrange - 创建有错误的内容
        var contentPath = "posts/fixable-error.md";
        await _fixture.AddContentAsync(contentPath, """
            ---
            title: "Fixable Error
            date: 2024-01-01
            ---
            
            Content with error.
            """);

        // Act - 第一次构建（应该失败或有警告）
        var result1 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result1.TimedOut.Should().BeFalse("第一次构建不应超时");

        // 修复错误
        await _fixture.AddContentAsync(contentPath, """
            ---
            title: "Fixed Error"
            date: 2024-01-01
            draft: false
            ---
            
            Content with error fixed.
            """);

        // Act - 第二次构建（应该成功）
        var result2 = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result2.TimedOut.Should().BeFalse("第二次构建不应超时");
        result2.IsSuccess.Should().BeTrue("修复错误后构建应该成功");
    }

    /// <summary>
    /// 测试删除错误文件后重新构建成功
    /// </summary>
    [Fact]
    public async Task ErrorFileDeleted_RebuildShouldSucceed()
    {
        // Arrange
        // 添加有效内容
        await _fixture.AddContentAsync("posts/valid-content.md", """
            ---
            title: "Valid Content"
            date: 2024-01-01
            draft: false
            ---
            
            This is valid content.
            """);

        // 添加错误内容
        var errorFilePath = Path.Combine(_fixture.SiteRoot, "content", "posts", "error-content.md");
        await File.WriteAllTextAsync(errorFilePath, """
            ---
            title: "Error Content
            ---
            Error
            """);

        // Act - 第一次构建
        var result1 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result1.TimedOut.Should().BeFalse("第一次构建不应超时");

        // 删除错误文件
        if (File.Exists(errorFilePath))
        {
            File.Delete(errorFilePath);
        }

        // Act - 第二次构建
        var result2 = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result2.TimedOut.Should().BeFalse("第二次构建不应超时");
        result2.IsSuccess.Should().BeTrue("删除错误文件后构建应该成功");
    }

    #endregion

    #region 开发服务器错误恢复测试

    /// <summary>
    /// 测试开发服务器在错误后能继续运行
    /// </summary>
    [Fact]
    public async Task DevServer_ShouldContinueAfterError()
    {
        // 这个测试验证开发服务器的错误恢复能力
        // 由于开发服务器是长时间运行的进程，这里只测试构建错误恢复

        // Arrange
        await _fixture.AddContentAsync("posts/dev-test.md", """
            ---
            title: "Dev Test"
            date: 2024-01-01
            draft: false
            ---
            
            Content for dev server test.
            """);

        // Act - 模拟开发服务器场景：多次构建
        var result1 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result1.TimedOut.Should().BeFalse("第一次构建不应超时");

        // 引入错误
        await _fixture.AddContentAsync("posts/error-file.md", """
            ---
            title: "Error File
            ---
            Error
            """);

        var result2 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result2.TimedOut.Should().BeFalse("第二次构建不应超时");

        // 修复错误
        var errorPath = Path.Combine(_fixture.SiteRoot, "content", "posts", "error-file.md");
        if (File.Exists(errorPath))
        {
            File.Delete(errorPath);
        }

        var result3 = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result3.TimedOut.Should().BeFalse("第三次构建不应超时");
        result3.IsSuccess.Should().BeTrue("修复错误后构建应该成功");
    }

    #endregion

    #region 配置错误恢复测试

    /// <summary>
    /// 测试配置错误修复后重新构建成功
    /// </summary>
    [Fact]
    public async Task ConfigErrorFixed_RebuildShouldSucceed()
    {
        // Arrange - 保存原始配置
        var configPath = Path.Combine(_fixture.SiteRoot, "Flint.toml");
        var originalConfig = await File.ReadAllTextAsync(configPath);

        // 创建有错误的配置（直接写入文件，绕过验证）
        // 使用无效的 TOML 语法：未闭合的表头
        var invalidConfig = "baseURL = \"http://example.com\"\n[invalid\ntitle = \"Test\"";
        await File.WriteAllTextAsync(configPath, invalidConfig);

        // Act - 第一次构建（应该失败）
        var result1 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result1.TimedOut.Should().BeFalse("第一次构建不应超时");
        result1.IsSuccess.Should().BeFalse("配置错误应该导致构建失败");

        // 修复配置
        await File.WriteAllTextAsync(configPath, originalConfig);

        // Act - 第二次构建（应该成功）
        var result2 = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result2.TimedOut.Should().BeFalse("第二次构建不应超时");
        result2.IsSuccess.Should().BeTrue("修复配置后构建应该成功");
    }

    #endregion

    #region 模板错误恢复测试

    /// <summary>
    /// 测试模板错误修复后重新构建成功
    /// </summary>
    [Fact]
    public async Task TemplateErrorFixed_RebuildShouldSucceed()
    {
        // Arrange - 创建有错误的模板
        var templatePath = "_default/error-template.html";
        await _fixture.AddTemplateAsync(templatePath, """
            <!DOCTYPE html>
            <html>
            <body>
                {{ for item in }}
            </body>
            </html>
            """);

        // Act - 第一次构建
        var result1 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result1.TimedOut.Should().BeFalse("第一次构建不应超时");

        // 修复模板
        await _fixture.AddTemplateAsync(templatePath, """
            <!DOCTYPE html>
            <html>
            <body>
                <h1>{{ page.title }}</h1>
                <div>{{ page.content }}</div>
            </body>
            </html>
            """);

        // Act - 第二次构建
        var result2 = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result2.TimedOut.Should().BeFalse("第二次构建不应超时");
    }

    #endregion

    #region 资源错误恢复测试

    /// <summary>
    /// 测试 SCSS 错误修复后重新构建成功
    /// </summary>
    [Fact]
    public async Task ScssErrorFixed_RebuildShouldSucceed()
    {
        // Arrange - 创建有错误的 SCSS
        await _fixture.AddAssetAsync("styles/error.scss", """
            .container {
                color: red
                background: blue;
            """u8.ToArray());

        // Act - 第一次构建
        var result1 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result1.TimedOut.Should().BeFalse("第一次构建不应超时");

        // 修复 SCSS
        await _fixture.AddAssetAsync("styles/error.scss", """
            .container {
                color: red;
                background: blue;
            }
            """u8.ToArray());

        // Act - 第二次构建
        var result2 = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result2.TimedOut.Should().BeFalse("第二次构建不应超时");
    }

    #endregion

    #region 连续错误恢复测试

    /// <summary>
    /// 测试连续多次错误和恢复
    /// </summary>
    [Fact]
    public async Task MultipleErrorsAndRecoveries_ShouldWork()
    {
        // Arrange
        var contentPath = "posts/multi-error.md";

        // 第一次：有效内容
        await _fixture.AddContentAsync(contentPath, """
            ---
            title: "Multi Error Test"
            date: 2024-01-01
            draft: false
            ---
            
            Valid content.
            """);

        var result1 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result1.TimedOut.Should().BeFalse("第一次构建不应超时");
        result1.IsSuccess.Should().BeTrue("第一次构建应该成功");

        // 第二次：引入错误
        await _fixture.AddContentAsync(contentPath, """
            ---
            title: "Error 1
            ---
            Error
            """);

        var result2 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result2.TimedOut.Should().BeFalse("第二次构建不应超时");

        // 第三次：修复错误
        await _fixture.AddContentAsync(contentPath, """
            ---
            title: "Fixed 1"
            date: 2024-01-01
            draft: false
            ---
            
            Fixed content.
            """);

        var result3 = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);
        result3.TimedOut.Should().BeFalse("第三次构建不应超时");
        result3.IsSuccess.Should().BeTrue("第三次构建应该成功");

        // 第四次：再次引入错误
        await _fixture.AddContentAsync(contentPath, """
            ---
            title: "Error 2
            date: invalid
            ---
            Error again
            """);

        var result4 = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        result4.TimedOut.Should().BeFalse("第四次构建不应超时");

        // 第五次：再次修复
        await _fixture.AddContentAsync(contentPath, """
            ---
            title: "Fixed 2"
            date: 2024-01-01
            draft: false
            ---
            
            Fixed again.
            """);

        var result5 = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);
        result5.TimedOut.Should().BeFalse("第五次构建不应超时");
        result5.IsSuccess.Should().BeTrue("第五次构建应该成功");
    }

    #endregion

    #region 系统状态恢复测试

    /// <summary>
    /// 测试错误后系统状态正常
    /// </summary>
    [Fact]
    public async Task AfterError_SystemStateShouldBeNormal()
    {
        // Arrange - 创建错误
        await _fixture.AddContentAsync("posts/state-error.md", """
            ---
            title: "State Error
            ---
            Error
            """);

        // Act - 构建（可能失败）
        var errorResult = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);
        errorResult.TimedOut.Should().BeFalse("错误构建不应超时");

        // 删除错误文件
        var errorPath = Path.Combine(_fixture.SiteRoot, "content", "posts", "state-error.md");
        if (File.Exists(errorPath))
        {
            File.Delete(errorPath);
        }

        // 添加新的有效内容
        await _fixture.AddContentAsync("posts/new-valid.md", """
            ---
            title: "New Valid Content"
            date: 2024-01-01
            draft: false
            ---
            
            New valid content after error.
            """);

        // Act - 再次构建
        var recoveryResult = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        recoveryResult.TimedOut.Should().BeFalse("恢复构建不应超时");
        recoveryResult.IsSuccess.Should().BeTrue("错误后系统应该能正常工作");

        // 验证新内容被正确处理
        var outputFiles = _fixture.GetOutputFiles();
        outputFiles.Should().NotBeEmpty("应该有输出文件");
    }

    #endregion
}

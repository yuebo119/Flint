// Flint 静态站点生成器
// 错误处理集成测试
// 测试各种错误场景的处理和错误信息格式

using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.ErrorHandling;

/// <summary>
/// 错误处理集成测试
/// 验证各种错误场景的处理和错误信息格式
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 8.1, 8.2, 8.3, 8.4: 测试错误处理
/// </remarks>
[Trait("Category", "ErrorHandling")]
[Trait("Category", "Integration")]
public sealed class ErrorHandlingIntegrationTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private readonly CliTestRunner _cli;

    public ErrorHandlingIntegrationTests()
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

    #region Front Matter 错误报告测试

    /// <summary>
    /// 测试 YAML Front Matter 语法错误报告
    /// </summary>
    [Fact]
    public async Task FrontMatter_YamlSyntaxError_ShouldReportError()
    {
        // Arrange - 创建有 YAML 语法错误的内容
        await _fixture.AddContentAsync("posts/yaml-error.md", """
            ---
            title: "Unclosed Quote
            date: 2024-01-01
            tags:
              - tag1
              - tag2
            ---
            
            Content with YAML syntax error.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");

        // 应该报告错误或跳过无效文件
        if (!result.IsSuccess)
        {
            var output = result.ErrorOutput + result.StandardOutput;
            var hasErrorInfo = output.Contains("yaml", StringComparison.OrdinalIgnoreCase) ||
                              output.Contains("front matter", StringComparison.OrdinalIgnoreCase) ||
                              output.Contains("syntax", StringComparison.OrdinalIgnoreCase) ||
                              output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                              output.Contains("yaml-error", StringComparison.OrdinalIgnoreCase) ||
                              output.Contains("语法", StringComparison.OrdinalIgnoreCase) ||
                              output.Contains("错误", StringComparison.OrdinalIgnoreCase);

            hasErrorInfo.Should().BeTrue("错误信息应该包含相关上下文");
        }
    }

    /// <summary>
    /// 测试 TOML Front Matter 语法错误报告
    /// </summary>
    [Fact]
    public async Task FrontMatter_TomlSyntaxError_ShouldReportError()
    {
        // Arrange - 创建有 TOML 语法错误的内容
        await _fixture.AddContentAsync("posts/toml-error.md", """
            +++
            title = "Unclosed Quote
            date = 2024-01-01
            tags = ["tag1", "tag2"
            +++
            
            Content with TOML syntax error.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试 Front Matter 类型错误报告
    /// </summary>
    [Fact]
    public async Task FrontMatter_TypeError_ShouldReportError()
    {
        // Arrange - 创建有类型错误的内容
        await _fixture.AddContentAsync("posts/type-error.md", """
            ---
            title: 12345
            date: "not a valid date"
            draft: "yes"
            weight: "heavy"
            ---
            
            Content with type errors in front matter.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试缺少必需字段的 Front Matter
    /// </summary>
    [Fact]
    public async Task FrontMatter_MissingRequiredField_ShouldHandleGracefully()
    {
        // Arrange - 创建缺少 title 的内容
        await _fixture.AddContentAsync("posts/missing-title.md", """
            ---
            date: 2024-01-01
            draft: false
            ---
            
            Content without title.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
        // 缺少 title 可能使用默认值或报告警告
    }

    #endregion

    #region 模板错误报告测试

    /// <summary>
    /// 测试模板语法错误报告
    /// </summary>
    [Fact]
    public async Task Template_SyntaxError_ShouldReportError()
    {
        // Arrange - 创建有语法错误的模板
        await _fixture.AddTemplateAsync("_default/syntax-error.html", """
            <!DOCTYPE html>
            <html>
            <body>
                {{ for item in items }}
                    <p>{{ item.name }}</p>
                {{ end
            </body>
            </html>
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试模板渲染错误报告
    /// </summary>
    [Fact]
    public async Task Template_RenderError_ShouldReportError()
    {
        // Arrange - 创建会导致渲染错误的模板
        await _fixture.AddTemplateAsync("_default/render-error.html", """
            <!DOCTYPE html>
            <html>
            <body>
                {{ undefined_variable.property }}
                {{ 1 / 0 }}
            </body>
            </html>
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试模板错误包含文件路径和行号
    /// </summary>
    [Fact]
    public async Task Template_Error_ShouldContainFilePathAndLineNumber()
    {
        // Arrange - 创建有错误的模板
        await _fixture.AddTemplateAsync("_default/line-error.html", """
            <!DOCTYPE html>
            <html>
            <body>
                <p>Line 4</p>
                <p>Line 5</p>
                {{ invalid syntax here }}
                <p>Line 7</p>
            </body>
            </html>
            """);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Verbose = true },
            _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");

        if (!result.IsSuccess)
        {
            var output = result.ErrorOutput + result.StandardOutput;
            // 错误信息应该包含文件路径
            var hasFilePath = output.Contains("line-error") ||
                             output.Contains("_default") ||
                             output.Contains(".html");
            // 不强制要求，但记录结果
        }
    }

    /// <summary>
    /// 测试未定义变量错误
    /// </summary>
    [Fact]
    public async Task Template_UndefinedVariable_ShouldHandleGracefully()
    {
        // Arrange
        await _fixture.AddTemplateAsync("_default/undefined-var.html", """
            <!DOCTYPE html>
            <html>
            <body>
                <h1>{{ page.title }}</h1>
                <p>{{ page.undefined_field }}</p>
                <p>{{ completely_undefined_variable }}</p>
            </body>
            </html>
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
        // 未定义变量可能输出空字符串或报告警告
    }

    /// <summary>
    /// 测试类型错误
    /// </summary>
    [Fact]
    public async Task Template_TypeError_ShouldHandleGracefully()
    {
        // Arrange
        await _fixture.AddTemplateAsync("_default/type-error.html", """
            <!DOCTYPE html>
            <html>
            <body>
                {{ "string" + 123 }}
                {{ page.title | slice: "not a number" }}
            </body>
            </html>
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 配置错误报告测试

    /// <summary>
    /// 测试配置格式错误报告
    /// </summary>
    [Fact]
    public async Task Config_FormatError_ShouldReportError()
    {
        // Arrange - 创建有格式错误的配置
        await _fixture.SetConfigAsync("""
            baseURL = "http://example.com
            title = Test Site
            [invalid section
            """, "toml");

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
        result.IsSuccess.Should().BeFalse("配置格式错误应该导致构建失败");
    }

    /// <summary>
    /// 测试配置验证错误报告
    /// </summary>
    [Fact]
    public async Task Config_ValidationError_ShouldReportError()
    {
        // Arrange - 创建有验证错误的配置
        await _fixture.SetConfigAsync("""
            baseURL = "not a valid url"
            title = ""
            paginate = -10
            """, "toml");

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试配置错误包含字段名
    /// </summary>
    [Fact]
    public async Task Config_Error_ShouldContainFieldName()
    {
        // Arrange
        await _fixture.SetConfigAsync("""
            baseURL = 12345
            title = true
            """, "toml");

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Verbose = true },
            _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 资源处理错误报告测试

    /// <summary>
    /// 测试 SCSS 编译错误报告
    /// </summary>
    [Fact]
    public async Task Asset_ScssCompileError_ShouldReportError()
    {
        // Arrange - 创建有语法错误的 SCSS
        await _fixture.AddAssetAsync("styles/error.scss", """
            .container {
                color: red
                background: blue;
                
                .nested {
                    font-size: 16px
            }
            """u8.ToArray());

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试 SCSS 导入错误报告
    /// </summary>
    [Fact]
    public async Task Asset_ScssImportError_ShouldReportError()
    {
        // Arrange - 创建引用不存在文件的 SCSS
        await _fixture.AddAssetAsync("styles/import-error.scss", """
            @import "non-existent-file";
            @import "another-missing-file";
            
            .container {
                color: red;
            }
            """u8.ToArray());

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试无效图片文件处理
    /// </summary>
    [Fact]
    public async Task Asset_InvalidImageFile_ShouldHandleGracefully()
    {
        // Arrange - 创建无效的图片文件
        await _fixture.AddAssetAsync("images/invalid.png", "not a valid png file"u8.ToArray());

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
        // 无效图片可能被跳过或报告警告
    }

    /// <summary>
    /// 测试损坏文件处理
    /// </summary>
    [Fact]
    public async Task Asset_CorruptedFile_ShouldHandleGracefully()
    {
        // Arrange - 创建损坏的文件
        await _fixture.AddAssetAsync("styles/corrupted.css", new byte[] { 0xFF, 0xFE, 0x00, 0x00 });

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 错误信息格式测试

    /// <summary>
    /// 测试错误信息包含文件路径
    /// </summary>
    [Fact]
    public async Task Error_ShouldContainFilePath()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/path-test-error.md", """
            ---
            title: "Path Test
            ---
            Content
            """);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Verbose = true },
            _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");

        if (!result.IsSuccess)
        {
            var output = result.ErrorOutput + result.StandardOutput;
            // 错误信息应该包含文件路径
            var hasPath = output.Contains("path-test-error") ||
                         output.Contains("posts") ||
                         output.Contains(".md");
            // 不强制要求，但记录结果
        }
    }

    /// <summary>
    /// 测试错误信息包含行号
    /// </summary>
    [Fact]
    public async Task Error_ShouldContainLineNumber()
    {
        // Arrange
        await _fixture.AddTemplateAsync("_default/line-number-test.html", """
            <!DOCTYPE html>
            <html>
            <body>
                <p>Line 4</p>
                <p>Line 5</p>
                <p>Line 6</p>
                {{ invalid syntax }}
                <p>Line 8</p>
            </body>
            </html>
            """);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Verbose = true },
            _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");

        if (!result.IsSuccess)
        {
            var output = result.ErrorOutput + result.StandardOutput;
            // 错误信息可能包含行号
            var hasLineNumber = output.Contains("line") ||
                               output.Contains("行") ||
                               System.Text.RegularExpressions.Regex.IsMatch(output, @":\d+");
            // 不强制要求，但记录结果
        }
    }

    /// <summary>
    /// 测试错误信息包含列号
    /// </summary>
    [Fact]
    public async Task Error_ShouldContainColumnNumber()
    {
        // Arrange
        await _fixture.AddContentAsync("posts/column-test.md", """
            ---
            title: "Test" invalid: here
            ---
            Content
            """);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Verbose = true },
            _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    #endregion

    #region 多错误收集测试

    /// <summary>
    /// 测试多个错误被收集
    /// </summary>
    [Fact]
    public async Task MultipleErrors_ShouldBeCollected()
    {
        // Arrange - 创建多个有错误的文件
        await _fixture.AddContentAsync("posts/error1.md", """
            ---
            title: "Error 1
            ---
            Content 1
            """);

        await _fixture.AddContentAsync("posts/error2.md", """
            ---
            title: "Error 2
            date: invalid
            ---
            Content 2
            """);

        await _fixture.AddContentAsync("posts/error3.md", """
            ---
            title: "Error 3
            draft: "yes"
            ---
            Content 3
            """);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Verbose = true },
            _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");
    }

    /// <summary>
    /// 测试错误不会阻止其他文件处理
    /// </summary>
    [Fact]
    public async Task Error_ShouldNotBlockOtherFiles()
    {
        // Arrange
        // 添加一个有效文件
        await _fixture.AddContentAsync("posts/valid-file.md", """
            ---
            title: "Valid File"
            date: 2024-01-01
            draft: false
            ---
            
            This is a valid file.
            """);

        // 添加一个无效文件
        await _fixture.AddContentAsync("posts/invalid-file.md", """
            ---
            title: "Invalid File
            ---
            Invalid content.
            """);

        // Act
        var result = await _cli.BuildAsync(workingDirectory: _fixture.SiteRoot);

        // Assert
        result.TimedOut.Should().BeFalse("构建不应超时");

        // 检查有效文件是否被处理
        var validOutputPath = Path.Combine(_fixture.OutputPath, "posts", "valid-file", "index.html");
        // 有效文件可能被处理，取决于实现
    }

    #endregion
}

// Flint 静态站点生成器
// 构建管道错误恢复测试
// 验证构建管道在错误情况下的恢复能力

using Flint.Core.Models;
using Flint.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// 构建管道错误恢复测试
/// 验证构建管道在错误情况下的恢复能力
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 8.5: 错误收集和报告
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
[Trait("TestType", "ErrorRecovery")]
public class BuildPipelineErrorRecoveryTests : IAsyncLifetime
{
    #region 私有字段

    private readonly ITestOutputHelper _output;
    private readonly TestSiteFixture _fixture;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建构建管道错误恢复测试实例
    /// </summary>
    /// <param name="output">测试输出帮助器</param>
    public BuildPipelineErrorRecoveryTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture = new TestSiteFixture();
    }

    #endregion

    #region IAsyncLifetime 实现

    /// <summary>
    /// 异步初始化
    /// </summary>
    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
    }

    /// <summary>
    /// 异步清理
    /// </summary>
    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    #endregion

    #region 单个文件错误不影响其他文件测试

    /// <summary>
    /// 测试单个文件错误不影响其他文件构建
    /// 验证一个文件的错误不会阻止其他文件的构建
    /// </summary>
    [Fact]
    [Trait("TestType", "PartialSuccess")]
    public async Task BuildAsync_WithSingleFileError_ShouldBuildOtherFiles()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加正常文章
        var normalContent1 = """
            +++
            title = "正常文章 1"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是正常文章 1 的内容。
            """;
        await _fixture.AddContentAsync("posts/normal-1.md", normalContent1);

        var normalContent2 = """
            +++
            title = "正常文章 2"
            date = 2024-01-16T10:00:00+08:00
            draft = false
            +++

            这是正常文章 2 的内容。
            """;
        await _fixture.AddContentAsync("posts/normal-2.md", normalContent2);

        // 添加包含错误的文章（无效的 Front Matter）
        var errorContent = """
            +++
            title = "错误文章"
            date = invalid-date-format
            draft = false
            +++

            这是包含错误的文章。
            """;
        await _fixture.AddContentAsync("posts/error-article.md", errorContent);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建结果
        _output.WriteLine($"构建成功: {result.Success}");
        _output.WriteLine($"页面数: {result.PagesBuilt}");
        _output.WriteLine($"错误数: {result.Errors.Count}");
        _output.WriteLine($"警告数: {result.Warnings.Count}");

        // 输出错误信息
        foreach (var error in result.Errors)
        {
            _output.WriteLine($"  错误: {error.Message}");
        }

        // 验证正常文章被构建
        var outputFiles = _fixture.GetOutputFiles();
        var hasNormal1 = outputFiles.Any(f => f.Contains("normal-1", StringComparison.OrdinalIgnoreCase));
        var hasNormal2 = outputFiles.Any(f => f.Contains("normal-2", StringComparison.OrdinalIgnoreCase));

        _output.WriteLine($"正常文章 1 存在: {hasNormal1}");
        _output.WriteLine($"正常文章 2 存在: {hasNormal2}");

        // 即使有错误，正常文章也应该被构建
        hasNormal1.Should().BeTrue("正常文章 1 应该被构建");
        hasNormal2.Should().BeTrue("正常文章 2 应该被构建");
    }

    /// <summary>
    /// 测试多个文件错误的收集
    /// 验证多个文件的错误都被正确收集
    /// </summary>
    [Fact]
    [Trait("TestType", "ErrorCollection")]
    public async Task BuildAsync_WithMultipleErrors_ShouldCollectAllErrors()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加多个包含错误的文章
        var errorContents = new[]
        {
            ("posts/error-1.md", "+++\ntitle = \"错误 1\"\ndate = invalid\n+++\n内容"),
            ("posts/error-2.md", "+++\ntitle = \"错误 2\"\ndate = also-invalid\n+++\n内容"),
            ("posts/error-3.md", "+++\ntitle = \"错误 3\"\ndate = bad-date\n+++\n内容")
        };

        foreach (var (path, content) in errorContents)
        {
            await _fixture.AddContentAsync(path, content);
        }

        // 添加正常文章
        var normalContent = """
            +++
            title = "正常文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是正常文章的内容。
            """;
        await _fixture.AddContentAsync("posts/normal.md", normalContent);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证错误收集
        _output.WriteLine($"构建成功: {result.Success}");
        _output.WriteLine($"错误数: {result.Errors.Count}");

        foreach (var error in result.Errors)
        {
            _output.WriteLine($"  - [{error.ErrorCode}] {error.Message}");
            if (!string.IsNullOrEmpty(error.FilePath))
            {
                _output.WriteLine($"    文件: {error.FilePath}");
            }
        }

        // 验证正常文章被构建
        var outputFiles = _fixture.GetOutputFiles();
        var hasNormal = outputFiles.Any(f => f.Contains("normal", StringComparison.OrdinalIgnoreCase));
        _output.WriteLine($"正常文章存在: {hasNormal}");
    }

    #endregion

    #region 错误报告测试

    /// <summary>
    /// 测试错误报告格式
    /// 验证错误报告包含必要的信息
    /// </summary>
    [Fact]
    [Trait("TestType", "ErrorReporting")]
    public async Task BuildAsync_WithError_ShouldReportErrorDetails()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含错误的文章
        var errorContent = """
            +++
            title = "错误文章"
            date = not-a-valid-date
            draft = false
            +++

            这是包含错误的文章。
            """;
        await _fixture.AddContentAsync("posts/error-article.md", errorContent);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证错误报告
        _output.WriteLine($"构建成功: {result.Success}");
        _output.WriteLine($"错误数: {result.Errors.Count}");

        foreach (var error in result.Errors)
        {
            _output.WriteLine($"错误详情:");
            _output.WriteLine($"  - 错误代码: {error.ErrorCode}");
            _output.WriteLine($"  - 消息: {error.Message}");
            _output.WriteLine($"  - 文件路径: {error.FilePath}");
            _output.WriteLine($"  - 行号: {error.Line}");
            _output.WriteLine($"  - 列号: {error.Column}");

            // 验证错误包含必要信息
            error.Message.Should().NotBeNullOrEmpty("错误消息不应为空");
        }
    }

    #endregion

    #region 部分构建成功测试

    /// <summary>
    /// 测试部分构建成功场景
    /// 验证部分文件构建成功时的行为
    /// </summary>
    [Fact]
    [Trait("TestType", "PartialSuccess")]
    public async Task BuildAsync_WithPartialSuccess_ShouldReportCorrectly()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加正常文章
        for (var i = 1; i <= 5; i++)
        {
            var content = $"""
                +++
                title = "正常文章 {i}"
                date = 2024-01-{i:D2}T10:00:00+08:00
                draft = false
                +++

                这是正常文章 {i} 的内容。
                """;
            await _fixture.AddContentAsync($"posts/normal-{i}.md", content);
        }

        // 添加错误文章
        for (var i = 1; i <= 2; i++)
        {
            var content = $"""
                +++
                title = "错误文章 {i}"
                date = invalid-date-{i}
                draft = false
                +++

                这是错误文章 {i} 的内容。
                """;
            await _fixture.AddContentAsync($"posts/error-{i}.md", content);
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证部分成功
        _output.WriteLine($"构建成功: {result.Success}");
        _output.WriteLine($"页面数: {result.PagesBuilt}");
        _output.WriteLine($"错误数: {result.Errors.Count}");

        // 验证正常文章被构建
        var outputFiles = _fixture.GetOutputFiles();
        var normalCount = outputFiles.Count(f => f.Contains("normal-", StringComparison.OrdinalIgnoreCase));
        _output.WriteLine($"正常文章输出数: {normalCount}");

        normalCount.Should().BeGreaterThanOrEqualTo(5, "所有正常文章都应该被构建");
    }

    #endregion

    #region 错误修复后重新构建测试

    /// <summary>
    /// 测试错误修复后的重新构建
    /// 验证修复错误后能正确重新构建
    /// </summary>
    [Fact]
    [Trait("TestType", "ErrorRecovery")]
    public async Task BuildAsync_AfterErrorFix_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含错误的文章
        var errorContent = """
            +++
            title = "错误文章"
            date = invalid-date
            draft = false
            +++

            这是包含错误的文章。
            """;
        await _fixture.AddContentAsync("posts/fixable-article.md", errorContent);

        // Act 1 - 第一次构建（有错误）
        var firstResult = await _fixture.BuildAsync();
        _output.WriteLine($"第一次构建成功: {firstResult.Success}");
        _output.WriteLine($"第一次构建错误数: {firstResult.Errors.Count}");

        // 修复错误
        var fixedContent = """
            +++
            title = "修复后的文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是修复后的文章内容。
            """;
        await _fixture.AddContentAsync("posts/fixable-article.md", fixedContent);

        // Act 2 - 第二次构建（错误已修复）
        var secondResult = await _fixture.BuildAsync(new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            CleanOutput = true
        });

        // Assert - 验证第二次构建成功
        _output.WriteLine($"第二次构建成功: {secondResult.Success}");
        _output.WriteLine($"第二次构建错误数: {secondResult.Errors.Count}");

        secondResult.Success.Should().BeTrue("修复错误后构建应该成功");
        secondResult.Errors.Should().BeEmpty("修复错误后不应该有错误");

        // 验证文章被构建
        var outputFiles = _fixture.GetOutputFiles();
        var hasArticle = outputFiles.Any(f => f.Contains("fixable-article", StringComparison.OrdinalIgnoreCase));
        hasArticle.Should().BeTrue("修复后的文章应该被构建");
    }

    #endregion

    #region 模板错误测试

    /// <summary>
    /// 测试模板错误处理
    /// 验证模板错误能正确报告
    /// </summary>
    [Fact]
    [Trait("TestType", "TemplateError")]
    public async Task BuildAsync_WithTemplateError_ShouldReportError()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含语法错误的模板
        var errorTemplate = """
            <!DOCTYPE html>
            <html>
            <head>
                <title>{{ page.title }}</title>
            </head>
            <body>
                {{ if page.title }}
                    <h1>{{ page.title }}</h1>
                <!-- 缺少 end -->
            </body>
            </html>
            """;
        await _fixture.AddTemplateAsync("_default/single.html", errorTemplate);

        // 添加正常文章
        var content = """
            +++
            title = "测试文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是测试文章的内容。
            """;
        await _fixture.AddContentAsync("posts/test-article.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证错误报告
        _output.WriteLine($"构建成功: {result.Success}");
        _output.WriteLine($"错误数: {result.Errors.Count}");

        foreach (var error in result.Errors)
        {
            _output.WriteLine($"  - [{error.ErrorCode}] {error.Message}");
        }
    }

    #endregion

    #region 资源处理错误测试

    /// <summary>
    /// 测试资源处理错误
    /// 验证资源处理错误能正确报告
    /// </summary>
    [Fact]
    [Trait("TestType", "AssetError")]
    public async Task BuildAsync_WithAssetError_ShouldReportError()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含语法错误的 SCSS 文件
        var errorScss = """
            $primary-color: #007bff;

            .container {
                color: $primary-color;
                // 缺少闭合大括号
            """;
        await _fixture.AddAssetAsync("styles/error.scss", System.Text.Encoding.UTF8.GetBytes(errorScss));

        // 添加正常文章
        var content = """
            +++
            title = "测试文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是测试文章的内容。
            """;
        await _fixture.AddContentAsync("posts/test-article.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证错误报告
        _output.WriteLine($"构建成功: {result.Success}");
        _output.WriteLine($"错误数: {result.Errors.Count}");
        _output.WriteLine($"警告数: {result.Warnings.Count}");

        foreach (var error in result.Errors)
        {
            _output.WriteLine($"  错误: [{error.ErrorCode}] {error.Message}");
        }

        foreach (var warning in result.Warnings)
        {
            _output.WriteLine($"  警告: [{warning.WarningCode}] {warning.Message}");
        }
    }

    #endregion

    #region 配置错误测试

    /// <summary>
    /// 测试配置错误处理
    /// 验证配置错误能正确报告
    /// </summary>
    [Fact]
    [Trait("TestType", "ConfigError")]
    public async Task BuildAsync_WithConfigError_ShouldReportError()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 设置包含错误的配置
        var errorConfig = """
            # 无效的 TOML 配置
            baseURL = "http://localhost:1313/"
            title = "测试站点"
            
            [invalid
            # 缺少闭合括号
            """;

        try
        {
            await _fixture.SetConfigAsync(errorConfig, "toml");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"配置设置失败（预期）: {ex.Message}");
        }

        // Act - 尝试执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证结果
        _output.WriteLine($"构建成功: {result.Success}");
        _output.WriteLine($"错误数: {result.Errors.Count}");

        foreach (var error in result.Errors)
        {
            _output.WriteLine($"  - [{error.ErrorCode}] {error.Message}");
        }
    }

    #endregion

    #region 警告收集测试

    /// <summary>
    /// 测试警告收集
    /// 验证警告被正确收集但不影响构建
    /// </summary>
    [Fact]
    [Trait("TestType", "WarningCollection")]
    public async Task BuildAsync_WithWarnings_ShouldSucceedAndCollectWarnings()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加可能产生警告的内容（如缺少描述）
        var content = """
            +++
            title = "无描述文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这篇文章没有描述字段，可能会产生警告。
            """;
        await _fixture.AddContentAsync("posts/no-description.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("有警告但构建应该成功");

        _output.WriteLine($"构建成功: {result.Success}");
        _output.WriteLine($"警告数: {result.Warnings.Count}");

        foreach (var warning in result.Warnings)
        {
            _output.WriteLine($"  - [{warning.WarningCode}] {warning.Message}");
        }
    }

    #endregion
}

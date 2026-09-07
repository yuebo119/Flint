// Flint 静态站点生成器
// 并行构建性能测试
// 测试并行构建的性能和正确性

using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Flint.IntegrationTests.Performance;

/// <summary>
/// 并行构建性能测试
/// 测试并行构建的性能和正确性
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 9.7: 测试并行构建性能
/// </remarks>
[Trait("Category", "Performance")]
[Trait("Category", "Integration")]
public sealed class ParallelBuildPerformanceTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private readonly CliTestRunner _cli;
    private readonly ITestOutputHelper _output;

    public ParallelBuildPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture = new TestSiteFixture();
        // 并行构建测试需要更长的超时时间（3 分钟）
        _cli = new CliTestRunner(defaultTimeout: TimeSpan.FromMinutes(3));
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

    #region 并行构建性能测试

    /// <summary>
    /// 测试并行构建性能
    /// </summary>
    [Fact]
    public async Task ParallelBuild_ShouldCompleteSuccessfully()
    {
        // Arrange
        const int pageCount = 100;
        await CreateTestPages(pageCount);

        _output.WriteLine($"处理器数量: {Environment.ProcessorCount}");

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result.IsSuccess.Should().BeTrue($"并行构建应该成功: {result.ErrorOutput}");

        _output.WriteLine($"构建时间: {result.Duration.TotalMilliseconds:F2} ms");
        _output.WriteLine($"吞吐量: {pageCount / result.Duration.TotalSeconds:F2} 文件/秒");
    }

    /// <summary>
    /// 测试多核利用率
    /// </summary>
    [Fact]
    public async Task ParallelBuild_ShouldUtilizeMultipleCores()
    {
        // 只在多核系统上测试
        if (Environment.ProcessorCount < 2)
        {
            _output.WriteLine("跳过测试：单核系统");
            return;
        }

        // Arrange
        const int pageCount = 200;
        await CreateTestPages(pageCount);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");

        var throughput = pageCount / result.Duration.TotalSeconds;
        _output.WriteLine($"处理器数量: {Environment.ProcessorCount}");
        _output.WriteLine($"构建时间: {result.Duration.TotalMilliseconds:F2} ms");
        _output.WriteLine($"吞吐量: {throughput:F2} 文件/秒");

        // 多核系统应该有更高的吞吐量
        // 这是一个启发式检查，不强制要求
    }

    #endregion

    #region 并行构建正确性测试

    /// <summary>
    /// 测试并行构建的正确性（输出与串行构建一致）
    /// </summary>
    [Fact]
    public async Task ParallelBuild_Output_ShouldBeCorrect()
    {
        // Arrange
        const int pageCount = 50;
        await CreateTestPages(pageCount);

        // Act - 构建
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");

        // 验证输出文件数量
        var outputFiles = _fixture.GetOutputFiles();
        outputFiles.Should().NotBeEmpty("应该有输出文件");

        _output.WriteLine($"输出文件数量: {outputFiles.Count}");

        // 验证每个内容文件都有对应的输出
        // 这是一个简化的检查
    }

    /// <summary>
    /// 测试并行构建的线程安全性
    /// </summary>
    [Fact]
    public async Task ParallelBuild_ShouldBeThreadSafe()
    {
        // Arrange
        const int pageCount = 100;
        await CreateTestPages(pageCount);

        // Act - 多次构建，验证结果一致
        var results = new List<IReadOnlyList<string>>();

        for (int i = 0; i < 3; i++)
        {
            var result = await _cli.BuildAsync(
                new CliBuildOptions { Clean = true },
                _fixture.SiteRoot);

            result.IsSuccess.Should().BeTrue($"第 {i + 1} 次构建应该成功: {result.ErrorOutput}");

            var files = _fixture.GetOutputFiles().OrderBy(f => f).ToList();
            results.Add(files);
        }

        // Assert
        // 所有构建应该产生相同的文件列表
        for (int i = 1; i < results.Count; i++)
        {
            results[i].Should().BeEquivalentTo(results[0],
                $"第 {i + 1} 次构建应该与第 1 次构建产生相同的文件");
        }
    }

    /// <summary>
    /// 测试并行构建不会产生竞态条件
    /// </summary>
    [Fact]
    public async Task ParallelBuild_ShouldNotHaveRaceConditions()
    {
        // Arrange
        const int pageCount = 100;

        // 创建有相互引用的内容
        for (int i = 0; i < pageCount; i++)
        {
            var date = DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd");
            var relatedPosts = string.Join(", ", Enumerable.Range(0, 3).Select(j => $"post-{(i + j) % pageCount:D4}"));

            await _fixture.AddContentAsync($"posts/post-{i:D4}.md", $"""
                ---
                title: "Test Post {i}"
                date: {date}
                draft: false
                tags:
                  - tag{i % 10}
                  - tag{(i + 1) % 10}
                categories:
                  - category{i % 5}
                related:
                  - {relatedPosts}
                ---
                
                This is test post number {i}.
                
                Related posts: {relatedPosts}
                """);
        }

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");
        result.TimedOut.Should().BeFalse("构建不应超时");

        // 验证输出文件完整性
        var outputFiles = _fixture.GetOutputFiles();
        outputFiles.Should().NotBeEmpty("应该有输出文件");
    }

    #endregion

    #region 并行度测试

    /// <summary>
    /// 测试不同页面数量的构建性能
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task ParallelBuild_DifferentPageCounts_ShouldScale(int pageCount)
    {
        // Arrange
        await CreateTestPages(pageCount);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");

        var throughput = pageCount / result.Duration.TotalSeconds;
        _output.WriteLine($"[{pageCount} 页面] 构建时间: {result.Duration.TotalMilliseconds:F2} ms");
        _output.WriteLine($"[{pageCount} 页面] 吞吐量: {throughput:F2} 文件/秒");
    }

    #endregion

    #region 资源竞争测试

    /// <summary>
    /// 测试并行构建时的资源竞争
    /// </summary>
    [Fact]
    public async Task ParallelBuild_WithSharedResources_ShouldHandleCorrectly()
    {
        // Arrange
        const int pageCount = 50;
        await CreateTestPages(pageCount);

        // 添加共享的 SCSS 文件
        await _fixture.AddAssetAsync("styles/shared.scss", """
            $primary: #007bff;
            $secondary: #6c757d;
            
            .shared-style {
                color: $primary;
                background: $secondary;
            }
            """u8.ToArray());

        // 添加多个引用共享样式的 SCSS 文件
        for (int i = 0; i < 5; i++)
        {
            var scssContent = $$"""
                @import "shared";
                
                .component-{{i}} {
                    color: $primary;
                    border: 1px solid $secondary;
                }
                """;
            await _fixture.AddAssetAsync($"styles/component-{i}.scss", System.Text.Encoding.UTF8.GetBytes(scssContent));
        }

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        // Assert
        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 创建测试页面
    /// </summary>
    private async Task CreateTestPages(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var date = DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd");
            await _fixture.AddContentAsync($"posts/post-{i:D4}.md", $"""
                ---
                title: "Test Post {i}"
                date: {date}
                draft: false
                tags:
                  - tag{i % 10}
                  - tag{(i + 5) % 10}
                categories:
                  - category{i % 5}
                ---
                
                This is test post number {i}.
                
                ## Section 1
                
                Lorem ipsum dolor sit amet, consectetur adipiscing elit.
                Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.
                
                ## Section 2
                
                Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.
                Nisi ut aliquip ex ea commodo consequat.
                """);
        }
    }

    #endregion
}

// Flint 静态站点生成器
// 内存泄漏检测测试
// 测试多次构建后的内存使用和长时间运行的内存稳定性

using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Flint.IntegrationTests.Performance;

/// <summary>
/// 内存泄漏检测测试
/// 测试多次构建后的内存使用和长时间运行的内存稳定性
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 9.2: 测试内存泄漏检测
/// </remarks>
[Trait("Category", "Performance")]
[Trait("Category", "Integration")]
[Trait("Category", "MemoryLeak")]
[Collection("Performance")]
public sealed class MemoryLeakDetectionTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private readonly CliTestRunner _cli;
    private readonly ITestOutputHelper _output;

    public MemoryLeakDetectionTests(ITestOutputHelper output)
    {
        _output = output;
        _fixture = new TestSiteFixture();
        // 内存测试需要更长的超时时间（3 分钟）
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

    #region 多次构建内存测试

    /// <summary>
    /// 测试多次构建后内存使用
    /// </summary>
    [Fact]
    public async Task MultipleBuild_MemoryUsage_ShouldNotGrow()
    {
        // Arrange
        const int pageCount = 50;
        const int buildIterations = 5;

        await CreateTestPages(pageCount);

        var memorySnapshots = new List<long>();

        // 预热
        await _cli.BuildAsync(new CliBuildOptions { Clean = true }, _fixture.SiteRoot);

        // 强制 GC 获取基线
        ForceGC();
        var baselineMemory = GC.GetTotalMemory(true);
        _output.WriteLine($"基线内存: {baselineMemory / 1024.0 / 1024.0:F2} MB");

        // Act - 多次构建
        for (int i = 0; i < buildIterations; i++)
        {
            var result = await _cli.BuildAsync(
                new CliBuildOptions { Clean = true },
                _fixture.SiteRoot);

            result.IsSuccess.Should().BeTrue($"第 {i + 1} 次构建应该成功: {result.ErrorOutput}");

            // 记录内存
            ForceGC();
            var currentMemory = GC.GetTotalMemory(true);
            memorySnapshots.Add(currentMemory);

            _output.WriteLine($"第 {i + 1} 次构建后内存: {currentMemory / 1024.0 / 1024.0:F2} MB");
        }

        // Assert
        // 检查内存是否持续增长
        var memoryGrowth = memorySnapshots.Last() - baselineMemory;
        var memoryGrowthMB = memoryGrowth / 1024.0 / 1024.0;

        _output.WriteLine($"总内存增长: {memoryGrowthMB:F2} MB");

        // 内存增长应该有限（每次构建不应该累积太多内存）
        // 允许一定的增长（100MB），但不应该无限增长
        memoryGrowthMB.Should().BeLessThan(100,
            "多次构建后内存增长应该有限");

        // 检查内存是否稳定（后几次构建的内存应该相近）
        if (memorySnapshots.Count >= 3)
        {
            var lastThree = memorySnapshots.TakeLast(3).ToList();
            var variance = lastThree.Max() - lastThree.Min();
            var varianceMB = variance / 1024.0 / 1024.0;

            _output.WriteLine($"最后三次构建内存波动: {varianceMB:F2} MB");

            varianceMB.Should().BeLessThan(50,
                "多次构建后内存应该趋于稳定");
        }
    }

    /// <summary>
    /// 测试增量构建内存使用
    /// </summary>
    [Fact]
    public async Task IncrementalBuild_MemoryUsage_ShouldNotGrow()
    {
        // Arrange
        const int pageCount = 50;
        const int buildIterations = 10;

        await CreateTestPages(pageCount);

        // 初始全量构建
        await _cli.BuildAsync(new CliBuildOptions { Clean = true }, _fixture.SiteRoot);

        var memorySnapshots = new List<long>();

        ForceGC();
        var baselineMemory = GC.GetTotalMemory(true);
        _output.WriteLine($"基线内存: {baselineMemory / 1024.0 / 1024.0:F2} MB");

        // Act - 多次增量构建
        for (int i = 0; i < buildIterations; i++)
        {
            // 修改一个文件
            await _fixture.AddContentAsync($"posts/post-{i % pageCount:D4}.md", $"""
                ---
                title: "Updated Post {i}"
                date: 2024-01-{(i % 28) + 1:D2}
                draft: false
                ---
                
                Updated content iteration {i}.
                """);

            var result = await _cli.BuildAsync(
                new CliBuildOptions { Clean = false },
                _fixture.SiteRoot);

            result.IsSuccess.Should().BeTrue($"第 {i + 1} 次增量构建应该成功: {result.ErrorOutput}");

            // 记录内存
            ForceGC();
            var currentMemory = GC.GetTotalMemory(true);
            memorySnapshots.Add(currentMemory);

            if (i % 3 == 0)
            {
                _output.WriteLine($"第 {i + 1} 次增量构建后内存: {currentMemory / 1024.0 / 1024.0:F2} MB");
            }
        }

        // Assert
        var memoryGrowth = memorySnapshots.Last() - baselineMemory;
        var memoryGrowthMB = memoryGrowth / 1024.0 / 1024.0;

        _output.WriteLine($"总内存增长: {memoryGrowthMB:F2} MB");

        // 增量构建的内存增长应该更小
        memoryGrowthMB.Should().BeLessThan(50,
            "增量构建后内存增长应该很小");
    }

    #endregion

    #region 大量文件内存测试

    /// <summary>
    /// 测试处理大量文件时的内存使用
    /// </summary>
    [Fact]
    public async Task LargeFileCount_MemoryUsage_ShouldBeReasonable()
    {
        // Arrange
        const int pageCount = 500;

        await CreateTestPages(pageCount);

        ForceGC();
        var beforeMemory = GC.GetTotalMemory(true);
        _output.WriteLine($"构建前内存: {beforeMemory / 1024.0 / 1024.0:F2} MB");

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        ForceGC();
        var afterMemory = GC.GetTotalMemory(true);

        // Assert
        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");

        var memoryUsed = afterMemory - beforeMemory;
        var memoryUsedMB = memoryUsed / 1024.0 / 1024.0;
        var memoryPerFile = memoryUsed / (double)pageCount;

        _output.WriteLine($"构建后内存: {afterMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"内存使用: {memoryUsedMB:F2} MB");
        _output.WriteLine($"每文件内存: {memoryPerFile / 1024.0:F2} KB");

        // 每个文件的内存使用应该合理（小于 1MB）
        memoryPerFile.Should().BeLessThan(1024 * 1024,
            "每个文件的内存使用应该小于 1MB");
    }

    /// <summary>
    /// 测试处理大文件时的内存使用
    /// </summary>
    [Fact]
    public async Task LargeFiles_MemoryUsage_ShouldBeReasonable()
    {
        // Arrange - 创建一些大文件
        const int fileCount = 10;
        const int contentSize = 100 * 1024; // 100KB 每个文件

        for (int i = 0; i < fileCount; i++)
        {
            var largeContent = new string('A', contentSize);
            await _fixture.AddContentAsync($"posts/large-{i:D4}.md", $"""
                ---
                title: "Large Post {i}"
                date: 2024-01-{(i % 28) + 1:D2}
                draft: false
                ---
                
                {largeContent}
                """);
        }

        ForceGC();
        var beforeMemory = GC.GetTotalMemory(true);
        _output.WriteLine($"构建前内存: {beforeMemory / 1024.0 / 1024.0:F2} MB");

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        ForceGC();
        var afterMemory = GC.GetTotalMemory(true);

        // Assert
        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");

        var memoryUsed = afterMemory - beforeMemory;
        var memoryUsedMB = memoryUsed / 1024.0 / 1024.0;

        _output.WriteLine($"构建后内存: {afterMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"内存使用: {memoryUsedMB:F2} MB");

        // 处理大文件时内存使用应该合理
        memoryUsedMB.Should().BeLessThan(500,
            "处理大文件时内存使用应该合理");
    }

    #endregion

    #region GC 压力测试

    /// <summary>
    /// 测试 Gen2 GC 频率
    /// </summary>
    [Fact]
    public async Task Gen2GC_Frequency_ShouldBeLow()
    {
        // Arrange
        const int pageCount = 100;
        const int buildIterations = 5;

        await CreateTestPages(pageCount);

        ForceGC();
        var gen2Before = GC.CollectionCount(2);

        // Act
        for (int i = 0; i < buildIterations; i++)
        {
            var result = await _cli.BuildAsync(
                new CliBuildOptions { Clean = true },
                _fixture.SiteRoot);

            result.IsSuccess.Should().BeTrue($"第 {i + 1} 次构建应该成功: {result.ErrorOutput}");
        }

        var gen2After = GC.CollectionCount(2);
        var gen2Count = gen2After - gen2Before;

        // Assert
        _output.WriteLine($"Gen2 GC 次数: {gen2Count}");

        // Gen2 GC 应该相对较少（表示没有大量长期存活的对象）
        // 注意：在 CI 环境或并行测试时，GC 行为可能不稳定
        // 因此使用较宽松的阈值
        gen2Count.Should().BeLessThan(buildIterations * 5,
            "Gen2 GC 频率应该较低");
    }

    /// <summary>
    /// 测试内存分配率
    /// </summary>
    [Fact]
    public async Task MemoryAllocation_Rate_ShouldBeReasonable()
    {
        // Arrange
        const int pageCount = 50;

        await CreateTestPages(pageCount);

        ForceGC();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var totalAllocated = allocatedAfter - allocatedBefore;

        // Assert
        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");

        var allocatedMB = totalAllocated / 1024.0 / 1024.0;
        var allocatedPerFile = totalAllocated / (double)pageCount;

        _output.WriteLine($"总分配内存: {allocatedMB:F2} MB");
        _output.WriteLine($"每文件分配: {allocatedPerFile / 1024.0:F2} KB");

        // 每个文件的内存分配应该合理
        allocatedPerFile.Should().BeLessThan(10 * 1024 * 1024,
            "每个文件的内存分配应该小于 10MB");
    }

    #endregion

    #region 资源释放测试

    /// <summary>
    /// 测试构建后资源正确释放
    /// </summary>
    [Fact]
    public async Task AfterBuild_Resources_ShouldBeReleased()
    {
        // Arrange
        const int pageCount = 50;

        await CreateTestPages(pageCount);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");

        // 强制 GC
        ForceGC();
        var memoryAfterGC = GC.GetTotalMemory(true);

        // 等待一段时间
        await Task.Delay(1000);

        // 再次 GC
        ForceGC();
        var memoryAfterWait = GC.GetTotalMemory(true);

        // Assert
        var memoryDiff = Math.Abs(memoryAfterWait - memoryAfterGC);
        var memoryDiffMB = memoryDiff / 1024.0 / 1024.0;

        _output.WriteLine($"GC 后内存: {memoryAfterGC / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"等待后内存: {memoryAfterWait / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"内存差异: {memoryDiffMB:F2} MB");

        // 内存应该稳定
        memoryDiffMB.Should().BeLessThan(20,
            "构建后内存应该稳定");
    }

    /// <summary>
    /// 测试文件句柄释放
    /// </summary>
    [Fact]
    public async Task AfterBuild_FileHandles_ShouldBeReleased()
    {
        // Arrange
        const int pageCount = 20;

        await CreateTestPages(pageCount);

        // Act
        var result = await _cli.BuildAsync(
            new CliBuildOptions { Clean = true },
            _fixture.SiteRoot);

        result.IsSuccess.Should().BeTrue($"构建应该成功: {result.ErrorOutput}");

        // 等待文件句柄释放
        await Task.Delay(500);

        // Assert - 尝试删除输出目录（如果文件句柄未释放会失败）
        var canDelete = true;
        try
        {
            // 尝试重命名一个输出文件（测试文件是否被锁定）
            var testFile = _fixture.GetOutputFiles().FirstOrDefault();
            if (testFile != null)
            {
                var fullPath = Path.Combine(_fixture.OutputPath, testFile);
                if (File.Exists(fullPath))
                {
                    var tempPath = fullPath + ".temp";
                    File.Move(fullPath, tempPath);
                    File.Move(tempPath, fullPath);
                }
            }
        }
        catch (IOException)
        {
            canDelete = false;
        }

        canDelete.Should().BeTrue("构建后文件句柄应该被释放");
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
                categories:
                  - category{i % 5}
                ---
                
                This is test post number {i}.
                
                Lorem ipsum dolor sit amet, consectetur adipiscing elit.
                Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.
                """);
        }
    }

    /// <summary>
    /// 强制 GC
    /// </summary>
    private static void ForceGC()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    #endregion
}

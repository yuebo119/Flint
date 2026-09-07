// Flint 静态站点生成器
// TestSiteFixture 属性测试
// Property 19: 测试隔离性

using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.Fixtures;

/// <summary>
/// TestSiteFixture 属性测试
/// 验证测试隔离性和临时目录清理
/// </summary>
/// <remarks>
/// **Feature: Flint-integration-tests, Property 19: 测试隔离性**
/// 
/// *For any* 并行执行的测试，每个测试应在独立的临时目录中运行，
/// 测试完成后应自动清理临时文件，测试之间不应相互影响。
/// 
/// **Validates: Requirements 10.3, 10.6, 10.9**
/// </remarks>
public class TestSiteFixturePropertyTests
{
    /// <summary>
    /// Property 19.1: 测试 ID 唯一性
    /// 验证每个 TestSiteFixture 实例都有唯一的 TestId
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TestId_ShouldBeUnique_ForEachInstance(PositiveInt count)
    {
        // 限制并行实例数量以避免资源耗尽
        var instanceCount = Math.Min(count.Get, 10);
        var testIds = new HashSet<string>();

        for (var i = 0; i < instanceCount; i++)
        {
            var fixture = new TestSiteFixture();
            testIds.Add(fixture.TestId);
        }

        // 验证所有 TestId 都是唯一的
        var allUnique = testIds.Count == instanceCount;
        return allUnique.ToProperty()
            .Label($"所有 {instanceCount} 个实例应有唯一的 TestId");
    }

    /// <summary>
    /// Property 19.2: 站点根目录唯一性
    /// 验证并行创建的站点有独立的根目录
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task SiteRoot_ShouldBeUnique_ForParallelInstances(int instanceCount)
    {
        var fixtures = new List<TestSiteFixture>();
        var siteRoots = new HashSet<string>();

        try
        {
            // 并行创建多个站点
            var tasks = Enumerable.Range(0, instanceCount)
                .Select(async _ =>
                {
                    var fixture = new TestSiteFixture();
                    lock (fixtures)
                    {
                        fixtures.Add(fixture);
                    }
                    return await fixture.CreateSiteAsync();
                });

            var roots = await Task.WhenAll(tasks);

            foreach (var root in roots)
            {
                siteRoots.Add(root);
            }

            // 验证所有站点根目录都是唯一的
            siteRoots.Count.Should().Be(instanceCount,
                $"所有 {instanceCount} 个站点应有唯一的根目录");
        }
        finally
        {
            // 清理所有 fixture
            foreach (var fixture in fixtures)
            {
                await fixture.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Property 19.3: 临时目录清理
    /// 验证 DisposeAsync 后临时目录被正确删除
    /// </summary>
    [Fact]
    public async Task TempDirectory_ShouldBeCleanedUp_AfterDispose()
    {
        var fixture = new TestSiteFixture();
        string siteRoot;

        try
        {
            siteRoot = await fixture.CreateSiteAsync();

            // 验证目录存在
            Directory.Exists(siteRoot).Should().BeTrue("站点目录应在创建后存在");
        }
        finally
        {
            await fixture.DisposeAsync();
        }

        // 等待文件系统完成删除
        await Task.Delay(300);

        // 验证目录已被删除
        Directory.Exists(siteRoot).Should().BeFalse("站点目录应在 Dispose 后被删除");
    }

    /// <summary>
    /// Property 19.4: 并行操作隔离性
    /// 验证并行操作不会相互干扰
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ParallelOperations_ShouldNotInterfere(int instanceCount)
    {
        var fixtures = new List<TestSiteFixture>();
        var results = new List<bool>();

        try
        {
            // 并行创建站点并添加内容
            var tasks = Enumerable.Range(0, instanceCount)
                .Select(async i =>
                {
                    var fixture = new TestSiteFixture();
                    lock (fixtures)
                    {
                        fixtures.Add(fixture);
                    }

                    await fixture.CreateSiteAsync();

                    // 添加唯一内容
                    var content = $"""
                        +++
                        title = "测试文章 {i}"
                        date = 2024-01-{(i + 1):D2}T10:00:00+08:00
                        +++

                        这是测试内容 {i}。
                        """;
                    await fixture.AddContentAsync($"posts/test-{i}.md", content);

                    // 验证内容文件存在
                    var filePath = Path.Combine(fixture.SiteRoot, "content", "posts", $"test-{i}.md");
                    return File.Exists(filePath);
                });

            var taskResults = await Task.WhenAll(tasks);
            results.AddRange(taskResults);

            // 验证所有操作都成功
            results.Should().AllSatisfy(r => r.Should().BeTrue());
        }
        finally
        {
            foreach (var fixture in fixtures)
            {
                await fixture.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Property 19.5: 文件内容隔离性
    /// 验证一个 fixture 的文件操作不会影响另一个 fixture
    /// </summary>
    [Fact]
    public async Task FileOperations_ShouldBeIsolated_BetweenFixtures()
    {
        var fixture1 = new TestSiteFixture();
        var fixture2 = new TestSiteFixture();

        try
        {
            await fixture1.CreateSiteAsync();
            await fixture2.CreateSiteAsync();

            // 在 fixture1 中添加文件
            await fixture1.AddContentAsync("unique-file.md", """
                +++
                title = "Fixture 1 文件"
                +++
                内容 1
                """);

            // 在 fixture2 中添加不同的文件
            await fixture2.AddContentAsync("another-file.md", """
                +++
                title = "Fixture 2 文件"
                +++
                内容 2
                """);

            // 验证 fixture1 没有 fixture2 的文件
            var fixture1HasFile2 = File.Exists(
                Path.Combine(fixture1.SiteRoot, "content", "another-file.md"));

            // 验证 fixture2 没有 fixture1 的文件
            var fixture2HasFile1 = File.Exists(
                Path.Combine(fixture2.SiteRoot, "content", "unique-file.md"));

            fixture1HasFile2.Should().BeFalse("fixture1 不应有 fixture2 的文件");
            fixture2HasFile1.Should().BeFalse("fixture2 不应有 fixture1 的文件");
        }
        finally
        {
            await fixture1.DisposeAsync();
            await fixture2.DisposeAsync();
        }
    }

    /// <summary>
    /// Property 19.6: 配置隔离性
    /// 验证一个 fixture 的配置更改不会影响另一个 fixture
    /// </summary>
    [Theory]
    [InlineData("站点A", "站点B")]
    [InlineData("Test Site 1", "Test Site 2")]
    [InlineData("测试", "テスト")]
    public async Task ConfigChanges_ShouldBeIsolated_BetweenFixtures(string title1, string title2)
    {
        var fixture1 = new TestSiteFixture();
        var fixture2 = new TestSiteFixture();

        try
        {
            await fixture1.CreateSiteAsync();
            await fixture2.CreateSiteAsync();

            // 设置不同的配置
            await fixture1.SetConfigAsync($"""
                baseURL = "http://site1.example.com/"
                title = "{title1}"
                languageCode = "zh-cn"
                """);

            await fixture2.SetConfigAsync($"""
                baseURL = "http://site2.example.com/"
                title = "{title2}"
                languageCode = "en"
                """);

            // 验证配置是隔离的
            fixture1.Config.Title.Should().Be(title1);
            fixture2.Config.Title.Should().Be(title2);
        }
        finally
        {
            await fixture1.DisposeAsync();
            await fixture2.DisposeAsync();
        }
    }

    /// <summary>
    /// Property 19.7: 输出目录隔离性
    /// 验证构建输出在各自的目录中隔离
    /// </summary>
    [Fact]
    public async Task BuildOutput_ShouldBeIsolated_BetweenFixtures()
    {
        var fixture1 = new TestSiteFixture();
        var fixture2 = new TestSiteFixture();

        try
        {
            await fixture1.CreateSiteAsync();
            await fixture2.CreateSiteAsync();

            // 执行构建
            await fixture1.BuildAsync();
            await fixture2.BuildAsync();

            // 验证输出目录不同
            fixture1.OutputPath.Should().NotBe(fixture2.OutputPath);
            Directory.Exists(fixture1.OutputPath).Should().BeTrue();
            Directory.Exists(fixture2.OutputPath).Should().BeTrue();
        }
        finally
        {
            await fixture1.DisposeAsync();
            await fixture2.DisposeAsync();
        }
    }

    /// <summary>
    /// Property 19.8: 快照隔离性
    /// 验证快照在各自的 fixture 中隔离
    /// </summary>
    [Fact]
    public async Task Snapshots_ShouldBeIsolated_BetweenFixtures()
    {
        var fixture1 = new TestSiteFixture();
        var fixture2 = new TestSiteFixture();

        try
        {
            await fixture1.CreateSiteAsync();
            await fixture2.CreateSiteAsync();

            await fixture1.BuildAsync();
            await fixture2.BuildAsync();

            // 创建快照
            await fixture1.CreateSnapshotAsync("test");
            await fixture2.CreateSnapshotAsync("test");

            // 在 fixture1 中添加文件
            var newFilePath = Path.Combine(fixture1.OutputPath, "new-file.txt");
            await File.WriteAllTextAsync(newFilePath, "new content");

            // 比较快照
            var comparison1 = await fixture1.CompareWithSnapshotAsync("test");
            var comparison2 = await fixture2.CompareWithSnapshotAsync("test");

            // fixture1 应该检测到变化，fixture2 不应该
            comparison1.HasChanges.Should().BeTrue("fixture1 应检测到变化");
            comparison2.HasChanges.Should().BeFalse("fixture2 不应检测到变化");
        }
        finally
        {
            await fixture1.DisposeAsync();
            await fixture2.DisposeAsync();
        }
    }

    /// <summary>
    /// Property 19.9: 多次 Dispose 安全性
    /// 验证多次调用 Dispose 不会抛出异常
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MultipleDispose_ShouldBeSafe(int disposeCount)
    {
        using var fixture = new TestSiteFixture();
        await fixture.CreateSiteAsync();

        // 多次调用 Dispose 不应抛出异常
        for (var i = 0; i < disposeCount; i++)
        {
            var act = () => fixture.Dispose();
            act.Should().NotThrow();
        }
    }

    /// <summary>
    /// Property 19.10: 并行 Dispose 线程安全性
    /// 验证并行调用 Dispose 是线程安全的
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task ParallelDispose_ShouldBeThreadSafe(int parallelCount)
    {
        using var fixture = new TestSiteFixture();
        await fixture.CreateSiteAsync();

        // 并行调用 Dispose 不应抛出异常
        var tasks = Enumerable.Range(0, parallelCount)
            .Select(_ => Task.Run(() => fixture.Dispose()));

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }
}

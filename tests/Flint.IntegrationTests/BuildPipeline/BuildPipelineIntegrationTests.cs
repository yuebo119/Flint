// Flint 静态站点生成器
// 构建管道集成测试
// 验证从内容解析到输出生成的完整流程

using System.Diagnostics;
using Flint.Core.Models;
using Flint.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// 构建管道集成测试
/// 验证从内容解析到输出生成的完整流程
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 2.6: 草稿过滤
/// - Requirements 2.7: 未来内容过滤
/// - Requirements 2.10: 构建管道输出正确性
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
public class BuildPipelineIntegrationTests : IAsyncLifetime
{
    #region 私有字段

    private readonly ITestOutputHelper _output;
    private readonly TestSiteFixture _fixture;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建构建管道集成测试实例
    /// </summary>
    /// <param name="output">测试输出帮助器</param>
    public BuildPipelineIntegrationTests(ITestOutputHelper output)
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

    #region 完整构建流程测试

    /// <summary>
    /// 测试完整构建流程 - 从内容到输出
    /// 验证构建管道能够正确处理内容文件并生成 HTML 输出
    /// </summary>
    [Fact]
    [Trait("TestType", "FullBuild")]
    public async Task BuildAsync_WithValidSite_ShouldGenerateCorrectOutput()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("complete");
        _output.WriteLine($"测试站点创建于: {_fixture.SiteRoot}");

        // Act - 执行构建
        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.BuildAsync();
        stopwatch.Stop();

        _output.WriteLine($"构建耗时: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"构建页面数: {result.PagesBuilt}");
        _output.WriteLine($"处理资源数: {result.AssetsProcessed}");

        // 输出错误信息（用于调试）
        if (result.Errors.Count > 0)
        {
            _output.WriteLine($"构建错误数: {result.Errors.Count}");
            foreach (var error in result.Errors)
            {
                _output.WriteLine($"  错误: [{error.ErrorCode}] {error.FilePath}:{error.Line} - {error.Message}");
            }
        }

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功完成");
        result.PagesBuilt.Should().BeGreaterThan(0, "应该构建至少一个页面");
        result.Errors.Should().BeEmpty("不应该有构建错误");

        // 验证输出目录存在
        Directory.Exists(_fixture.OutputPath).Should().BeTrue("输出目录应该存在");

        // 验证首页生成
        _fixture.OutputFileExists("index.html").Should().BeTrue("应该生成首页");

        // 验证关于页面生成
        _fixture.OutputFileExists("about/index.html").Should().BeTrue("应该生成关于页面");

        // 输出所有生成的文件
        var outputFiles = _fixture.GetOutputFiles();
        _output.WriteLine($"生成的文件总数: {outputFiles.Count}");
        foreach (var file in outputFiles.Take(20))
        {
            _output.WriteLine($"  - {file}");
        }
        if (outputFiles.Count > 20)
        {
            _output.WriteLine($"  ... 还有 {outputFiles.Count - 20} 个文件");
        }
    }

    /// <summary>
    /// 测试完整构建流程 - 最小站点
    /// 验证最小配置的站点也能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "FullBuild")]
    public async Task BuildAsync_WithMinimalSite_ShouldGenerateBasicOutput()
    {
        // Arrange - 创建最小测试站点
        await _fixture.CreateSiteAsync("minimal");
        _output.WriteLine($"最小测试站点创建于: {_fixture.SiteRoot}");

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("最小站点构建应该成功");
        result.PagesBuilt.Should().BeGreaterThan(0, "应该至少构建一个页面");
        result.Errors.Should().BeEmpty("不应该有构建错误");

        // 验证首页生成
        _fixture.OutputFileExists("index.html").Should().BeTrue("应该生成首页");

        // 验证首页内容
        var indexContent = await _fixture.GetOutputFileAsync("index.html");
        indexContent.Should().NotBeNullOrEmpty("首页内容不应为空");
        indexContent.Should().Contain("<!DOCTYPE html>", "应该是有效的 HTML 文档");
    }

    /// <summary>
    /// 测试完整构建流程 - 验证 HTML 内容正确性
    /// 验证生成的 HTML 包含正确的页面元数据和内容
    /// </summary>
    [Fact]
    [Trait("TestType", "FullBuild")]
    public async Task BuildAsync_ShouldGenerateHtmlWithCorrectContent()
    {
        // Arrange - 创建测试站点并添加自定义内容
        await _fixture.CreateSiteAsync("minimal");

        // 添加测试文章
        var testContent = """
            +++
            title = "测试文章标题"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            description = "这是测试文章的描述"
            +++

            ## 文章正文

            这是测试文章的正文内容。

            - 列表项 1
            - 列表项 2
            - 列表项 3
            """;
        await _fixture.AddContentAsync("posts/test-article.md", testContent);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证文章页面生成
        var outputFiles = _fixture.GetOutputFiles();
        var articleExists = outputFiles.Any(f => f.Contains("test-article"));
        articleExists.Should().BeTrue("应该生成文章页面");
    }

    #endregion

    #region 增量构建测试

    /// <summary>
    /// 测试增量构建 - 只处理变化的文件
    /// 验证增量构建只重新处理修改过的文件
    /// </summary>
    [Fact]
    [Trait("TestType", "IncrementalBuild")]
    public async Task IncrementalBuildAsync_WithChangedFile_ShouldOnlyProcessChangedFiles()
    {
        // Arrange - 创建测试站点并执行初始构建
        await _fixture.CreateSiteAsync("complete");

        var initialResult = await _fixture.BuildAsync();
        initialResult.Success.Should().BeTrue("初始构建应该成功");

        // 创建快照
        await _fixture.CreateSnapshotAsync("initial");

        // 添加新文章
        var newContent = """
            +++
            title = "新增文章"
            date = 2024-01-20T10:00:00+08:00
            draft = false
            +++

            这是新增的文章内容。
            """;
        await _fixture.AddContentAsync("posts/new-article.md", newContent);

        // Act - 执行增量构建
        var changedFiles = new List<string>
        {
            Path.Combine(_fixture.SiteRoot, "content", "posts", "new-article.md")
        };
        var incrementalResult = await _fixture.IncrementalBuildAsync(changedFiles);

        // Assert - 验证增量构建成功
        incrementalResult.Success.Should().BeTrue("增量构建应该成功");

        // 比较快照
        var comparison = await _fixture.CompareWithSnapshotAsync("initial");
        _output.WriteLine($"新增文件: {comparison.AddedFiles.Count}");
        _output.WriteLine($"修改文件: {comparison.ModifiedFiles.Count}");
        _output.WriteLine($"未变化文件: {comparison.UnchangedFiles.Count}");

        // 验证有新增文件
        comparison.AddedFiles.Should().NotBeEmpty("应该有新增的输出文件");
    }

    /// <summary>
    /// 测试增量构建 - 修改现有文件
    /// 验证修改现有文件后增量构建能正确更新输出
    /// </summary>
    [Fact]
    [Trait("TestType", "IncrementalBuild")]
    public async Task IncrementalBuildAsync_WithModifiedFile_ShouldUpdateOutput()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加初始文章
        var initialContent = """
            +++
            title = "初始标题"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            初始内容。
            """;
        await _fixture.AddContentAsync("posts/modifiable.md", initialContent);

        // 执行初始构建
        var initialResult = await _fixture.BuildAsync();
        initialResult.Success.Should().BeTrue("初始构建应该成功");

        // 创建快照
        await _fixture.CreateSnapshotAsync("before-modification");

        // 修改文章内容
        var modifiedContent = """
            +++
            title = "修改后的标题"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            修改后的内容。这是更新后的文章。
            """;
        await _fixture.AddContentAsync("posts/modifiable.md", modifiedContent);

        // Act - 执行增量构建
        var changedFiles = new List<string>
        {
            Path.Combine(_fixture.SiteRoot, "content", "posts", "modifiable.md")
        };
        var incrementalResult = await _fixture.IncrementalBuildAsync(changedFiles);

        // Assert - 验证增量构建成功
        incrementalResult.Success.Should().BeTrue("增量构建应该成功");

        // 比较快照
        var comparison = await _fixture.CompareWithSnapshotAsync("before-modification");
        _output.WriteLine($"修改的文件: {string.Join(", ", comparison.ModifiedFiles)}");

        // 验证文章被更新
        comparison.ModifiedFiles.Should().NotBeEmpty("应该有修改的输出文件");
    }

    /// <summary>
    /// 测试增量构建性能 - 验证增量构建比全量构建更快
    /// </summary>
    [Fact]
    [Trait("TestType", "IncrementalBuild")]
    [Trait("TestType", "Performance")]
    public async Task IncrementalBuildAsync_ShouldBeFasterThanFullBuild()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("complete");

        // 执行初始全量构建
        var fullBuildStopwatch = Stopwatch.StartNew();
        var fullBuildResult = await _fixture.BuildAsync();
        fullBuildStopwatch.Stop();
        var fullBuildTime = fullBuildStopwatch.ElapsedMilliseconds;

        fullBuildResult.Success.Should().BeTrue("全量构建应该成功");
        _output.WriteLine($"全量构建耗时: {fullBuildTime}ms");

        // 添加一个新文章
        var newContent = """
            +++
            title = "性能测试文章"
            date = 2024-01-20T10:00:00+08:00
            draft = false
            +++

            这是用于性能测试的文章。
            """;
        await _fixture.AddContentAsync("posts/perf-test.md", newContent);

        // Act - 执行增量构建
        var changedFiles = new List<string>
        {
            Path.Combine(_fixture.SiteRoot, "content", "posts", "perf-test.md")
        };

        var incrementalStopwatch = Stopwatch.StartNew();
        var incrementalResult = await _fixture.IncrementalBuildAsync(changedFiles);
        incrementalStopwatch.Stop();
        var incrementalBuildTime = incrementalStopwatch.ElapsedMilliseconds;

        _output.WriteLine($"增量构建耗时: {incrementalBuildTime}ms");

        // Assert - 验证增量构建成功且更快
        incrementalResult.Success.Should().BeTrue("增量构建应该成功");
        _output.WriteLine($"性能比较: 增量构建 {incrementalBuildTime}ms vs 全量构建 {fullBuildTime}ms");
    }

    #endregion

    #region 草稿过滤测试

    /// <summary>
    /// 测试草稿过滤 - 默认不包含草稿
    /// 验证默认构建选项下草稿内容不会被包含在输出中
    /// </summary>
    [Fact]
    [Trait("TestType", "DraftFilter")]
    public async Task BuildAsync_WithDefaultOptions_ShouldExcludeDrafts()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加草稿文章
        var draftContent = """
            +++
            title = "草稿文章"
            date = 2024-01-15T10:00:00+08:00
            draft = true
            +++

            这是草稿内容，不应该出现在默认构建输出中。
            """;
        await _fixture.AddContentAsync("posts/draft-article.md", draftContent);

        // 添加正常文章
        var normalContent = """
            +++
            title = "正常文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是正常内容，应该出现在构建输出中。
            """;
        await _fixture.AddContentAsync("posts/normal-article.md", normalContent);

        // Act - 使用默认选项构建（不包含草稿）
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeDrafts = false
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证草稿文章不存在
        var outputFiles = _fixture.GetOutputFiles();
        var draftExists = outputFiles.Any(f => f.Contains("draft-article"));
        draftExists.Should().BeFalse("草稿文章不应该被生成");

        // 验证正常文章存在
        var normalExists = outputFiles.Any(f => f.Contains("normal-article"));
        normalExists.Should().BeTrue("正常文章应该被生成");

        _output.WriteLine("草稿过滤测试通过：草稿文章被正确排除");
    }

    /// <summary>
    /// 测试草稿过滤 - 启用草稿包含
    /// 验证启用 IncludeDrafts 选项后草稿内容会被包含在输出中
    /// </summary>
    [Fact]
    [Trait("TestType", "DraftFilter")]
    public async Task BuildAsync_WithIncludeDrafts_ShouldIncludeDrafts()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加草稿文章
        var draftContent = """
            +++
            title = "草稿文章"
            date = 2024-01-15T10:00:00+08:00
            draft = true
            +++

            这是草稿内容，启用 IncludeDrafts 后应该出现在输出中。
            """;
        await _fixture.AddContentAsync("posts/draft-article.md", draftContent);

        // Act - 使用包含草稿的选项构建
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeDrafts = true
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证草稿文章存在
        var outputFiles = _fixture.GetOutputFiles();
        var draftExists = outputFiles.Any(f => f.Contains("draft-article"));
        draftExists.Should().BeTrue("启用 IncludeDrafts 后草稿文章应该被生成");

        _output.WriteLine("草稿包含测试通过：草稿文章被正确包含");
    }

    /// <summary>
    /// 测试草稿过滤 - 使用 Complete 站点模板
    /// 验证 Complete 站点中的草稿文章被正确过滤
    /// </summary>
    [Fact]
    [Trait("TestType", "DraftFilter")]
    public async Task BuildAsync_WithCompleteSite_ShouldFilterDraftsCorrectly()
    {
        // Arrange - 使用 Complete 站点模板
        await _fixture.CreateSiteAsync("complete");

        // Act - 默认构建（不包含草稿）
        var defaultResult = await _fixture.BuildAsync(new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeDrafts = false
        });

        // Assert - 验证构建成功
        defaultResult.Success.Should().BeTrue("默认构建应该成功");

        var defaultOutputFiles = _fixture.GetOutputFiles();
        _output.WriteLine($"默认构建输出文件数: {defaultOutputFiles.Count}");

        // 清理输出目录
        await _fixture.CleanOutputAsync();

        // Act - 包含草稿的构建
        var withDraftsResult = await _fixture.BuildAsync(new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeDrafts = true
        });

        // Assert - 验证包含草稿的构建
        withDraftsResult.Success.Should().BeTrue("包含草稿的构建应该成功");

        var withDraftsOutputFiles = _fixture.GetOutputFiles();
        _output.WriteLine($"包含草稿构建输出文件数: {withDraftsOutputFiles.Count}");

        withDraftsOutputFiles.Count.Should().BeGreaterThanOrEqualTo(defaultOutputFiles.Count,
            "包含草稿的构建应该生成更多或相同数量的文件");
    }

    #endregion

    #region 未来内容过滤测试

    /// <summary>
    /// 测试未来内容过滤 - 默认不包含未来内容
    /// 验证默认构建选项下未来日期的内容不会被包含在输出中
    /// </summary>
    [Fact]
    [Trait("TestType", "FutureFilter")]
    public async Task BuildAsync_WithDefaultOptions_ShouldExcludeFutureContent()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加未来日期的文章
        var futureDate = DateTime.Now.AddYears(10).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var futureContent = $"""
            +++
            title = "未来文章"
            date = {futureDate}
            draft = false
            +++

            这是未来日期的内容，不应该出现在默认构建输出中。
            """;
        await _fixture.AddContentAsync("posts/future-article.md", futureContent);

        // 添加当前日期的文章
        var currentDate = DateTime.Now.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var currentContent = $"""
            +++
            title = "当前文章"
            date = {currentDate}
            draft = false
            +++

            这是当前日期的内容，应该出现在构建输出中。
            """;
        await _fixture.AddContentAsync("posts/current-article.md", currentContent);

        // Act - 使用默认选项构建（不包含未来内容）
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeFuture = false
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证未来文章不存在
        var outputFiles = _fixture.GetOutputFiles();
        var futureExists = outputFiles.Any(f => f.Contains("future-article"));
        futureExists.Should().BeFalse("未来日期的文章不应该被生成");

        // 验证当前文章存在
        var currentExists = outputFiles.Any(f => f.Contains("current-article"));
        currentExists.Should().BeTrue("当前日期的文章应该被生成");

        _output.WriteLine("未来内容过滤测试通过：未来日期的文章被正确排除");
    }

    /// <summary>
    /// 测试未来内容过滤 - 启用未来内容包含
    /// 验证启用 IncludeFuture 选项后未来日期的内容会被包含在输出中
    /// </summary>
    [Fact]
    [Trait("TestType", "FutureFilter")]
    public async Task BuildAsync_WithIncludeFuture_ShouldIncludeFutureContent()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加未来日期的文章
        var futureDate = DateTime.Now.AddYears(10).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var futureContent = $"""
            +++
            title = "未来文章"
            date = {futureDate}
            draft = false
            +++

            这是未来日期的内容，启用 IncludeFuture 后应该出现在输出中。
            """;
        await _fixture.AddContentAsync("posts/future-article.md", futureContent);

        // Act - 使用包含未来内容的选项构建
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeFuture = true
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证未来文章存在
        var outputFiles = _fixture.GetOutputFiles();
        var futureExists = outputFiles.Any(f => f.Contains("future-article"));
        futureExists.Should().BeTrue("启用 IncludeFuture 后未来日期的文章应该被生成");

        _output.WriteLine("未来内容包含测试通过：未来日期的文章被正确包含");
    }

    #endregion

    #region 过期内容过滤测试

    /// <summary>
    /// 测试过期内容过滤 - 默认不包含过期内容
    /// 验证默认构建选项下过期的内容不会被包含在输出中
    /// </summary>
    [Fact]
    [Trait("TestType", "ExpiredFilter")]
    public async Task BuildAsync_WithDefaultOptions_ShouldExcludeExpiredContent()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加过期的文章（设置 expiryDate 为过去的日期）
        var pastDate = DateTime.Now.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var expiredContent = $"""
            +++
            title = "过期文章"
            date = 2024-01-01T10:00:00+08:00
            expiryDate = {pastDate}
            draft = false
            +++

            这是过期的内容，不应该出现在默认构建输出中。
            """;
        await _fixture.AddContentAsync("posts/expired-article.md", expiredContent);

        // 添加未过期的文章
        var futureExpiry = DateTime.Now.AddYears(1).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var validContent = $"""
            +++
            title = "有效文章"
            date = 2024-01-01T10:00:00+08:00
            expiryDate = {futureExpiry}
            draft = false
            +++

            这是有效的内容，应该出现在构建输出中。
            """;
        await _fixture.AddContentAsync("posts/valid-article.md", validContent);

        // Act - 使用默认选项构建（不包含过期内容）
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeExpired = false
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证过期文章不存在
        var outputFiles = _fixture.GetOutputFiles();
        var expiredExists = outputFiles.Any(f => f.Contains("expired-article"));
        expiredExists.Should().BeFalse("过期的文章不应该被生成");

        // 验证有效文章存在
        var validExists = outputFiles.Any(f => f.Contains("valid-article"));
        validExists.Should().BeTrue("有效的文章应该被生成");

        _output.WriteLine("过期内容过滤测试通过：过期的文章被正确排除");
    }

    /// <summary>
    /// 测试过期内容过滤 - 启用过期内容包含
    /// 验证启用 IncludeExpired 选项后过期的内容会被包含在输出中
    /// </summary>
    [Fact]
    [Trait("TestType", "ExpiredFilter")]
    public async Task BuildAsync_WithIncludeExpired_ShouldIncludeExpiredContent()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加过期的文章
        var pastDate = DateTime.Now.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var expiredContent = $"""
            +++
            title = "过期文章"
            date = 2024-01-01T10:00:00+08:00
            expiryDate = {pastDate}
            draft = false
            +++

            这是过期的内容，启用 IncludeExpired 后应该出现在输出中。
            """;
        await _fixture.AddContentAsync("posts/expired-article.md", expiredContent);

        // Act - 使用包含过期内容的选项构建
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeExpired = true
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证过期文章存在
        var outputFiles = _fixture.GetOutputFiles();
        var expiredExists = outputFiles.Any(f => f.Contains("expired-article"));
        expiredExists.Should().BeTrue("启用 IncludeExpired 后过期的文章应该被生成");

        _output.WriteLine("过期内容包含测试通过：过期的文章被正确包含");
    }

    #endregion

    #region 并行构建测试

    /// <summary>
    /// 测试并行构建 - 验证并行构建正确性
    /// 验证并行构建产生与串行构建相同的结果
    /// </summary>
    [Fact]
    [Trait("TestType", "ParallelBuild")]
    public async Task BuildAsync_WithParallelism_ShouldProduceCorrectOutput()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("complete");

        // Act - 使用并行构建
        var parallelOptions = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Parallelism = Environment.ProcessorCount
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.BuildAsync(parallelOptions);
        stopwatch.Stop();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("并行构建应该成功");
        result.PagesBuilt.Should().BeGreaterThan(0, "应该构建至少一个页面");
        result.Errors.Should().BeEmpty("不应该有构建错误");

        _output.WriteLine($"并行构建耗时: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"使用的并行度: {parallelOptions.Parallelism}");
        _output.WriteLine($"构建页面数: {result.PagesBuilt}");

        // 验证输出文件存在
        var outputFiles = _fixture.GetOutputFiles();
        outputFiles.Should().NotBeEmpty("应该生成输出文件");

        // 验证关键页面存在
        _fixture.OutputFileExists("index.html").Should().BeTrue("应该生成首页");
    }

    /// <summary>
    /// 测试并行构建 - 比较不同并行度的结果
    /// 验证不同并行度产生相同的输出结果
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [Trait("TestType", "ParallelBuild")]
    public async Task BuildAsync_WithDifferentParallelism_ShouldProduceSameOutput(int parallelism)
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加多个测试文章以便测试并行处理
        for (var i = 1; i <= 5; i++)
        {
            var content = $"""
                +++
                title = "测试文章 {i}"
                date = 2024-01-{i:D2}T10:00:00+08:00
                draft = false
                +++

                这是测试文章 {i} 的内容。
                """;
            await _fixture.AddContentAsync($"posts/article-{i}.md", content);
        }

        // Act - 使用指定并行度构建
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Parallelism = parallelism
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.BuildAsync(options);
        stopwatch.Stop();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue($"并行度 {parallelism} 的构建应该成功");

        _output.WriteLine($"并行度 {parallelism} 构建耗时: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"构建页面数: {result.PagesBuilt}");

        // 验证所有文章都被生成
        var outputFiles = _fixture.GetOutputFiles();
        for (var i = 1; i <= 5; i++)
        {
            var articleExists = outputFiles.Any(f => f.Contains($"article-{i}"));
            articleExists.Should().BeTrue($"文章 {i} 应该被生成");
        }
    }

    /// <summary>
    /// 测试并行构建性能 - 验证并行构建比串行构建更快
    /// </summary>
    [Fact]
    [Trait("TestType", "ParallelBuild")]
    [Trait("TestType", "Performance")]
    public async Task BuildAsync_ParallelShouldBeFasterThanSerial()
    {
        // 跳过单核系统
        if (Environment.ProcessorCount < 2)
        {
            _output.WriteLine("跳过测试：系统只有单核处理器");
            return;
        }

        // Arrange - 创建测试站点并添加多个文章
        await _fixture.CreateSiteAsync("minimal");

        // 添加足够多的文章以便测试并行性能
        for (var i = 1; i <= 20; i++)
        {
            var content = $"""
                +++
                title = "性能测试文章 {i}"
                date = 2024-01-{(i % 28) + 1:D2}T10:00:00+08:00
                draft = false
                tags = ["测试", "性能"]
                +++

                这是性能测试文章 {i} 的内容。

                ## 章节 1

                Lorem ipsum dolor sit amet, consectetur adipiscing elit.

                ## 章节 2

                Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.
                """;
            await _fixture.AddContentAsync($"posts/perf-article-{i}.md", content);
        }

        // Act - 串行构建
        var serialOptions = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Parallelism = 1,
            CleanOutput = true
        };

        var serialStopwatch = Stopwatch.StartNew();
        var serialResult = await _fixture.BuildAsync(serialOptions);
        serialStopwatch.Stop();
        var serialTime = serialStopwatch.ElapsedMilliseconds;

        serialResult.Success.Should().BeTrue("串行构建应该成功");
        _output.WriteLine($"串行构建耗时: {serialTime}ms");

        // 清理输出目录
        await _fixture.CleanOutputAsync();

        // Act - 并行构建
        var parallelOptions = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Parallelism = Environment.ProcessorCount,
            CleanOutput = true
        };

        var parallelStopwatch = Stopwatch.StartNew();
        var parallelResult = await _fixture.BuildAsync(parallelOptions);
        parallelStopwatch.Stop();
        var parallelTime = parallelStopwatch.ElapsedMilliseconds;

        parallelResult.Success.Should().BeTrue("并行构建应该成功");
        _output.WriteLine($"并行构建耗时: {parallelTime}ms");
        _output.WriteLine($"使用的并行度: {Environment.ProcessorCount}");

        // Assert - 验证结果一致
        parallelResult.PagesBuilt.Should().Be(serialResult.PagesBuilt,
            "并行构建和串行构建应该生成相同数量的页面");

        // 输出性能比较
        var speedup = serialTime > 0 ? (double)serialTime / parallelTime : 1.0;
        _output.WriteLine($"性能提升: {speedup:F2}x");
    }

    #endregion

    #region 组合过滤测试

    /// <summary>
    /// 测试组合过滤 - 同时启用草稿和未来内容
    /// 验证同时启用多个过滤选项时的正确行为
    /// </summary>
    [Fact]
    [Trait("TestType", "CombinedFilter")]
    public async Task BuildAsync_WithCombinedFilters_ShouldIncludeAllContent()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加草稿文章
        var draftContent = """
            +++
            title = "草稿文章"
            date = 2024-01-15T10:00:00+08:00
            draft = true
            +++

            草稿内容。
            """;
        await _fixture.AddContentAsync("posts/draft.md", draftContent);

        // 添加未来日期的文章
        var futureDate = DateTime.Now.AddYears(10).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var futureContent = $"""
            +++
            title = "未来文章"
            date = {futureDate}
            draft = false
            +++

            未来内容。
            """;
        await _fixture.AddContentAsync("posts/future.md", futureContent);

        // 添加正常文章
        var normalContent = """
            +++
            title = "正常文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            正常内容。
            """;
        await _fixture.AddContentAsync("posts/normal.md", normalContent);

        // Act - 同时启用草稿和未来内容
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeDrafts = true,
            IncludeFuture = true
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证所有文章都被生成
        var outputFiles = _fixture.GetOutputFiles();
        outputFiles.Any(f => f.Contains("draft")).Should().BeTrue("草稿文章应该被生成");
        outputFiles.Any(f => f.Contains("future")).Should().BeTrue("未来文章应该被生成");
        outputFiles.Any(f => f.Contains("normal")).Should().BeTrue("正常文章应该被生成");

        _output.WriteLine("组合过滤测试通过：所有内容都被正确包含");
    }

    /// <summary>
    /// 测试组合过滤 - 草稿且未来日期的文章
    /// 验证同时是草稿且是未来日期的文章的过滤行为
    /// </summary>
    [Fact]
    [Trait("TestType", "CombinedFilter")]
    public async Task BuildAsync_WithDraftAndFutureContent_ShouldFilterCorrectly()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加既是草稿又是未来日期的文章
        var futureDate = DateTime.Now.AddYears(10).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var draftFutureContent = $"""
            +++
            title = "草稿且未来的文章"
            date = {futureDate}
            draft = true
            +++

            这是既是草稿又是未来日期的内容。
            """;
        await _fixture.AddContentAsync("posts/draft-future.md", draftFutureContent);

        // 测试场景：同时启用草稿和未来内容
        var bothOptions = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeDrafts = true,
            IncludeFuture = true
        };
        var bothResult = await _fixture.BuildAsync(bothOptions);
        bothResult.Success.Should().BeTrue();

        var bothFiles = _fixture.GetOutputFiles();
        var existsWithBoth = bothFiles.Any(f => f.Contains("draft-future"));
        existsWithBoth.Should().BeTrue("同时启用草稿和未来内容时应该包含该文章");
        _output.WriteLine($"同时启用草稿和未来内容时是否包含: {existsWithBoth}");
    }

    #endregion

    #region 构建选项测试

    /// <summary>
    /// 测试清理输出目录选项
    /// 验证 CleanOutput 选项能正确清理输出目录
    /// </summary>
    [Fact]
    [Trait("TestType", "BuildOptions")]
    public async Task BuildAsync_WithCleanOutput_ShouldCleanOutputDirectory()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 执行初始构建
        var initialResult = await _fixture.BuildAsync();
        initialResult.Success.Should().BeTrue();

        // 在输出目录中创建一个额外的文件
        var extraFilePath = Path.Combine(_fixture.OutputPath, "extra-file.txt");
        await File.WriteAllTextAsync(extraFilePath, "这是一个额外的文件");
        File.Exists(extraFilePath).Should().BeTrue("额外文件应该存在");

        // Act - 使用 CleanOutput 选项重新构建
        var cleanOptions = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            CleanOutput = true
        };
        var cleanResult = await _fixture.BuildAsync(cleanOptions);

        // Assert - 验证构建成功且额外文件被删除
        cleanResult.Success.Should().BeTrue("构建应该成功");
        File.Exists(extraFilePath).Should().BeFalse("额外文件应该被清理");

        _output.WriteLine("清理输出目录测试通过");
    }

    /// <summary>
    /// 测试不清理输出目录选项
    /// 验证禁用 CleanOutput 选项时保留现有文件
    /// </summary>
    [Fact]
    [Trait("TestType", "BuildOptions")]
    public async Task BuildAsync_WithoutCleanOutput_ShouldPreserveExistingFiles()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 执行初始构建
        var initialResult = await _fixture.BuildAsync();
        initialResult.Success.Should().BeTrue();

        // 在输出目录中创建一个额外的文件
        var extraFilePath = Path.Combine(_fixture.OutputPath, "preserve-me.txt");
        await File.WriteAllTextAsync(extraFilePath, "这个文件应该被保留");
        File.Exists(extraFilePath).Should().BeTrue("额外文件应该存在");

        // Act - 不使用 CleanOutput 选项重新构建
        var noCleanOptions = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            CleanOutput = false
        };
        var noCleanResult = await _fixture.BuildAsync(noCleanOptions);

        // Assert - 验证构建成功且额外文件被保留
        noCleanResult.Success.Should().BeTrue("构建应该成功");
        File.Exists(extraFilePath).Should().BeTrue("额外文件应该被保留");

        _output.WriteLine("保留现有文件测试通过");
    }

    /// <summary>
    /// 测试压缩输出选项
    /// 验证 Minify 选项能正确压缩输出
    /// </summary>
    [Fact]
    [Trait("TestType", "BuildOptions")]
    public async Task BuildAsync_WithMinify_ShouldProduceMinifiedOutput()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("complete");

        // Act - 不压缩构建
        var normalOptions = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Minify = false,
            CleanOutput = true
        };
        var normalResult = await _fixture.BuildAsync(normalOptions);
        normalResult.Success.Should().BeTrue();

        // 获取正常构建的首页大小
        var normalIndexContent = await _fixture.GetOutputFileAsync("index.html");
        var normalSize = normalIndexContent?.Length ?? 0;
        _output.WriteLine($"正常构建首页大小: {normalSize} 字符");

        // 清理
        await _fixture.CleanOutputAsync();

        // Act - 压缩构建
        var minifyOptions = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Minify = true,
            CleanOutput = true
        };
        var minifyResult = await _fixture.BuildAsync(minifyOptions);
        minifyResult.Success.Should().BeTrue();

        // 获取压缩构建的首页大小
        var minifiedIndexContent = await _fixture.GetOutputFileAsync("index.html");
        var minifiedSize = minifiedIndexContent?.Length ?? 0;
        _output.WriteLine($"压缩构建首页大小: {minifiedSize} 字符");
        _output.WriteLine($"大小比较: 压缩后 {minifiedSize} vs 正常 {normalSize}");
    }

    /// <summary>
    /// 测试环境变量选项
    /// 验证 Environment 选项能正确设置构建环境
    /// </summary>
    [Theory]
    [InlineData("development")]
    [InlineData("production")]
    [InlineData("staging")]
    [Trait("TestType", "BuildOptions")]
    public async Task BuildAsync_WithEnvironment_ShouldSetCorrectEnvironment(string environment)
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // Act - 使用指定环境构建
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Environment = environment
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue($"环境 {environment} 的构建应该成功");

        _output.WriteLine($"环境 {environment} 构建测试通过");
    }

    #endregion

    #region 构建结果验证测试

    /// <summary>
    /// 测试构建结果统计信息
    /// 验证构建结果包含正确的统计信息
    /// </summary>
    [Fact]
    [Trait("TestType", "BuildResult")]
    public async Task BuildAsync_ShouldReturnCorrectStatistics()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("complete");

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建结果
        result.Success.Should().BeTrue("构建应该成功");
        result.PagesBuilt.Should().BeGreaterThan(0, "应该构建至少一个页面");
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero, "构建耗时应该大于零");
        result.MemoryUsed.Should().BeGreaterThanOrEqualTo(0, "内存使用应该是非负数");
        result.OutputPath.Should().Be(_fixture.OutputPath, "输出路径应该正确");
        result.Errors.Should().BeEmpty("不应该有错误");

        _output.WriteLine($"构建统计:");
        _output.WriteLine($"  - 页面数: {result.PagesBuilt}");
        _output.WriteLine($"  - 资源数: {result.AssetsProcessed}");
        _output.WriteLine($"  - 耗时: {result.Duration.TotalMilliseconds:F2}ms");
        _output.WriteLine($"  - 内存: {result.MemoryUsed / 1024.0 / 1024.0:F2}MB");
        _output.WriteLine($"  - 警告数: {result.Warnings.Count}");

        // 验证统计信息（如果存在）
        if (result.Statistics != null)
        {
            _output.WriteLine($"  - 解析耗时: {result.Statistics.ParsingTime.TotalMilliseconds:F2}ms");
            _output.WriteLine($"  - 渲染耗时: {result.Statistics.RenderingTime.TotalMilliseconds:F2}ms");
            _output.WriteLine($"  - 资源处理耗时: {result.Statistics.AssetProcessingTime.TotalMilliseconds:F2}ms");
            _output.WriteLine($"  - 写入耗时: {result.Statistics.WritingTime.TotalMilliseconds:F2}ms");
            _output.WriteLine($"  - 缓存命中率: {result.Statistics.CacheHitRate:P2}");
        }
    }

    /// <summary>
    /// 测试构建警告收集
    /// 验证构建过程中的警告被正确收集
    /// </summary>
    [Fact]
    [Trait("TestType", "BuildResult")]
    public async Task BuildAsync_ShouldCollectWarnings()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加可能产生警告的内容
        var contentWithoutDescription = """
            +++
            title = "无描述文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这篇文章没有描述字段。
            """;
        await _fixture.AddContentAsync("posts/no-description.md", contentWithoutDescription);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功（警告不应导致失败）");

        // 输出警告信息
        if (result.Warnings.Count > 0)
        {
            _output.WriteLine($"收集到 {result.Warnings.Count} 个警告:");
            foreach (var warning in result.Warnings)
            {
                _output.WriteLine($"  - [{warning.WarningCode}] {warning.Message}");
            }
        }
        else
        {
            _output.WriteLine("没有收集到警告");
        }
    }

    #endregion

    #region 多语言站点测试

    /// <summary>
    /// 测试多语言站点构建
    /// 验证多语言站点能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "Multilingual")]
    public async Task BuildAsync_WithMultilingualSite_ShouldGenerateAllLanguages()
    {
        // Arrange - 创建多语言测试站点
        await _fixture.CreateSiteAsync("multilingual");

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // 输出错误信息以便诊断
        if (!result.Success)
        {
            _output.WriteLine($"构建失败，错误数: {result.Errors.Count}");
            foreach (var error in result.Errors)
            {
                _output.WriteLine($"  [{error.ErrorCode}] {error.FilePath}: {error.Message}");
            }
        }

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("多语言站点构建应该成功");

        // 获取输出文件
        var outputFiles = _fixture.GetOutputFiles();
        _output.WriteLine($"多语言站点输出文件数: {outputFiles.Count}");

        // 验证各语言目录存在
        var hasChineseContent = outputFiles.Any(f => f.Contains("zh") || f.Contains("zh-cn"));
        var hasEnglishContent = outputFiles.Any(f => f.Contains("en"));
        var hasJapaneseContent = outputFiles.Any(f => f.Contains("ja"));

        _output.WriteLine($"中文内容: {hasChineseContent}");
        _output.WriteLine($"英文内容: {hasEnglishContent}");
        _output.WriteLine($"日文内容: {hasJapaneseContent}");

        // 输出部分文件列表
        foreach (var file in outputFiles.Take(30))
        {
            _output.WriteLine($"  - {file}");
        }
    }

    #endregion

    #region 边界条件测试

    /// <summary>
    /// 测试空站点构建
    /// 验证没有内容的站点也能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "EdgeCase")]
    public async Task BuildAsync_WithEmptySite_ShouldSucceed()
    {
        // Arrange - 创建最小站点并删除所有内容
        await _fixture.CreateSiteAsync("minimal");

        // 删除内容目录中的所有文件
        var contentDir = Path.Combine(_fixture.SiteRoot, "content");
        if (Directory.Exists(contentDir))
        {
            foreach (var file in Directory.GetFiles(contentDir, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // 输出错误信息用于调试
        if (!result.Success)
        {
            _output.WriteLine($"构建失败，错误数: {result.Errors.Count}");
            foreach (var error in result.Errors)
            {
                _output.WriteLine($"  错误: {error.FilePath} - {error.Message} ({error.ErrorCode})");
            }
        }

        // Assert - 验证构建成功（即使没有内容）
        result.Success.Should().BeTrue("空站点构建应该成功");
        result.Errors.Should().BeEmpty("不应该有错误");

        _output.WriteLine($"空站点构建完成，页面数: {result.PagesBuilt}");
    }

    /// <summary>
    /// 测试大量文件构建
    /// 验证大量文件的站点能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "EdgeCase")]
    [Trait("TestType", "Performance")]
    public async Task BuildAsync_WithManyFiles_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加大量文章
        const int articleCount = 50;
        for (var i = 1; i <= articleCount; i++)
        {
            var content = $"""
                +++
                title = "批量文章 {i}"
                date = 2024-{(i % 12) + 1:D2}-{(i % 28) + 1:D2}T10:00:00+08:00
                draft = false
                tags = ["批量测试", "文章{i % 10}"]
                categories = ["分类{i % 5}"]
                +++

                这是批量生成的文章 {i}。

                ## 内容

                Lorem ipsum dolor sit amet, consectetur adipiscing elit.
                Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.
                """;
            await _fixture.AddContentAsync($"posts/batch-{i:D4}.md", content);
        }

        // Act - 执行构建
        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.BuildAsync();
        stopwatch.Stop();

        // 输出错误信息用于调试
        if (!result.Success)
        {
            _output.WriteLine($"构建失败，错误数: {result.Errors.Count}");
            foreach (var error in result.Errors.Take(10))
            {
                _output.WriteLine($"  错误: {error.FilePath} - {error.Message} ({error.ErrorCode})");
            }
        }

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("大量文件构建应该成功");
        result.PagesBuilt.Should().BeGreaterThanOrEqualTo(articleCount,
            $"应该至少构建 {articleCount} 个页面");

        _output.WriteLine($"大量文件构建完成:");
        _output.WriteLine($"  - 文章数: {articleCount}");
        _output.WriteLine($"  - 构建页面数: {result.PagesBuilt}");
        _output.WriteLine($"  - 耗时: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  - 平均每页: {stopwatch.ElapsedMilliseconds / (double)result.PagesBuilt:F2}ms");
    }

    /// <summary>
    /// 测试深层嵌套目录构建
    /// 验证深层嵌套的内容目录能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "EdgeCase")]
    public async Task BuildAsync_WithDeeplyNestedContent_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加深层嵌套的内容
        var deepPath = "level1/level2/level3/level4/level5";
        var deepContent = """
            +++
            title = "深层嵌套文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是一篇位于深层嵌套目录中的文章。
            """;
        await _fixture.AddContentAsync($"{deepPath}/deep-article.md", deepContent);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("深层嵌套内容构建应该成功");

        // 验证深层嵌套的文章被生成
        var outputFiles = _fixture.GetOutputFiles();
        var deepArticleExists = outputFiles.Any(f => f.Contains("deep-article"));
        deepArticleExists.Should().BeTrue("深层嵌套的文章应该被生成");

        _output.WriteLine($"深层嵌套内容构建完成");
    }

    /// <summary>
    /// 测试特殊字符文件名构建
    /// 验证包含特殊字符的文件名能正确处理
    /// </summary>
    [Fact]
    [Trait("TestType", "EdgeCase")]
    public async Task BuildAsync_WithSpecialCharacterFilenames_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含中文和特殊字符的文件名
        var specialContent = """
            +++
            title = "特殊字符测试"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是一篇文件名包含特殊字符的文章。
            """;
        await _fixture.AddContentAsync("posts/测试文章-2024.md", specialContent);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("特殊字符文件名构建应该成功");

        _output.WriteLine($"特殊字符文件名构建完成");
        _output.WriteLine($"构建页面数: {result.PagesBuilt}");
    }

    /// <summary>
    /// 测试 Unicode 内容构建
    /// 验证包含各种 Unicode 字符的内容能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "EdgeCase")]
    public async Task BuildAsync_WithUnicodeContent_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含各种 Unicode 字符的内容
        var unicodeContent = """
            +++
            title = "Unicode 测试 🎉"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            description = "包含 Emoji 和多语言字符的测试"
            +++

            ## 多语言内容

            - 中文：你好世界
            - 日文：こんにちは世界
            - 韩文：안녕하세요 세계
            - 俄文：Привет мир
            - 阿拉伯文：مرحبا بالعالم

            ## Emoji 测试

            🎉 🚀 💻 🔥 ✨ 🌟 💫 ⭐

            ## 特殊符号

            © ® ™ § ¶ † ‡ • ‣ ◦
            """;
        await _fixture.AddContentAsync("posts/unicode-test.md", unicodeContent);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("Unicode 内容构建应该成功");

        // 验证输出文件存在
        var outputFiles = _fixture.GetOutputFiles();
        var unicodeArticleExists = outputFiles.Any(f => f.Contains("unicode-test"));
        unicodeArticleExists.Should().BeTrue("Unicode 测试文章应该被生成");

        // 验证内容正确
        var articlePath = outputFiles.FirstOrDefault(f => f.Contains("unicode-test"));
        if (articlePath != null)
        {
            var articleHtml = await _fixture.GetOutputFileAsync(articlePath);
            articleHtml.Should().Contain("你好世界", "应该包含中文内容");
            articleHtml.Should().Contain("🎉", "应该包含 Emoji");
        }

        _output.WriteLine("Unicode 内容构建测试通过");
    }

    #endregion

    #region 缓存测试

    /// <summary>
    /// 测试启用缓存的构建
    /// 验证缓存能正确工作
    /// </summary>
    [Fact]
    [Trait("TestType", "Cache")]
    public async Task BuildAsync_WithCacheEnabled_ShouldUseCache()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("complete");

        // Act - 第一次构建（填充缓存）
        var firstBuildOptions = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            EnableCache = true,
            CleanOutput = true
        };

        var firstStopwatch = Stopwatch.StartNew();
        var firstResult = await _fixture.BuildAsync(firstBuildOptions);
        firstStopwatch.Stop();

        firstResult.Success.Should().BeTrue("第一次构建应该成功");
        _output.WriteLine($"第一次构建耗时: {firstStopwatch.ElapsedMilliseconds}ms");

        // 清理输出但保留缓存
        await _fixture.CleanOutputAsync();

        // Act - 第二次构建（使用缓存）
        var secondStopwatch = Stopwatch.StartNew();
        var secondResult = await _fixture.BuildAsync(firstBuildOptions);
        secondStopwatch.Stop();

        secondResult.Success.Should().BeTrue("第二次构建应该成功");
        _output.WriteLine($"第二次构建耗时: {secondStopwatch.ElapsedMilliseconds}ms");

        // Assert - 验证结果一致
        secondResult.PagesBuilt.Should().Be(firstResult.PagesBuilt,
            "两次构建应该生成相同数量的页面");

        // 输出缓存统计
        if (secondResult.Statistics != null)
        {
            _output.WriteLine($"缓存命中率: {secondResult.Statistics.CacheHitRate:P2}");
            _output.WriteLine($"缓存命中: {secondResult.Statistics.CacheHits}");
            _output.WriteLine($"缓存未命中: {secondResult.Statistics.CacheMisses}");
        }
    }

    /// <summary>
    /// 测试禁用缓存的构建
    /// 验证禁用缓存时不使用缓存
    /// </summary>
    [Fact]
    [Trait("TestType", "Cache")]
    public async Task BuildAsync_WithCacheDisabled_ShouldNotUseCache()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // Act - 禁用缓存构建
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            EnableCache = false
        };

        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("禁用缓存的构建应该成功");

        // 验证缓存统计（如果存在）
        if (result.Statistics != null)
        {
            result.Statistics.CacheHits.Should().Be(0, "禁用缓存时不应有缓存命中");
        }

        _output.WriteLine("禁用缓存构建测试通过");
    }

    #endregion
}

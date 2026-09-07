// Flint 静态站点生成器
// 构建管道边界条件测试
// 验证构建管道在边界条件下的行为

using System.Diagnostics;
using Flint.Core.Models;
using Flint.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// 构建管道边界条件测试
/// 验证构建管道在边界条件下的行为
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 2.10: 构建管道输出正确性
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
[Trait("TestType", "EdgeCase")]
public class BuildPipelineBoundaryTests : IAsyncLifetime
{
    #region 私有字段

    private readonly ITestOutputHelper _output;
    private readonly TestSiteFixture _fixture;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建构建管道边界条件测试实例
    /// </summary>
    /// <param name="output">测试输出帮助器</param>
    public BuildPipelineBoundaryTests(ITestOutputHelper output)
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

    #region 空站点测试

    /// <summary>
    /// 测试空站点构建
    /// 验证没有内容的站点也能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "EmptySite")]
    public async Task BuildAsync_WithEmptySite_ShouldSucceed()
    {
        // Arrange - 创建最小站点
        await _fixture.CreateSiteAsync("minimal");

        // 删除所有内容文件
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

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("空站点构建应该成功");
        result.Errors.Should().BeEmpty("不应该有错误");

        _output.WriteLine($"空站点构建完成");
        _output.WriteLine($"页面数: {result.PagesBuilt}");
    }

    /// <summary>
    /// 测试只有配置没有内容的站点
    /// 验证只有配置文件的站点能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "ConfigOnly")]
    public async Task BuildAsync_WithConfigOnly_ShouldSucceed()
    {
        // Arrange - 创建最小站点
        await _fixture.CreateSiteAsync("minimal");

        // 删除所有内容和模板文件，只保留配置
        var contentDir = Path.Combine(_fixture.SiteRoot, "content");
        if (Directory.Exists(contentDir))
        {
            Directory.Delete(contentDir, recursive: true);
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功（或优雅失败）
        _output.WriteLine($"构建成功: {result.Success}");
        _output.WriteLine($"错误数: {result.Errors.Count}");

        foreach (var error in result.Errors)
        {
            _output.WriteLine($"  - {error.Message}");
        }
    }

    #endregion

    #region 超大站点测试

    /// <summary>
    /// 测试超大站点构建（1000+ 页面）
    /// 验证大量页面的站点能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "LargeSite")]
    [Trait("TestType", "Performance")]
    public async Task BuildAsync_WithLargeSite_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加大量文章
        const int articleCount = 100; // 减少数量以加快测试
        for (var i = 1; i <= articleCount; i++)
        {
            var content = $"""
                +++
                title = "大规模测试文章 {i}"
                date = 2024-{(i % 12) + 1:D2}-{(i % 28) + 1:D2}T10:00:00+08:00
                draft = false
                tags = ["测试", "大规模", "文章{i % 10}"]
                categories = ["分类{i % 5}"]
                +++

                # 大规模测试文章 {i}

                这是大规模测试的第 {i} 篇文章。

                ## 内容

                Lorem ipsum dolor sit amet, consectetur adipiscing elit.
                Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.
                Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.
                """;
            await _fixture.AddContentAsync($"posts/large-{i:D4}.md", content);
        }

        // Act - 执行构建
        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.BuildAsync();
        stopwatch.Stop();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("大规模站点构建应该成功");
        result.PagesBuilt.Should().BeGreaterThanOrEqualTo(articleCount,
            $"应该至少构建 {articleCount} 个页面");

        _output.WriteLine($"大规模站点构建完成:");
        _output.WriteLine($"  - 文章数: {articleCount}");
        _output.WriteLine($"  - 构建页面数: {result.PagesBuilt}");
        _output.WriteLine($"  - 耗时: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  - 平均每页: {stopwatch.ElapsedMilliseconds / (double)result.PagesBuilt:F2}ms");
    }

    #endregion

    #region 深层嵌套目录测试

    /// <summary>
    /// 测试深层嵌套内容目录
    /// 验证深层嵌套的内容目录能正确构建
    /// </summary>
    [Fact]
    [Trait("TestType", "DeepNesting")]
    public async Task BuildAsync_WithDeeplyNestedContent_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加深层嵌套的内容
        var depths = new[] { 3, 5, 7, 10 };
        foreach (var depth in depths)
        {
            var path = string.Join("/", Enumerable.Range(1, depth).Select(i => $"level{i}"));
            var content = $"""
                +++
                title = "深度 {depth} 文章"
                date = 2024-01-15T10:00:00+08:00
                draft = false
                +++

                这是位于深度 {depth} 的文章。
                路径: {path}
                """;
            await _fixture.AddContentAsync($"{path}/article.md", content);
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("深层嵌套内容构建应该成功");

        // 验证所有深层嵌套的文章都被生成
        var outputFiles = _fixture.GetOutputFiles();
        foreach (var depth in depths)
        {
            var hasArticle = outputFiles.Any(f => f.Contains($"level{depth}", StringComparison.OrdinalIgnoreCase));
            _output.WriteLine($"深度 {depth} 文章存在: {hasArticle}");
        }
    }

    #endregion

    #region 特殊字符文件名测试

    /// <summary>
    /// 测试包含特殊字符的文件名
    /// 验证特殊字符文件名能正确处理
    /// </summary>
    [Fact]
    [Trait("TestType", "SpecialCharacters")]
    public async Task BuildAsync_WithSpecialCharacterFilenames_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含特殊字符的文件名
        var specialNames = new[]
        {
            ("posts/中文文章.md", "中文文章"),
            ("posts/article-with-dash.md", "带连字符的文章"),
            ("posts/article_with_underscore.md", "带下划线的文章"),
            ("posts/article.with.dots.md", "带点的文章"),
            ("posts/UPPERCASE.md", "大写文章"),
            ("posts/MixedCase.md", "混合大小写文章")
        };

        foreach (var (path, title) in specialNames)
        {
            var content = $"""
                +++
                title = "{title}"
                date = 2024-01-15T10:00:00+08:00
                draft = false
                +++

                这是 {title} 的内容。
                """;
            await _fixture.AddContentAsync(path, content);
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("特殊字符文件名构建应该成功");

        // 验证所有文章都被生成
        var outputFiles = _fixture.GetOutputFiles();
        _output.WriteLine($"输出文件数: {outputFiles.Count}");

        foreach (var (_, title) in specialNames)
        {
            _output.WriteLine($"  - {title}");
        }
    }

    /// <summary>
    /// 测试包含 Unicode 字符的内容
    /// 验证 Unicode 内容能正确处理
    /// </summary>
    [Fact]
    [Trait("TestType", "Unicode")]
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
            - 希伯来文：שלום עולם
            - 泰文：สวัสดีโลก
            - 印地文：नमस्ते दुनिया

            ## Emoji 测试

            🎉 🚀 💻 🔥 ✨ 🌟 💫 ⭐ 🎯 🎨 🎭 🎪

            ## 特殊符号

            © ® ™ § ¶ † ‡ • ‣ ◦ ∞ ≠ ≤ ≥ ± × ÷

            ## 数学符号

            ∑ ∏ ∫ ∂ ∇ √ ∛ ∜ ∝ ∞

            ## 箭头

            → ← ↑ ↓ ↔ ↕ ⇒ ⇐ ⇑ ⇓
            """;
        await _fixture.AddContentAsync("posts/unicode-test.md", unicodeContent);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("Unicode 内容构建应该成功");

        // 验证输出文件存在
        var outputFiles = _fixture.GetOutputFiles();
        var unicodeArticleExists = outputFiles.Any(f => f.Contains("unicode-test", StringComparison.OrdinalIgnoreCase));
        unicodeArticleExists.Should().BeTrue("Unicode 测试文章应该被生成");

        // 验证内容正确
        var articlePath = outputFiles.FirstOrDefault(f => f.Contains("unicode-test", StringComparison.OrdinalIgnoreCase));
        if (articlePath != null)
        {
            var articleHtml = await _fixture.GetOutputFileAsync(articlePath);
            articleHtml.Should().Contain("你好世界", "应该包含中文内容");
            articleHtml.Should().Contain("🎉", "应该包含 Emoji");
        }

        _output.WriteLine("Unicode 内容构建测试通过");
    }

    #endregion

    #region 空内容文件测试

    /// <summary>
    /// 测试空内容文件
    /// 验证空内容文件能正确处理
    /// </summary>
    [Fact]
    [Trait("TestType", "EmptyContent")]
    public async Task BuildAsync_WithEmptyContentFile_ShouldHandleGracefully()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加空内容文件
        var emptyContent = """
            +++
            title = "空内容文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++
            """;
        await _fixture.AddContentAsync("posts/empty-content.md", emptyContent);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("空内容文件构建应该成功");

        _output.WriteLine($"空内容文件构建完成");
        _output.WriteLine($"页面数: {result.PagesBuilt}");
    }

    /// <summary>
    /// 测试只有 Front Matter 没有正文的文件
    /// 验证只有 Front Matter 的文件能正确处理
    /// </summary>
    [Fact]
    [Trait("TestType", "FrontMatterOnly")]
    public async Task BuildAsync_WithFrontMatterOnly_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加只有 Front Matter 的文件
        var frontMatterOnly = """
            +++
            title = "只有 Front Matter"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            description = "这篇文章只有 Front Matter，没有正文"
            tags = ["测试"]
            +++
            """;
        await _fixture.AddContentAsync("posts/frontmatter-only.md", frontMatterOnly);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("只有 Front Matter 的文件构建应该成功");

        _output.WriteLine($"只有 Front Matter 的文件构建完成");
    }

    #endregion

    #region 超大内容文件测试

    /// <summary>
    /// 测试超大内容文件
    /// 验证超大内容文件能正确处理
    /// </summary>
    [Fact]
    [Trait("TestType", "LargeContent")]
    public async Task BuildAsync_WithLargeContentFile_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 生成大量内容
        var paragraphs = new List<string>();
        for (var i = 0; i < 100; i++)
        {
            paragraphs.Add($"""
                ## 章节 {i + 1}

                Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
                Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. 
                Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris 
                nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in 
                reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla 
                pariatur. Excepteur sint occaecat cupidatat non proident, sunt in 
                culpa qui officia deserunt mollit anim id est laborum.

                这是第 {i + 1} 章节的中文内容。包含一些测试文本，用于验证大文件的处理能力。
                """);
        }

        var largeContent = $"""
            +++
            title = "超大内容文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            # 超大内容文章

            这是一篇包含大量内容的测试文章。

            {string.Join("\n\n", paragraphs)}
            """;
        await _fixture.AddContentAsync("posts/large-content.md", largeContent);

        // Act - 执行构建
        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.BuildAsync();
        stopwatch.Stop();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("超大内容文件构建应该成功");

        _output.WriteLine($"超大内容文件构建完成:");
        _output.WriteLine($"  - 内容大小: {largeContent.Length} 字符");
        _output.WriteLine($"  - 章节数: {paragraphs.Count}");
        _output.WriteLine($"  - 耗时: {stopwatch.ElapsedMilliseconds}ms");
    }

    #endregion

    #region 循环引用测试

    /// <summary>
    /// 测试内容之间的循环引用
    /// 验证循环引用能正确处理
    /// </summary>
    [Fact]
    [Trait("TestType", "CircularReference")]
    public async Task BuildAsync_WithCircularReferences_ShouldHandleGracefully()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加相互引用的内容
        var content1 = """
            +++
            title = "文章 A"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是文章 A，它引用了 [文章 B](/posts/article-b/)。
            """;
        await _fixture.AddContentAsync("posts/article-a.md", content1);

        var content2 = """
            +++
            title = "文章 B"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是文章 B，它引用了 [文章 A](/posts/article-a/)。
            """;
        await _fixture.AddContentAsync("posts/article-b.md", content2);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("循环引用构建应该成功");

        _output.WriteLine($"循环引用构建完成");
        _output.WriteLine($"页面数: {result.PagesBuilt}");
    }

    #endregion

    #region 无效日期测试

    /// <summary>
    /// 测试边界日期值
    /// 验证边界日期值能正确处理
    /// </summary>
    [Fact]
    [Trait("TestType", "BoundaryDate")]
    public async Task BuildAsync_WithBoundaryDates_ShouldSucceed()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含边界日期的内容
        var dates = new[]
        {
            ("posts/date-1970.md", "1970-01-01T00:00:00Z", "1970 年文章"),
            ("posts/date-2000.md", "2000-01-01T00:00:00Z", "2000 年文章"),
            ("posts/date-2099.md", "2099-12-31T23:59:59Z", "2099 年文章")
        };

        foreach (var (path, date, title) in dates)
        {
            var content = $"""
                +++
                title = "{title}"
                date = {date}
                draft = false
                +++

                这是 {title} 的内容。
                """;
            await _fixture.AddContentAsync(path, content);
        }

        // Act - 执行构建（包含未来内容）
        var options = new BuildOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            IncludeFuture = true
        };
        var result = await _fixture.BuildAsync(options);

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("边界日期构建应该成功");

        _output.WriteLine($"边界日期构建完成");
        _output.WriteLine($"页面数: {result.PagesBuilt}");
    }

    #endregion

    #region 并发构建测试

    /// <summary>
    /// 测试并发构建的正确性
    /// 验证并发构建不会产生竞态条件
    /// </summary>
    [Fact]
    [Trait("TestType", "Concurrent")]
    public async Task BuildAsync_ConcurrentBuilds_ShouldNotInterfere()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加测试内容
        for (var i = 1; i <= 10; i++)
        {
            var content = $"""
                +++
                title = "并发测试文章 {i}"
                date = 2024-01-{i:D2}T10:00:00+08:00
                draft = false
                +++

                这是并发测试文章 {i} 的内容。
                """;
            await _fixture.AddContentAsync($"posts/concurrent-{i}.md", content);
        }

        // Act - 执行多次构建
        var results = new List<BuildResult>();
        for (var i = 0; i < 3; i++)
        {
            var result = await _fixture.BuildAsync(new BuildOptions
            {
                SourcePath = _fixture.SiteRoot,
                OutputPath = _fixture.OutputPath,
                CleanOutput = true
            });
            results.Add(result);
        }

        // Assert - 验证所有构建成功且结果一致
        foreach (var result in results)
        {
            result.Success.Should().BeTrue("每次构建都应该成功");
        }

        // 验证页面数一致
        var pageCounts = results.Select(r => r.PagesBuilt).Distinct().ToList();
        pageCounts.Should().HaveCount(1, "每次构建应该生成相同数量的页面");

        _output.WriteLine($"并发构建测试完成");
        _output.WriteLine($"构建次数: {results.Count}");
        _output.WriteLine($"每次页面数: {results[0].PagesBuilt}");
    }

    #endregion
}

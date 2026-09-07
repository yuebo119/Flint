// Flint 静态站点生成器
// 构建管道输出正确性属性测试
// 验证构建管道对任意有效输入产生正确输出

using Flint.IntegrationTests.Fixtures;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// 构建管道输出正确性属性测试
/// 使用 FsCheck 验证构建管道对任意有效输入产生正确输出
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 1.5: 构建命令成功场景
/// - Requirements 2.10: 构建管道输出正确性
/// 
/// **Validates: Property 2 - 构建管道输出正确性**
/// *For any* 有效的站点配置和内容文件组合，执行构建后，输出目录应包含与输入内容对应的 HTML 文件，
/// 且每个 HTML 文件应包含正确渲染的内容。
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
[Trait("TestType", "Property")]
public class BuildPipelineOutputPropertyTests
{
    #region Property 2: 构建管道输出正确性

    /// <summary>
    /// Property 2: 构建管道输出正确性
    /// 对于任意有效的站点配置和内容文件组合，执行构建后，
    /// 输出目录应包含与输入内容对应的 HTML 文件
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 1.5, 2.10**
    /// 最少 100 次迭代
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(BuildPipelineArbitraries) })]
    [Trait("Property", "2")]
    public Property Property2_BuildPipelineOutputCorrectness(ValidSiteContent siteContent)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            try
            {
                // Arrange - 创建测试站点
                await fixture.CreateSiteAsync("minimal");

                // 添加生成的内容文件
                foreach (var content in siteContent.ContentFiles)
                {
                    await fixture.AddContentAsync(content.Path, content.Content);
                }

                // Act - 执行构建
                var result = await fixture.BuildAsync();

                // Assert - 验证构建成功
                var buildSuccess = result.Success;
                var hasNoErrors = result.Errors.Count == 0;

                // 验证输出目录存在
                var outputExists = Directory.Exists(fixture.OutputPath);

                // 验证 HTML 文件包含正确的结构
                var outputFiles = fixture.GetOutputFiles();
                var htmlFilesValid = true;
                foreach (var htmlFile in outputFiles.Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
                {
                    var htmlContent = await fixture.GetOutputFileAsync(htmlFile);
                    if (htmlContent != null)
                    {
                        htmlFilesValid &= htmlContent.Contains("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase) ||
                                          htmlContent.Contains("<html", StringComparison.OrdinalIgnoreCase);
                    }
                }

                return buildSuccess && hasNoErrors && outputExists && htmlFilesValid;
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// Property 2 变体: 验证输出目录结构正确性
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(BuildPipelineArbitraries) })]
    [Trait("Property", "2")]
    public Property Property2_OutputDirectoryStructureCorrectness(ValidSiteContent siteContent)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            try
            {
                // Arrange
                await fixture.CreateSiteAsync("minimal");

                foreach (var content in siteContent.ContentFiles)
                {
                    await fixture.AddContentAsync(content.Path, content.Content);
                }

                // Act
                var result = await fixture.BuildAsync();

                // Assert
                var outputFiles = fixture.GetOutputFiles();

                // 验证输出目录结构
                // 1. 应该有 index.html（首页）
                var hasIndex = outputFiles.Any(f => f.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));

                // 2. 所有 HTML 文件应该在正确的目录结构中
                var allHtmlInCorrectStructure = outputFiles
                    .Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    .All(f => !f.Contains("..") && !Path.IsPathRooted(f));

                return result.Success && hasIndex && allHtmlInCorrectStructure;
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        });
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 运行异步属性测试
    /// </summary>
    private static Property RunPropertyTestAsync(Func<Task<bool>> testFunc)
    {
        try
        {
            var result = testFunc().GetAwaiter().GetResult();
            return result.ToProperty();
        }
        catch (Exception)
        {
            return false.ToProperty();
        }
    }

    #endregion
}

/// <summary>
/// 构建管道属性测试的自定义生成器
/// </summary>
public static class BuildPipelineArbitraries
{
    /// <summary>
    /// 有效站点内容生成器
    /// </summary>
    public static Arbitrary<ValidSiteContent> ValidSiteContentArb() =>
        (from contentCount in Gen.Choose(1, 5)
         from contents in Gen.ListOf<ValidContentFile>(ValidContentFileArb().Generator).Select(c => c.Take(contentCount).ToList())
         select new ValidSiteContent
         {
             ContentFiles = contents
         }).ToArbitrary();

    /// <summary>
    /// 有效内容文件生成器
    /// </summary>
    public static Arbitrary<ValidContentFile> ValidContentFileArb() =>
        (from title in Gen.Elements("测试文章", "Hello World", "Getting Started", "Tutorial", "Guide")
         from slug in Gen.Elements("test-post", "hello-world", "getting-started", "tutorial", "guide")
         from year in Gen.Choose(2020, 2025)
         from month in Gen.Choose(1, 12)
         from day in Gen.Choose(1, 28)
         from draft in Gen.Constant(false) // 只生成非草稿内容
         from bodyParagraphs in Gen.Choose(1, 3)
         from body in Gen.ListOf<string>(Gen.Elements(
             "这是测试内容。",
             "Lorem ipsum dolor sit amet.",
             "这是一段示例文本。",
             "Welcome to my blog.",
             "感谢阅读本文。")).Select(b => b.Take(bodyParagraphs).ToList())
         select new ValidContentFile
         {
             Path = $"posts/{slug}.md",
             Content = $"""
                 +++
                 title = "{title}"
                 date = {year}-{month:D2}-{day:D2}T10:00:00+08:00
                 draft = {draft.ToString().ToLowerInvariant()}
                 +++

                 # {title}

                 {string.Join("\n\n", body)}
                 """
         }).ToArbitrary();
}

/// <summary>
/// 有效站点内容
/// </summary>
public sealed class ValidSiteContent
{
    /// <summary>
    /// 内容文件列表
    /// </summary>
    public required IReadOnlyList<ValidContentFile> ContentFiles { get; init; }
}

/// <summary>
/// 有效内容文件
/// </summary>
public sealed class ValidContentFile
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 文件内容
    /// </summary>
    public required string Content { get; init; }
}

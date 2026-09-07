// Flint 静态站点生成器
// 内容过滤正确性属性测试
// 验证草稿和未来内容的过滤行为

using Flint.Core.Models;
using Flint.IntegrationTests.Fixtures;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// 内容过滤正确性属性测试
/// 使用 FsCheck 验证草稿和未来内容的过滤行为
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 2.6: 草稿过滤
/// - Requirements 2.7: 未来内容过滤
/// 
/// **Validates: Property 6 - 内容过滤正确性**
/// *For any* 包含草稿和未来内容的站点，当构建选项禁用草稿/未来内容时，
/// 输出目录不应包含这些内容的 HTML 文件；当启用时，应包含这些文件。
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
[Trait("TestType", "Property")]
public class ContentFilterPropertyTests
{

    #region Property 6: 内容过滤正确性

    /// <summary>
    /// Property 6: 草稿过滤正确性
    /// 对于任意包含草稿标记的内容，当 IncludeDrafts=false 时，
    /// 输出目录不应包含草稿内容的 HTML 文件
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.6**
    /// 最少 100 次迭代
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ContentFilterArbitraries) })]
    [Trait("Property", "6")]
    public Property Property6_DraftFilterCorrectness(DraftContentSet contentSet)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            try
            {
                // Arrange - 创建测试站点
                await fixture.CreateSiteAsync("minimal");

                // 添加草稿内容
                foreach (var draft in contentSet.DraftContents)
                {
                    await fixture.AddContentAsync(draft.Path, draft.Content);
                }

                // 添加正常内容
                foreach (var normal in contentSet.NormalContents)
                {
                    await fixture.AddContentAsync(normal.Path, normal.Content);
                }

                // Act - 不包含草稿的构建
                var excludeDraftsOptions = new BuildOptions
                {
                    SourcePath = fixture.SiteRoot,
                    OutputPath = fixture.OutputPath,
                    IncludeDrafts = false,
                    CleanOutput = true
                };
                var excludeResult = await fixture.BuildAsync(excludeDraftsOptions);

                // 获取不包含草稿时的输出文件
                var excludeOutputFiles = fixture.GetOutputFiles();

                // 验证草稿内容不存在
                var draftsExcluded = contentSet.DraftContents.All(draft =>
                {
                    var slug = Path.GetFileNameWithoutExtension(draft.Path);
                    return !excludeOutputFiles.Any(f => f.Contains(slug, StringComparison.OrdinalIgnoreCase));
                });

                // 验证正常内容存在
                var normalsIncluded = contentSet.NormalContents.All(normal =>
                {
                    var slug = Path.GetFileNameWithoutExtension(normal.Path);
                    return excludeOutputFiles.Any(f => f.Contains(slug, StringComparison.OrdinalIgnoreCase));
                });

                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除

                return excludeResult.Success && draftsExcluded && normalsIncluded;
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// Property 6: 草稿包含正确性
    /// 对于任意包含草稿标记的内容，当 IncludeDrafts=true 时，
    /// 输出目录应包含草稿内容的 HTML 文件
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.6**
    /// 最少 100 次迭代
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ContentFilterArbitraries) })]
    [Trait("Property", "6")]
    public Property Property6_DraftInclusionCorrectness(DraftContentSet contentSet)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            try
            {
                // Arrange - 创建测试站点
                await fixture.CreateSiteAsync("minimal");

                // 添加草稿内容
                foreach (var draft in contentSet.DraftContents)
                {
                    await fixture.AddContentAsync(draft.Path, draft.Content);
                }

                // 添加正常内容
                foreach (var normal in contentSet.NormalContents)
                {
                    await fixture.AddContentAsync(normal.Path, normal.Content);
                }

                // Act - 包含草稿的构建
                var includeDraftsOptions = new BuildOptions
                {
                    SourcePath = fixture.SiteRoot,
                    OutputPath = fixture.OutputPath,
                    IncludeDrafts = true,
                    CleanOutput = true
                };
                var includeResult = await fixture.BuildAsync(includeDraftsOptions);

                // 获取包含草稿时的输出文件
                var includeOutputFiles = fixture.GetOutputFiles();

                // 验证草稿内容存在
                var draftsIncluded = contentSet.DraftContents.All(draft =>
                {
                    var slug = Path.GetFileNameWithoutExtension(draft.Path);
                    return includeOutputFiles.Any(f => f.Contains(slug, StringComparison.OrdinalIgnoreCase));
                });

                // 验证正常内容也存在
                var normalsIncluded = contentSet.NormalContents.All(normal =>
                {
                    var slug = Path.GetFileNameWithoutExtension(normal.Path);
                    return includeOutputFiles.Any(f => f.Contains(slug, StringComparison.OrdinalIgnoreCase));
                });

                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除

                return includeResult.Success && draftsIncluded && normalsIncluded;
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// Property 6: 未来内容过滤正确性
    /// 对于任意包含未来日期的内容，当 IncludeFuture=false 时，
    /// 输出目录不应包含未来内容的 HTML 文件
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.7**
    /// 最少 100 次迭代
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ContentFilterArbitraries) })]
    [Trait("Property", "6")]
    public Property Property6_FutureFilterCorrectness(FutureContentSet contentSet)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            try
            {
                // Arrange - 创建测试站点
                await fixture.CreateSiteAsync("minimal");

                // 添加未来内容
                foreach (var future in contentSet.FutureContents)
                {
                    await fixture.AddContentAsync(future.Path, future.Content);
                }

                // 添加当前内容
                foreach (var current in contentSet.CurrentContents)
                {
                    await fixture.AddContentAsync(current.Path, current.Content);
                }

                // Act - 不包含未来内容的构建
                var excludeFutureOptions = new BuildOptions
                {
                    SourcePath = fixture.SiteRoot,
                    OutputPath = fixture.OutputPath,
                    IncludeFuture = false,
                    CleanOutput = true
                };
                var excludeResult = await fixture.BuildAsync(excludeFutureOptions);

                // 获取不包含未来内容时的输出文件
                var excludeOutputFiles = fixture.GetOutputFiles();

                // 验证未来内容不存在
                var futureExcluded = contentSet.FutureContents.All(future =>
                {
                    var slug = Path.GetFileNameWithoutExtension(future.Path);
                    return !excludeOutputFiles.Any(f => f.Contains(slug, StringComparison.OrdinalIgnoreCase));
                });

                // 验证当前内容存在
                var currentIncluded = contentSet.CurrentContents.All(current =>
                {
                    var slug = Path.GetFileNameWithoutExtension(current.Path);
                    return excludeOutputFiles.Any(f => f.Contains(slug, StringComparison.OrdinalIgnoreCase));
                });

                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除

                return excludeResult.Success && futureExcluded && currentIncluded;
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// Property 6: 未来内容包含正确性
    /// 对于任意包含未来日期的内容，当 IncludeFuture=true 时，
    /// 输出目录应包含未来内容的 HTML 文件
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.7**
    /// 最少 100 次迭代
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ContentFilterArbitraries) })]
    [Trait("Property", "6")]
    public Property Property6_FutureInclusionCorrectness(FutureContentSet contentSet)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            try
            {
                // Arrange - 创建测试站点
                await fixture.CreateSiteAsync("minimal");

                // 添加未来内容
                foreach (var future in contentSet.FutureContents)
                {
                    await fixture.AddContentAsync(future.Path, future.Content);
                }

                // 添加当前内容
                foreach (var current in contentSet.CurrentContents)
                {
                    await fixture.AddContentAsync(current.Path, current.Content);
                }

                // Act - 包含未来内容的构建
                var includeFutureOptions = new BuildOptions
                {
                    SourcePath = fixture.SiteRoot,
                    OutputPath = fixture.OutputPath,
                    IncludeFuture = true,
                    CleanOutput = true
                };
                var includeResult = await fixture.BuildAsync(includeFutureOptions);

                // 获取包含未来内容时的输出文件
                var includeOutputFiles = fixture.GetOutputFiles();

                // 验证未来内容存在
                var futureIncluded = contentSet.FutureContents.All(future =>
                {
                    var slug = Path.GetFileNameWithoutExtension(future.Path);
                    return includeOutputFiles.Any(f => f.Contains(slug, StringComparison.OrdinalIgnoreCase));
                });

                // 验证当前内容也存在
                var currentIncluded = contentSet.CurrentContents.All(current =>
                {
                    var slug = Path.GetFileNameWithoutExtension(current.Path);
                    return includeOutputFiles.Any(f => f.Contains(slug, StringComparison.OrdinalIgnoreCase));
                });

                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除

                return includeResult.Success && futureIncluded && currentIncluded;
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// Property 6: 组合过滤正确性
    /// 对于任意包含草稿和未来内容的站点，过滤选项应正确组合应用
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.6, 2.7**
    /// 最少 100 次迭代
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ContentFilterArbitraries) })]
    [Trait("Property", "6")]
    public Property Property6_CombinedFilterCorrectness(
        MixedContentSet contentSet,
        bool includeDrafts,
        bool includeFuture)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            try
            {
                // Arrange - 创建测试站点
                await fixture.CreateSiteAsync("minimal");

                // 添加所有内容
                foreach (var content in contentSet.AllContents)
                {
                    await fixture.AddContentAsync(content.Path, content.Content);
                }

                // Act - 使用指定的过滤选项构建
                var options = new BuildOptions
                {
                    SourcePath = fixture.SiteRoot,
                    OutputPath = fixture.OutputPath,
                    IncludeDrafts = includeDrafts,
                    IncludeFuture = includeFuture,
                    CleanOutput = true
                };
                var result = await fixture.BuildAsync(options);

                // 获取输出文件
                var outputFiles = fixture.GetOutputFiles();

                // 验证过滤逻辑
                var filterCorrect = contentSet.AllContents.All(content =>
                {
                    var slug = Path.GetFileNameWithoutExtension(content.Path);
                    var existsInOutput = outputFiles.Any(f => f.Contains(slug, StringComparison.OrdinalIgnoreCase));

                    // 判断内容是否应该被包含
                    var shouldInclude = true;
                    if (content.IsDraft && !includeDrafts)
                        shouldInclude = false;
                    if (content.IsFuture && !includeFuture)
                        shouldInclude = false;

                    return existsInOutput == shouldInclude;
                });

                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除
                // 日志输出已移除

                return result.Success && filterCorrect;
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
/// 内容过滤属性测试的自定义生成器
/// </summary>
public static class ContentFilterArbitraries
{
    /// <summary>
    /// 草稿内容集生成器
    /// </summary>
    public static Arbitrary<DraftContentSet> DraftContentSetArb() =>
        (from draftCount in Gen.Choose(1, 3)
         from normalCount in Gen.Choose(1, 3)
         from drafts in Gen.ListOf<string>(DraftContentArb().Generator).Select(d => d.Take(draftCount).ToList())
         from normals in Gen.ListOf<string>(NormalContentArb().Generator).Select(n => n.Take(normalCount).ToList())
         select new DraftContentSet
         {
             DraftContents = drafts.Select((d, i) => new FilterableContent
             {
                 Path = $"posts/draft-{i + 1}.md",
                 Content = d,
                 IsDraft = true,
                 IsFuture = false
             }).ToList(),
             NormalContents = normals.Select((n, i) => new FilterableContent
             {
                 Path = $"posts/normal-{i + 1}.md",
                 Content = n,
                 IsDraft = false,
                 IsFuture = false
             }).ToList()
         }).ToArbitrary();

    /// <summary>
    /// 未来内容集生成器
    /// </summary>
    public static Arbitrary<FutureContentSet> FutureContentSetArb() =>
        (from futureCount in Gen.Choose(1, 3)
         from currentCount in Gen.Choose(1, 3)
         from futures in Gen.ListOf<string>(FutureContentArb().Generator).Select(f => f.Take(futureCount).ToList())
         from currents in Gen.ListOf<string>(CurrentContentArb().Generator).Select(c => c.Take(currentCount).ToList())
         select new FutureContentSet
         {
             FutureContents = futures.Select((f, i) => new FilterableContent
             {
                 Path = $"posts/future-{i + 1}.md",
                 Content = f,
                 IsDraft = false,
                 IsFuture = true
             }).ToList(),
             CurrentContents = currents.Select((c, i) => new FilterableContent
             {
                 Path = $"posts/current-{i + 1}.md",
                 Content = c,
                 IsDraft = false,
                 IsFuture = false
             }).ToList()
         }).ToArbitrary();

    /// <summary>
    /// 混合内容集生成器
    /// </summary>
    public static Arbitrary<MixedContentSet> MixedContentSetArb() =>
        (from draftCount in Gen.Choose(1, 2)
         from futureCount in Gen.Choose(1, 2)
         from normalCount in Gen.Choose(1, 2)
         from drafts in Gen.ListOf<string>(DraftContentArb().Generator).Select(d => d.Take(draftCount).ToList())
         from futures in Gen.ListOf<string>(FutureContentArb().Generator).Select(f => f.Take(futureCount).ToList())
         from normals in Gen.ListOf<string>(NormalContentArb().Generator).Select(n => n.Take(normalCount).ToList())
         let allContents = new List<FilterableContent>()
         select new MixedContentSet
         {
             AllContents = drafts.Select((d, i) => new FilterableContent
             {
                 Path = $"posts/draft-{i + 1}.md",
                 Content = d,
                 IsDraft = true,
                 IsFuture = false
             })
                 .Concat(futures.Select((f, i) => new FilterableContent
                 {
                     Path = $"posts/future-{i + 1}.md",
                     Content = f,
                     IsDraft = false,
                     IsFuture = true
                 }))
                 .Concat(normals.Select((n, i) => new FilterableContent
                 {
                     Path = $"posts/normal-{i + 1}.md",
                     Content = n,
                     IsDraft = false,
                     IsFuture = false
                 }))
                 .ToList()
         }).ToArbitrary();

    /// <summary>
    /// 草稿内容生成器
    /// </summary>
    private static Arbitrary<string> DraftContentArb() =>
        (from title in Gen.Elements("草稿文章", "Draft Post", "WIP Article")
         from year in Gen.Choose(2020, 2024)
         from month in Gen.Choose(1, 12)
         from day in Gen.Choose(1, 28)
         select $"""
             +++
             title = "{title}"
             date = {year}-{month:D2}-{day:D2}T10:00:00+08:00
             draft = true
             +++

             # {title}

             这是草稿内容。
             """).ToArbitrary();

    /// <summary>
    /// 正常内容生成器
    /// </summary>
    private static Arbitrary<string> NormalContentArb() =>
        (from title in Gen.Elements("正常文章", "Normal Post", "Published Article")
         from year in Gen.Choose(2020, 2024)
         from month in Gen.Choose(1, 12)
         from day in Gen.Choose(1, 28)
         select $"""
             +++
             title = "{title}"
             date = {year}-{month:D2}-{day:D2}T10:00:00+08:00
             draft = false
             +++

             # {title}

             这是正常内容。
             """).ToArbitrary();

    /// <summary>
    /// 未来内容生成器
    /// </summary>
    private static Arbitrary<string> FutureContentArb() =>
        (from title in Gen.Elements("未来文章", "Future Post", "Scheduled Article")
         from daysInFuture in Gen.Choose(30, 365)
         let futureDate = DateTime.Now.AddDays(daysInFuture)
         select $"""
             +++
             title = "{title}"
             date = {futureDate:yyyy-MM-dd}T10:00:00+08:00
             draft = false
             +++

             # {title}

             这是未来内容。
             """).ToArbitrary();

    /// <summary>
    /// 当前内容生成器
    /// </summary>
    private static Arbitrary<string> CurrentContentArb() =>
        (from title in Gen.Elements("当前文章", "Current Post", "Recent Article")
         from daysAgo in Gen.Choose(1, 30)
         let pastDate = DateTime.Now.AddDays(-daysAgo)
         select $"""
             +++
             title = "{title}"
             date = {pastDate:yyyy-MM-dd}T10:00:00+08:00
             draft = false
             +++

             # {title}

             这是当前内容。
             """).ToArbitrary();
}

/// <summary>
/// 可过滤的内容
/// </summary>
public sealed class FilterableContent
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 文件内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 是否为草稿
    /// </summary>
    public bool IsDraft { get; init; }

    /// <summary>
    /// 是否为未来内容
    /// </summary>
    public bool IsFuture { get; init; }
}

/// <summary>
/// 草稿内容集
/// </summary>
public sealed class DraftContentSet
{
    /// <summary>
    /// 草稿内容列表
    /// </summary>
    public required IReadOnlyList<FilterableContent> DraftContents { get; init; }

    /// <summary>
    /// 正常内容列表
    /// </summary>
    public required IReadOnlyList<FilterableContent> NormalContents { get; init; }
}

/// <summary>
/// 未来内容集
/// </summary>
public sealed class FutureContentSet
{
    /// <summary>
    /// 未来内容列表
    /// </summary>
    public required IReadOnlyList<FilterableContent> FutureContents { get; init; }

    /// <summary>
    /// 当前内容列表
    /// </summary>
    public required IReadOnlyList<FilterableContent> CurrentContents { get; init; }
}

/// <summary>
/// 混合内容集
/// </summary>
public sealed class MixedContentSet
{
    /// <summary>
    /// 所有内容列表
    /// </summary>
    public required IReadOnlyList<FilterableContent> AllContents { get; init; }
}


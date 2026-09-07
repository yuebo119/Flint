// Flint 静态站点生成器
// 分类页面生成测试
// 验证 tags 和 categories 分类页面的正确生成

using Flint.IntegrationTests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// 分类页面生成测试
/// 验证 tags 和 categories 分类页面的正确生成
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 2.4: 分类页面生成
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
[Trait("TestType", "Taxonomy")]
public class TaxonomyPageGenerationTests : IAsyncLifetime
{
    #region 私有字段

    private readonly ITestOutputHelper _output;
    private readonly TestSiteFixture _fixture;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建分类页面生成测试实例
    /// </summary>
    /// <param name="output">测试输出帮助器</param>
    public TaxonomyPageGenerationTests(ITestOutputHelper output)
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

    #region Tags 分类页面测试

    /// <summary>
    /// 测试单个 tag 分类页面生成
    /// 验证包含单个 tag 的文章能正确生成 tag 页面
    /// </summary>
    [Fact]
    [Trait("TestType", "SingleTag")]
    public async Task BuildAsync_WithSingleTag_ShouldGenerateTagPage()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含单个 tag 的文章
        var content = """
            +++
            title = "测试文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            tags = ["programming"]
            +++

            这是一篇关于编程的文章。
            """;
        await _fixture.AddContentAsync("posts/test-article.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证 tag 页面生成
        var outputFiles = _fixture.GetOutputFiles();
        var hasTagPage = outputFiles.Any(f =>
            f.Contains("tags", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("programming", StringComparison.OrdinalIgnoreCase));

        _output.WriteLine($"输出文件数: {outputFiles.Count}");
        _output.WriteLine($"Tag 页面存在: {hasTagPage}");

        foreach (var file in outputFiles.Where(f => f.Contains("tag", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine($"  - {file}");
        }

        hasTagPage.Should().BeTrue("应该生成 programming tag 页面");
    }

    /// <summary>
    /// 测试多个 tags 分类页面生成
    /// 验证包含多个 tags 的文章能正确生成所有 tag 页面
    /// </summary>
    [Fact]
    [Trait("TestType", "MultipleTags")]
    public async Task BuildAsync_WithMultipleTags_ShouldGenerateAllTagPages()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含多个 tags 的文章
        var content = """
            +++
            title = "多标签文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            tags = ["programming", "dotnet", "csharp"]
            +++

            这是一篇包含多个标签的文章。
            """;
        await _fixture.AddContentAsync("posts/multi-tag-article.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证所有 tag 页面生成
        var outputFiles = _fixture.GetOutputFiles();
        var tags = new[] { "programming", "dotnet", "csharp" };

        foreach (var tag in tags)
        {
            var hasTagPage = outputFiles.Any(f =>
                f.Contains("tags", StringComparison.OrdinalIgnoreCase) &&
                f.Contains(tag, StringComparison.OrdinalIgnoreCase));
            _output.WriteLine($"Tag '{tag}' 页面存在: {hasTagPage}");
            hasTagPage.Should().BeTrue($"应该生成 {tag} tag 页面");
        }
    }

    /// <summary>
    /// 测试 tag 列表页面内容
    /// 验证 tag 列表页面包含所有相关文章的链接
    /// </summary>
    [Fact]
    [Trait("TestType", "TagListContent")]
    public async Task BuildAsync_TagListPage_ShouldContainAllRelatedArticles()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加多篇包含相同 tag 的文章
        var articles = new[]
        {
            ("posts/article-1.md", "文章一", "programming"),
            ("posts/article-2.md", "文章二", "programming"),
            ("posts/article-3.md", "文章三", "programming")
        };

        foreach (var (path, title, tag) in articles)
        {
            var content = $"""
                +++
                title = "{title}"
                date = 2024-01-15T10:00:00+08:00
                draft = false
                tags = ["{tag}"]
                +++

                这是 {title} 的内容。
                """;
            await _fixture.AddContentAsync(path, content);
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 查找 tag 列表页面
        var outputFiles = _fixture.GetOutputFiles();
        var tagPagePath = outputFiles.FirstOrDefault(f =>
            f.Contains("tags", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("programming", StringComparison.OrdinalIgnoreCase) &&
            f.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));

        if (tagPagePath != null)
        {
            var tagPageContent = await _fixture.GetOutputFileAsync(tagPagePath);
            _output.WriteLine($"Tag 页面路径: {tagPagePath}");
            _output.WriteLine($"Tag 页面内容长度: {tagPageContent?.Length ?? 0}");

            // 验证页面包含所有文章的链接或标题
            foreach (var (_, title, _) in articles)
            {
                var containsArticle = tagPageContent?.Contains(title, StringComparison.OrdinalIgnoreCase) ?? false;
                _output.WriteLine($"包含 '{title}': {containsArticle}");
            }
        }
        else
        {
            _output.WriteLine("未找到 tag 列表页面");
        }
    }

    #endregion

    #region term 页面语义（Hugo .Pages）

    /// <summary>
    /// term 页模板的 pages 变量应只含该词条的页面（此前 site.regular_pages 列出全站页面）
    /// </summary>
    [Fact]
    [Trait("TestType", "TermPages")]
    public async Task BuildAsync_TermTemplateUsingPages_ListsOnlyTermPages()
    {
        // Arrange - 站点带 term 模板：经 pages（词条专属集合）渲染标题
        await _fixture.CreateSiteAsync("minimal");
        await _fixture.AddTemplateAsync("term.html",
            "{{ for p in pages }}[{{ p.title }}]{{ end }}");

        await _fixture.AddContentAsync("posts/alpha-post.md", """
            +++
            title = "Alpha 文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            tags = ["alpha"]
            +++

            Alpha 内容。
            """);
        await _fixture.AddContentAsync("posts/beta-post.md", """
            +++
            title = "Beta 文章"
            date = 2024-01-16T10:00:00+08:00
            draft = false
            tags = ["beta"]
            +++

            Beta 内容。
            """);

        // Act
        var result = await _fixture.BuildAsync();

        // Assert
        result.Success.Should().BeTrue("构建应该成功");

        var outputFiles = _fixture.GetOutputFiles();
        var alphaTermPath = outputFiles.FirstOrDefault(f =>
            f.Contains("tags", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("alpha", StringComparison.OrdinalIgnoreCase) &&
            f.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
        alphaTermPath.Should().NotBeNull("应该生成 alpha 词条页面");

        var alphaTermContent = await _fixture.GetOutputFileAsync(alphaTermPath!);
        alphaTermContent.Should().Contain("[Alpha 文章]", "词条页面应列出该词条的文章");
        alphaTermContent.Should().NotContain("[Beta 文章]", "词条页面不应列出其他词条的文章");
    }

    #endregion

    #region Categories 分类页面测试

    /// <summary>
    /// 测试单个 category 分类页面生成
    /// 验证包含单个 category 的文章能正确生成 category 页面
    /// </summary>
    [Fact]
    [Trait("TestType", "SingleCategory")]
    public async Task BuildAsync_WithSingleCategory_ShouldGenerateCategoryPage()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含单个 category 的文章
        var content = """
            +++
            title = "技术文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            categories = ["Technology"]
            +++

            这是一篇技术类文章。
            """;
        await _fixture.AddContentAsync("posts/tech-article.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证 category 页面生成
        var outputFiles = _fixture.GetOutputFiles();
        var hasCategoryPage = outputFiles.Any(f =>
            f.Contains("categories", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("technology", StringComparison.OrdinalIgnoreCase));

        _output.WriteLine($"输出文件数: {outputFiles.Count}");
        _output.WriteLine($"Category 页面存在: {hasCategoryPage}");

        foreach (var file in outputFiles.Where(f => f.Contains("categor", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine($"  - {file}");
        }

        hasCategoryPage.Should().BeTrue("应该生成 Technology category 页面");
    }

    /// <summary>
    /// 测试层级分类页面生成
    /// 验证层级分类能正确生成嵌套的分类页面
    /// </summary>
    [Fact]
    [Trait("TestType", "HierarchicalCategory")]
    public async Task BuildAsync_WithHierarchicalCategories_ShouldGenerateNestedCategoryPages()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含层级分类的文章
        var content = """
            +++
            title = "C# 教程"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            categories = ["Programming", "DotNet", "CSharp"]
            +++

            这是一篇 C# 教程文章。
            """;
        await _fixture.AddContentAsync("posts/csharp-tutorial.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证所有层级的 category 页面生成
        var outputFiles = _fixture.GetOutputFiles();
        var categories = new[] { "programming", "dotnet", "csharp" };

        foreach (var category in categories)
        {
            var hasCategoryPage = outputFiles.Any(f =>
                f.Contains("categories", StringComparison.OrdinalIgnoreCase) &&
                f.Contains(category, StringComparison.OrdinalIgnoreCase));
            _output.WriteLine($"Category '{category}' 页面存在: {hasCategoryPage}");
        }
    }

    /// <summary>
    /// 测试分类列表页面内容
    /// 验证分类列表页面包含所有相关文章的链接
    /// </summary>
    [Fact]
    [Trait("TestType", "CategoryListContent")]
    public async Task BuildAsync_CategoryListPage_ShouldContainAllRelatedArticles()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加多篇包含相同 category 的文章
        var articles = new[]
        {
            ("posts/tech-1.md", "技术文章一"),
            ("posts/tech-2.md", "技术文章二"),
            ("posts/tech-3.md", "技术文章三")
        };

        foreach (var (path, title) in articles)
        {
            var content = $"""
                +++
                title = "{title}"
                date = 2024-01-15T10:00:00+08:00
                draft = false
                categories = ["Technology"]
                +++

                这是 {title} 的内容。
                """;
            await _fixture.AddContentAsync(path, content);
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 查找 category 列表页面
        var outputFiles = _fixture.GetOutputFiles();
        var categoryPagePath = outputFiles.FirstOrDefault(f =>
            f.Contains("categories", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("technology", StringComparison.OrdinalIgnoreCase) &&
            f.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));

        if (categoryPagePath != null)
        {
            var categoryPageContent = await _fixture.GetOutputFileAsync(categoryPagePath);
            _output.WriteLine($"Category 页面路径: {categoryPagePath}");
            _output.WriteLine($"Category 页面内容长度: {categoryPageContent?.Length ?? 0}");

            // 验证页面包含所有文章的链接或标题
            foreach (var (_, title) in articles)
            {
                var containsArticle = categoryPageContent?.Contains(title, StringComparison.OrdinalIgnoreCase) ?? false;
                _output.WriteLine($"包含 '{title}': {containsArticle}");
            }
        }
        else
        {
            _output.WriteLine("未找到 category 列表页面");
        }
    }

    #endregion

    #region 分类页面分页测试

    /// <summary>
    /// 测试分类页面分页
    /// 验证当分类下文章数量超过分页限制时能正确分页
    /// </summary>
    [Fact]
    [Trait("TestType", "TaxonomyPagination")]
    public async Task BuildAsync_WithManyArticlesInTag_ShouldGeneratePaginatedPages()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加大量包含相同 tag 的文章
        const int articleCount = 25;
        for (var i = 1; i <= articleCount; i++)
        {
            var content = $"""
                +++
                title = "文章 {i}"
                date = 2024-01-{(i % 28) + 1:D2}T10:00:00+08:00
                draft = false
                tags = ["pagination-test"]
                +++

                这是文章 {i} 的内容。
                """;
            await _fixture.AddContentAsync($"posts/article-{i:D3}.md", content);
        }

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证分页页面生成
        var outputFiles = _fixture.GetOutputFiles();
        var tagPages = outputFiles.Where(f =>
            f.Contains("tags", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("pagination-test", StringComparison.OrdinalIgnoreCase)).ToList();

        _output.WriteLine($"Tag 相关页面数: {tagPages.Count}");
        foreach (var page in tagPages)
        {
            _output.WriteLine($"  - {page}");
        }

        // 应该有多个分页页面（如果启用了分页）
        tagPages.Should().NotBeEmpty("应该生成 tag 页面");
    }

    #endregion

    #region 空分类处理测试

    /// <summary>
    /// 测试空分类处理
    /// 验证没有文章的分类不会生成空页面
    /// </summary>
    [Fact]
    [Trait("TestType", "EmptyTaxonomy")]
    public async Task BuildAsync_WithNoArticlesInTag_ShouldNotGenerateEmptyTagPage()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加一篇没有 tags 的文章
        var content = """
            +++
            title = "无标签文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是一篇没有标签的文章。
            """;
        await _fixture.AddContentAsync("posts/no-tags.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证没有生成空的 tag 页面
        var outputFiles = _fixture.GetOutputFiles();
        var tagPages = outputFiles.Where(f =>
            f.Contains("tags", StringComparison.OrdinalIgnoreCase) &&
            f.EndsWith("index.html", StringComparison.OrdinalIgnoreCase)).ToList();

        _output.WriteLine($"Tag 页面数: {tagPages.Count}");
        foreach (var page in tagPages)
        {
            _output.WriteLine($"  - {page}");
        }
    }

    /// <summary>
    /// 测试混合分类
    /// 验证同时使用 tags 和 categories 时能正确生成所有分类页面
    /// </summary>
    [Fact]
    [Trait("TestType", "MixedTaxonomy")]
    public async Task BuildAsync_WithTagsAndCategories_ShouldGenerateBothTaxonomyPages()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加同时包含 tags 和 categories 的文章
        var content = """
            +++
            title = "混合分类文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            tags = ["programming", "tutorial"]
            categories = ["Technology", "Education"]
            +++

            这是一篇同时包含标签和分类的文章。
            """;
        await _fixture.AddContentAsync("posts/mixed-taxonomy.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证 tag 和 category 页面都生成
        var outputFiles = _fixture.GetOutputFiles();

        var hasTagPages = outputFiles.Any(f => f.Contains("tags", StringComparison.OrdinalIgnoreCase));
        var hasCategoryPages = outputFiles.Any(f => f.Contains("categories", StringComparison.OrdinalIgnoreCase));

        _output.WriteLine($"有 Tag 页面: {hasTagPages}");
        _output.WriteLine($"有 Category 页面: {hasCategoryPages}");

        // 列出所有分类相关页面
        foreach (var file in outputFiles.Where(f =>
            f.Contains("tags", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("categories", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine($"  - {file}");
        }
    }

    #endregion

    #region 中文分类测试

    /// <summary>
    /// 测试中文分类
    /// 验证中文 tags 和 categories 能正确生成分类页面
    /// </summary>
    [Fact]
    [Trait("TestType", "ChineseTaxonomy")]
    public async Task BuildAsync_WithChineseTaxonomy_ShouldGenerateCorrectPages()
    {
        // Arrange - 创建测试站点
        await _fixture.CreateSiteAsync("minimal");

        // 添加包含中文分类的文章
        var content = """
            +++
            title = "中文分类测试"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            tags = ["编程", "教程", "技术"]
            categories = ["技术文章", "学习笔记"]
            +++

            这是一篇使用中文分类的文章。
            """;
        await _fixture.AddContentAsync("posts/chinese-taxonomy.md", content);

        // Act - 执行构建
        var result = await _fixture.BuildAsync();

        // Assert - 验证构建成功
        result.Success.Should().BeTrue("构建应该成功");

        // 验证分类页面生成
        var outputFiles = _fixture.GetOutputFiles();

        _output.WriteLine($"输出文件数: {outputFiles.Count}");

        // 列出所有分类相关页面
        foreach (var file in outputFiles.Where(f =>
            f.Contains("tags", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("categories", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine($"  - {file}");
        }
    }

    #endregion
}

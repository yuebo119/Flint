// Flint 静态站点生成器
// 分类系统服务单元测试

using Flint.Core.Abstractions;
using Flint.Core.Site;
using Xunit;
using SiteTaxonomyConfig = Flint.Core.Site.TaxonomyConfig;

namespace Flint.Core.Tests.Site;

/// <summary>
/// TaxonomyService 单元测试
/// </summary>
public class TaxonomyServiceTests
{
    private readonly TaxonomyService _service;

    public TaxonomyServiceTests()
    {
        _service = new TaxonomyService("https://example.com");
    }

    #region GenerateSlug 测试

    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("C# Programming", "c-programming")]
    [InlineData("Test 123", "test-123")]
    [InlineData("Multiple   Spaces", "multiple-spaces")]
    [InlineData("  Trim  ", "trim")]
    [InlineData("中文标签", "中文标签")]
    public void GenerateSlug_应该生成正确的slug(string input, string expected)
    {
        // Act
        var slug = TaxonomyService.GenerateSlug(input);

        // Assert
        Assert.Equal(expected, slug);
    }

    [Fact]
    public void GenerateSlug_空字符串返回空()
    {
        // Act
        var slug = TaxonomyService.GenerateSlug("");

        // Assert
        Assert.Equal("", slug);
    }

    [Fact]
    public void GenerateSlug_空白字符串返回空()
    {
        // Act
        var slug = TaxonomyService.GenerateSlug("   ");

        // Assert
        Assert.Equal("", slug);
    }

    #endregion

    #region GetTaxonomyNames 测试

    [Fact]
    public void GetTaxonomyNames_应该返回默认分类()
    {
        // Act
        var names = _service.GetTaxonomyNames();

        // Assert
        Assert.Contains("tags", names);
        Assert.Contains("categories", names);
    }

    #endregion

    #region GetTaxonomyConfig 测试

    [Fact]
    public void GetTaxonomyConfig_存在的分类应该返回配置()
    {
        // Act
        var config = _service.GetTaxonomyConfig("tags");

        // Assert
        Assert.NotNull(config);
        Assert.Equal("tag", config.Singular);
        Assert.Equal("tags", config.Plural);
    }

    [Fact]
    public void GetTaxonomyConfig_不存在的分类应该返回null()
    {
        // Act
        var config = _service.GetTaxonomyConfig("nonexistent");

        // Assert
        Assert.Null(config);
    }

    #endregion

    #region AddTaxonomy 测试

    [Fact]
    public void AddTaxonomy_应该添加自定义分类()
    {
        // Arrange
        var config = new SiteTaxonomyConfig
        {
            Singular = "author",
            Plural = "authors",
            SortBy = TaxonomySortBy.Name
        };

        // Act
        _service.AddTaxonomy("authors", config);

        // Assert
        var names = _service.GetTaxonomyNames();
        Assert.Contains("authors", names);
    }

    #endregion

    #region GenerateTaxonomyListPermalink 测试

    [Fact]
    public void GenerateTaxonomyListPermalink_应该生成正确的链接()
    {
        // Act
        var permalink = _service.GenerateTaxonomyListPermalink("tags");

        // Assert
        Assert.Equal("https://example.com/tags/", permalink);
    }

    [Fact]
    public void GenerateTaxonomyListPermalink_应该处理大写()
    {
        // Act
        var permalink = _service.GenerateTaxonomyListPermalink("Categories");

        // Assert
        Assert.Equal("https://example.com/categories/", permalink);
    }

    #endregion

    #region BuildTaxonomies 测试

    [Fact]
    public void BuildTaxonomies_应该构建分类集合()
    {
        // Arrange
        var pages = CreateTestPages();

        // Act
        var taxonomies = _service.BuildTaxonomies(pages);

        // Assert
        Assert.NotNull(taxonomies);
        Assert.True(taxonomies.Taxonomies.ContainsKey("tags"));
        Assert.True(taxonomies.Taxonomies.ContainsKey("categories"));
    }

    [Fact]
    public void BuildTaxonomies_应该正确统计标签()
    {
        // Arrange
        var pages = CreateTestPages();

        // Act
        var taxonomies = _service.BuildTaxonomies(pages);
        var tags = taxonomies.Taxonomies["tags"];

        // Assert
        Assert.NotEmpty(tags);
        var csharpTag = tags.FirstOrDefault(t => t.Name.Equals("C#", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(csharpTag);
        Assert.Equal(2, csharpTag.Count);
    }

    [Fact]
    public void BuildTaxonomies_应该正确统计分类()
    {
        // Arrange
        var pages = CreateTestPages();

        // Act
        var taxonomies = _service.BuildTaxonomies(pages);
        var categories = taxonomies.Taxonomies["categories"];

        // Assert
        Assert.NotEmpty(categories);
        var techCategory = categories.FirstOrDefault(t => t.Name.Equals("技术", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(techCategory);
    }

    [Fact]
    public void BuildTaxonomies_空页面列表应该返回空分类()
    {
        // Arrange
        var pages = new List<PageContext>();

        // Act
        var taxonomies = _service.BuildTaxonomies(pages);

        // Assert
        Assert.Empty(taxonomies.Taxonomies["tags"]);
        Assert.Empty(taxonomies.Taxonomies["categories"]);
    }

    #endregion

    #region 辅助方法

    private static List<PageContext> CreateTestPages()
    {
        return
        [
            new PageContext
            {
                Title = "C# 入门",
                Content = "内容",
                Permalink = "/posts/csharp-intro/",
                RelPermalink = "/posts/csharp-intro/",
                Date = DateTimeOffset.Now.AddDays(-1),
                Tags = ["C#", ".NET"],
                Categories = ["技术"],
                WordCount = 100,
                ReadingTime = TimeSpan.FromMinutes(1),
                Params = new Dictionary<string, object>()
            },
            new PageContext
            {
                Title = "C# 进阶",
                Content = "内容",
                Permalink = "/posts/csharp-advanced/",
                RelPermalink = "/posts/csharp-advanced/",
                Date = DateTimeOffset.Now.AddDays(-2),
                Tags = ["C#", "高级"],
                Categories = ["技术"],
                WordCount = 200,
                ReadingTime = TimeSpan.FromMinutes(2),
                Params = new Dictionary<string, object>()
            },
            new PageContext
            {
                Title = "生活随笔",
                Content = "内容",
                Permalink = "/posts/life/",
                RelPermalink = "/posts/life/",
                Date = DateTimeOffset.Now.AddDays(-3),
                Tags = ["生活"],
                Categories = ["随笔"],
                WordCount = 50,
                ReadingTime = TimeSpan.FromMinutes(1),
                Params = new Dictionary<string, object>()
            }
        ];
    }

    #endregion
}

/// <summary>
/// TaxonomyConfig 单元测试
/// </summary>
public class SiteTaxonomyConfigTests
{
    [Fact]
    public void TaxonomyConfig_应该正确设置属性()
    {
        // Arrange & Act
        var config = new SiteTaxonomyConfig
        {
            Singular = "tag",
            Plural = "tags",
            SortBy = TaxonomySortBy.Count,
            UsePluralInUrl = true
        };

        // Assert
        Assert.Equal("tag", config.Singular);
        Assert.Equal("tags", config.Plural);
        Assert.Equal(TaxonomySortBy.Count, config.SortBy);
        Assert.True(config.UsePluralInUrl);
    }

    [Fact]
    public void TaxonomyConfig_默认值()
    {
        // Arrange & Act
        var config = new SiteTaxonomyConfig
        {
            Singular = "tag",
            Plural = "tags"
        };

        // Assert
        Assert.Equal(TaxonomySortBy.Name, config.SortBy);
        Assert.True(config.UsePluralInUrl);
    }
}

/// <summary>
/// TaxonomyTerm 单元测试
/// </summary>
public class TaxonomyTermTests
{
    [Fact]
    public void TaxonomyTerm_Count应该返回页面数量()
    {
        // Arrange
        var pages = new List<PageContext>
        {
            CreateTestPage("Page 1"),
            CreateTestPage("Page 2"),
            CreateTestPage("Page 3")
        };

        var term = new TaxonomyTerm
        {
            Name = "Test",
            Slug = "test",
            Pages = pages,
            Permalink = "/tags/test/"
        };

        // Assert
        Assert.Equal(3, term.Count);
    }

    private static PageContext CreateTestPage(string title)
    {
        return new PageContext
        {
            Title = title,
            Content = "内容",
            Permalink = $"/posts/{title.ToLowerInvariant().Replace(" ", "-")}/",
            RelPermalink = $"/posts/{title.ToLowerInvariant().Replace(" ", "-")}/",
            Date = DateTimeOffset.Now,
            Tags = [],
            Categories = [],
            WordCount = 100,
            ReadingTime = TimeSpan.FromMinutes(1),
            Params = new Dictionary<string, object>()
        };
    }
}

/// <summary>
/// TaxonomyCollection 单元测试
/// </summary>
public class TaxonomyCollectionTests
{
    [Fact]
    public void TaxonomyCollection_应该正确存储分类()
    {
        // Arrange
        var taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>
        {
            ["tags"] = new List<TaxonomyTerm>
            {
                new() { Name = "C#", Slug = "c", Pages = [], Permalink = "/tags/c/" }
            },
            ["categories"] = new List<TaxonomyTerm>
            {
                new() { Name = "技术", Slug = "tech", Pages = [], Permalink = "/categories/tech/" }
            }
        };

        // Act
        var collection = new TaxonomyCollection { Taxonomies = taxonomies };

        // Assert
        Assert.Equal(2, collection.Taxonomies.Count);
        Assert.Single(collection.Taxonomies["tags"]);
        Assert.Single(collection.Taxonomies["categories"]);
    }
}

/// <summary>
/// TaxonomyPageGenerator 单元测试
/// </summary>
public class TaxonomyPageGeneratorTests
{
    [Fact]
    public void GeneratePages_应该生成分类列表页()
    {
        // Arrange
        var taxonomyService = new TaxonomyService("https://example.com");
        var paginationService = new PaginationService();
        var generator = new TaxonomyPageGenerator(taxonomyService, paginationService);

        var taxonomies = new TaxonomyCollection
        {
            Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>
            {
                ["tags"] = new List<TaxonomyTerm>
                {
                    new() { Name = "C#", Slug = "c", Pages = [], Permalink = "/tags/c/" }
                }
            }
        };

        // Act
        var pages = generator.GeneratePages(taxonomies);

        // Assert
        var listPage = pages.FirstOrDefault(p => p.PageType == TaxonomyPageType.TaxonomyList);
        Assert.NotNull(listPage);
        Assert.Equal("tags", listPage.TaxonomyName);
    }

    [Fact]
    public void GeneratePages_应该生成术语页面()
    {
        // Arrange
        var taxonomyService = new TaxonomyService("https://example.com");
        var paginationService = new PaginationService();
        var generator = new TaxonomyPageGenerator(taxonomyService, paginationService);

        var testPages = new List<PageContext>
        {
            CreateTestPage("Page 1"),
            CreateTestPage("Page 2")
        };

        var taxonomies = new TaxonomyCollection
        {
            Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>
            {
                ["tags"] = new List<TaxonomyTerm>
                {
                    new() { Name = "C#", Slug = "c", Pages = testPages, Permalink = "/tags/c/" }
                }
            }
        };

        // Act
        var pages = generator.GeneratePages(taxonomies);

        // Assert
        var termPage = pages.FirstOrDefault(p => p.PageType == TaxonomyPageType.TermPage);
        Assert.NotNull(termPage);
        Assert.Equal("C#", termPage.TermName);
        Assert.Equal("c", termPage.TermSlug);
    }

    private static PageContext CreateTestPage(string title)
    {
        return new PageContext
        {
            Title = title,
            Content = "内容",
            Permalink = $"/posts/{title.ToLowerInvariant().Replace(" ", "-")}/",
            RelPermalink = $"/posts/{title.ToLowerInvariant().Replace(" ", "-")}/",
            Date = DateTimeOffset.Now,
            Tags = [],
            Categories = [],
            WordCount = 100,
            ReadingTime = TimeSpan.FromMinutes(1),
            Params = new Dictionary<string, object>()
        };
    }
}

/// <summary>
/// TaxonomyPageInfo 单元测试
/// </summary>
public class TaxonomyPageInfoTests
{
    [Fact]
    public void TaxonomyPageInfo_应该正确设置属性()
    {
        // Arrange & Act
        var info = new TaxonomyPageInfo
        {
            TaxonomyName = "tags",
            TermName = "C#",
            TermSlug = "c",
            PageType = TaxonomyPageType.TermPage,
            Permalink = "/tags/c/",
            Terms = null,
            Pages = [],
            PageNumber = 1,
            TotalPages = 1,
            TotalItems = 5
        };

        // Assert
        Assert.Equal("tags", info.TaxonomyName);
        Assert.Equal("C#", info.TermName);
        Assert.Equal("c", info.TermSlug);
        Assert.Equal(TaxonomyPageType.TermPage, info.PageType);
        Assert.Equal("/tags/c/", info.Permalink);
        Assert.Equal(5, info.TotalItems);
    }
}

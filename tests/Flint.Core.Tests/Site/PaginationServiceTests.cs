// Flint 静态站点生成器
// 分页服务单元测试

using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// PaginationService 单元测试
/// </summary>
public class PaginationServiceTests
{
    private readonly PaginationService _service;

    public PaginationServiceTests()
    {
        _service = new PaginationService("page", 10);
    }

    #region Paginate 测试

    [Fact]
    public void Paginate_应该正确分页()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var paginator = _service.Paginate(items, 1);

        // Assert
        Assert.Equal(10, paginator.Items.Count);
        Assert.Equal(1, paginator.PageNumber);
        Assert.Equal(3, paginator.TotalPages);
        Assert.Equal(25, paginator.TotalItems);
    }

    [Fact]
    public void Paginate_第二页应该返回正确的项目()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var paginator = _service.Paginate(items, 2);

        // Assert
        Assert.Equal(10, paginator.Items.Count);
        Assert.Equal(11, paginator.Items[0]);
        Assert.Equal(20, paginator.Items[9]);
    }

    [Fact]
    public void Paginate_最后一页可能不满()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var paginator = _service.Paginate(items, 3);

        // Assert
        Assert.Equal(5, paginator.Items.Count);
        Assert.Equal(21, paginator.Items[0]);
        Assert.Equal(25, paginator.Items[4]);
    }

    [Fact]
    public void Paginate_自定义页面大小()
    {
        // Arrange
        var items = Enumerable.Range(1, 100).ToList();

        // Act
        var paginator = _service.Paginate(items, 1, 20);

        // Assert
        Assert.Equal(20, paginator.Items.Count);
        Assert.Equal(5, paginator.TotalPages);
    }

    [Fact]
    public void Paginate_空列表应该返回空分页器()
    {
        // Arrange
        var items = new List<int>();

        // Act
        var paginator = _service.Paginate(items, 1);

        // Assert
        Assert.Empty(paginator.Items);
        Assert.Equal(1, paginator.TotalPages);
    }

    #endregion

    #region PaginateWithUrls 测试

    [Fact]
    public void PaginateWithUrls_应该生成正确的URL()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var result = _service.PaginateWithUrls(items, 1, "/blog");

        // Assert
        Assert.Equal("/blog/", result.BasePath);
        Assert.Equal("page", result.PaginatePath);
    }

    [Fact]
    public void PaginateWithUrls_CurrentUrl第一页()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var result = _service.PaginateWithUrls(items, 1, "/blog");

        // Assert
        Assert.Equal("/blog/", result.CurrentUrl);
    }

    [Fact]
    public void PaginateWithUrls_CurrentUrl非第一页()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var result = _service.PaginateWithUrls(items, 2, "/blog");

        // Assert
        Assert.Equal("/blog/page/2/", result.CurrentUrl);
    }

    [Fact]
    public void PaginateWithUrls_PrevUrl第一页为null()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var result = _service.PaginateWithUrls(items, 1, "/blog");

        // Assert
        Assert.Null(result.PrevUrl);
    }

    [Fact]
    public void PaginateWithUrls_PrevUrl非第一页()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var result = _service.PaginateWithUrls(items, 2, "/blog");

        // Assert
        Assert.Equal("/blog/", result.PrevUrl);
    }

    [Fact]
    public void PaginateWithUrls_NextUrl最后一页为null()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var result = _service.PaginateWithUrls(items, 3, "/blog");

        // Assert
        Assert.Null(result.NextUrl);
    }

    [Fact]
    public void PaginateWithUrls_NextUrl非最后一页()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();

        // Act
        var result = _service.PaginateWithUrls(items, 1, "/blog");

        // Assert
        Assert.Equal("/blog/page/2/", result.NextUrl);
    }

    #endregion

    #region GeneratePageUrl 测试

    [Theory]
    [InlineData("/blog", 1, "/blog/")]
    [InlineData("/blog", 2, "/blog/page/2/")]
    [InlineData("/blog/", 3, "/blog/page/3/")]
    [InlineData("blog", 1, "/blog/")]
    public void GeneratePageUrl_应该生成正确的URL(string basePath, int pageNumber, string expected)
    {
        // Act
        var url = _service.GeneratePageUrl(basePath, pageNumber);

        // Assert
        Assert.Equal(expected, url);
    }

    #endregion

    #region GenerateAllPageUrls 测试

    [Fact]
    public void GenerateAllPageUrls_应该生成所有页面URL()
    {
        // Act
        var urls = _service.GenerateAllPageUrls("/blog", 3);

        // Assert
        Assert.Equal(3, urls.Count);
        Assert.Equal("/blog/", urls[0].Url);
        Assert.Equal("/blog/page/2/", urls[1].Url);
        Assert.Equal("/blog/page/3/", urls[2].Url);
    }

    [Fact]
    public void GenerateAllPageUrls_应该标记首尾页()
    {
        // Act
        var urls = _service.GenerateAllPageUrls("/blog", 3);

        // Assert
        Assert.True(urls[0].IsFirst);
        Assert.False(urls[0].IsLast);
        Assert.False(urls[1].IsFirst);
        Assert.False(urls[1].IsLast);
        Assert.False(urls[2].IsFirst);
        Assert.True(urls[2].IsLast);
    }

    [Fact]
    public void GenerateAllPageUrls_单页时首尾相同()
    {
        // Act
        var urls = _service.GenerateAllPageUrls("/blog", 1);

        // Assert
        Assert.Single(urls);
        Assert.True(urls[0].IsFirst);
        Assert.True(urls[0].IsLast);
    }

    #endregion

    #region CalculateTotalPages 测试

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    [InlineData(100, 20, 5)]
    public void CalculateTotalPages_应该正确计算(int totalItems, int pageSize, int expected)
    {
        // Act
        var totalPages = _service.CalculateTotalPages(totalItems, pageSize);

        // Assert
        Assert.Equal(expected, totalPages);
    }

    [Fact]
    public void CalculateTotalPages_使用默认页面大小()
    {
        // Act
        var totalPages = _service.CalculateTotalPages(25);

        // Assert
        Assert.Equal(3, totalPages);
    }

    #endregion

    #region GetPageRange 测试

    [Fact]
    public void GetPageRange_应该返回正确的范围()
    {
        // Act
        var range = _service.GetPageRange(5, 10, 5);

        // Assert
        Assert.Equal(3, range.Start);
        Assert.Equal(7, range.End);
        Assert.Equal(5, range.CurrentPage);
        Assert.Equal(10, range.TotalPages);
    }

    [Fact]
    public void GetPageRange_第一页附近()
    {
        // Act
        var range = _service.GetPageRange(1, 10, 5);

        // Assert
        Assert.Equal(1, range.Start);
        Assert.Equal(5, range.End);
        Assert.False(range.ShowFirst);
        Assert.True(range.ShowLast);
    }

    [Fact]
    public void GetPageRange_最后一页附近()
    {
        // Act
        var range = _service.GetPageRange(10, 10, 5);

        // Assert
        Assert.Equal(6, range.Start);
        Assert.Equal(10, range.End);
        Assert.True(range.ShowFirst);
        Assert.False(range.ShowLast);
    }

    [Fact]
    public void GetPageRange_中间位置()
    {
        // Act
        var range = _service.GetPageRange(5, 10, 5);

        // Assert
        Assert.True(range.ShowFirst);
        Assert.True(range.ShowLast);
    }

    [Fact]
    public void GetPageRange_Pages属性应该返回范围内的页码()
    {
        // Act
        var range = _service.GetPageRange(5, 10, 5);
        var pages = range.Pages.ToList();

        // Assert
        Assert.Equal(5, pages.Count);
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, pages);
    }

    [Fact]
    public void GetPageRange_ShowPrevEllipsis()
    {
        // Act
        var range = _service.GetPageRange(5, 10, 5);

        // Assert
        Assert.True(range.ShowPrevEllipsis); // Start > 2
    }

    [Fact]
    public void GetPageRange_ShowNextEllipsis()
    {
        // Act
        var range = _service.GetPageRange(5, 10, 5);

        // Assert
        Assert.True(range.ShowNextEllipsis); // End < TotalPages - 1
    }

    #endregion
}

/// <summary>
/// PaginatedResult 单元测试
/// </summary>
public class PaginatedResultTests
{
    [Fact]
    public void GetPageUrl_第一页返回基础路径()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();
        var service = new PaginationService();
        var result = service.PaginateWithUrls(items, 1, "/blog");

        // Act
        var url = result.GetPageUrl(1);

        // Assert
        Assert.Equal("/blog/", url);
    }

    [Fact]
    public void GetPageUrl_非第一页返回分页路径()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();
        var service = new PaginationService();
        var result = service.PaginateWithUrls(items, 1, "/blog");

        // Act
        var url = result.GetPageUrl(2);

        // Assert
        Assert.Equal("/blog/page/2/", url);
    }

    [Fact]
    public void FirstUrl_应该返回第一页URL()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();
        var service = new PaginationService();
        var result = service.PaginateWithUrls(items, 2, "/blog");

        // Assert
        Assert.Equal("/blog/", result.FirstUrl);
    }

    [Fact]
    public void LastUrl_应该返回最后一页URL()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).ToList();
        var service = new PaginationService();
        var result = service.PaginateWithUrls(items, 1, "/blog");

        // Assert
        Assert.Equal("/blog/page/3/", result.LastUrl);
    }
}

/// <summary>
/// PageUrlInfo 单元测试
/// </summary>
public class PageUrlInfoTests
{
    [Fact]
    public void PageUrlInfo_应该正确设置属性()
    {
        // Arrange & Act
        var info = new PageUrlInfo
        {
            PageNumber = 2,
            Url = "/blog/page/2/",
            IsFirst = false,
            IsLast = false
        };

        // Assert
        Assert.Equal(2, info.PageNumber);
        Assert.Equal("/blog/page/2/", info.Url);
        Assert.False(info.IsFirst);
        Assert.False(info.IsLast);
    }
}

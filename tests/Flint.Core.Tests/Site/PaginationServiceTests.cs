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
}

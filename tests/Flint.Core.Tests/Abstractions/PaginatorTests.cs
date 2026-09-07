// Flint 核心库测试
// Paginator 分页器单元测试

using Flint.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace Flint.Core.Tests.Abstractions;

/// <summary>
/// Paginator 分页器的单元测试
/// </summary>
public class PaginatorTests
{
    [Fact]
    public void Paginator_Create_应该正确分页()
    {
        // Arrange
        var items = Enumerable.Range(1, 100).ToList();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 1, pageSize: 10);

        // Assert
        paginator.Items.Should().HaveCount(10);
        paginator.Items.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        paginator.PageNumber.Should().Be(1);
        paginator.PageSize.Should().Be(10);
        paginator.TotalPages.Should().Be(10);
        paginator.TotalItems.Should().Be(100);
    }

    [Fact]
    public void Paginator_应该正确处理中间页()
    {
        // Arrange
        var items = Enumerable.Range(1, 100).ToList();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 5, pageSize: 10);

        // Assert
        paginator.Items.Should().BeEquivalentTo(new[] { 41, 42, 43, 44, 45, 46, 47, 48, 49, 50 });
        paginator.PageNumber.Should().Be(5);
        paginator.HasPrev.Should().BeTrue();
        paginator.HasNext.Should().BeTrue();
    }

    [Fact]
    public void Paginator_应该正确处理最后一页()
    {
        // Arrange
        var items = Enumerable.Range(1, 95).ToList();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 10, pageSize: 10);

        // Assert
        paginator.Items.Should().HaveCount(5);
        paginator.Items.Should().BeEquivalentTo(new[] { 91, 92, 93, 94, 95 });
        paginator.PageNumber.Should().Be(10);
        paginator.HasNext.Should().BeFalse();
        paginator.IsLast.Should().BeTrue();
    }

    [Fact]
    public void Paginator_第一页应该没有上一页()
    {
        // Arrange
        var items = Enumerable.Range(1, 50).ToList();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 1, pageSize: 10);

        // Assert
        paginator.HasPrev.Should().BeFalse();
        paginator.PrevPageNumber.Should().BeNull();
        paginator.IsFirst.Should().BeTrue();
    }

    [Fact]
    public void Paginator_最后一页应该没有下一页()
    {
        // Arrange
        var items = Enumerable.Range(1, 50).ToList();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 5, pageSize: 10);

        // Assert
        paginator.HasNext.Should().BeFalse();
        paginator.NextPageNumber.Should().BeNull();
        paginator.IsLast.Should().BeTrue();
    }

    [Fact]
    public void Paginator_应该正确返回上一页和下一页页码()
    {
        // Arrange
        var items = Enumerable.Range(1, 100).ToList();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 5, pageSize: 10);

        // Assert
        paginator.PrevPageNumber.Should().Be(4);
        paginator.NextPageNumber.Should().Be(6);
    }

    [Fact]
    public void Paginator_页码超出范围应该修正到最后一页()
    {
        // Arrange
        var items = Enumerable.Range(1, 50).ToList();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 100, pageSize: 10);

        // Assert
        paginator.PageNumber.Should().Be(5);
        paginator.IsLast.Should().BeTrue();
    }

    [Fact]
    public void Paginator_页码小于1应该修正到第一页()
    {
        // Arrange
        var items = Enumerable.Range(1, 50).ToList();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 0, pageSize: 10);

        // Assert
        paginator.PageNumber.Should().Be(1);
        paginator.IsFirst.Should().BeTrue();
    }

    [Fact]
    public void Paginator_空列表应该返回空分页()
    {
        // Arrange
        var items = new List<int>();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 1, pageSize: 10);

        // Assert
        paginator.Items.Should().BeEmpty();
        paginator.TotalItems.Should().Be(0);
        paginator.TotalPages.Should().Be(1);
        paginator.PageNumber.Should().Be(1);
    }

    [Fact]
    public void Paginator_单页数据应该正确处理()
    {
        // Arrange
        var items = Enumerable.Range(1, 5).ToList();

        // Act
        var paginator = Paginator<int>.Create(items, pageNumber: 1, pageSize: 10);

        // Assert
        paginator.Items.Should().HaveCount(5);
        paginator.TotalPages.Should().Be(1);
        paginator.HasPrev.Should().BeFalse();
        paginator.HasNext.Should().BeFalse();
        paginator.IsFirst.Should().BeTrue();
        paginator.IsLast.Should().BeTrue();
    }

    [Fact]
    public void Paginator_应该抛出异常当列表为null()
    {
        // Arrange
        IReadOnlyList<int>? items = null;

        // Act
        var act = () => Paginator<int>.Create(items!, pageNumber: 1, pageSize: 10);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Paginator_应该支持复杂类型()
    {
        // Arrange
        var items = new List<TestItem>
        {
            new() { Id = 1, Name = "Item 1" },
            new() { Id = 2, Name = "Item 2" },
            new() { Id = 3, Name = "Item 3" }
        };

        // Act
        var paginator = Paginator<TestItem>.Create(items, pageNumber: 1, pageSize: 2);

        // Assert
        paginator.Items.Should().HaveCount(2);
        paginator.Items[0].Name.Should().Be("Item 1");
        paginator.Items[1].Name.Should().Be("Item 2");
        paginator.TotalPages.Should().Be(2);
    }

    private sealed class TestItem
    {
        public int Id { get; init; }
        public required string Name { get; init; }
    }
}

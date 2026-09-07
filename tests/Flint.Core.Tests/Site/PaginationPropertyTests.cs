// Flint 静态站点生成器
// 分页属性测试

using Flint.Core.Abstractions;
using Flint.Core.Site;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// 分页属性测试
/// **Property 8: 分页逻辑正确性**
/// **验证: 需求 5.3**
/// </summary>
public class PaginationPropertyTests
{
    /// <summary>
    /// **Property 8: 分页后的所有页面应该包含所有原始内容**
    /// 对于任意内容列表和分页大小，分页后的所有页面应该包含所有原始内容
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(PaginationArbitrary)])]
    public Property AllItems_ShouldBePreserved_AfterPagination(
        ValidItemList items,
        ValidPageSize pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(pageSize);

        // Arrange
        var service = new PaginationService(defaultPageSize: pageSize.Value);
        var allItems = items.Value;
        var totalPages = service.CalculateTotalPages(allItems.Count, pageSize.Value);

        // Act - 收集所有分页后的项目
        var collectedItems = new List<int>();
        for (var page = 1; page <= totalPages; page++)
        {
            var paginator = service.Paginate(allItems, page, pageSize.Value);
            collectedItems.AddRange(paginator.Items);
        }

        // Assert - 所有原始项目都应该被包含
        return (collectedItems.Count == allItems.Count &&
                collectedItems.OrderBy(x => x).SequenceEqual(allItems.OrderBy(x => x)))
            .ToProperty()
            .Label($"项目数: {allItems.Count}, 页大小: {pageSize.Value}, 总页数: {totalPages}");
    }

    /// <summary>
    /// **Property 8: 每页内容数量不超过分页大小（最后一页除外）**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(PaginationArbitrary)])]
    public Property PageSize_ShouldNotExceedLimit_ExceptLastPage(
        ValidItemList items,
        ValidPageSize pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(pageSize);

        // Arrange
        var service = new PaginationService(defaultPageSize: pageSize.Value);
        var allItems = items.Value;
        var totalPages = service.CalculateTotalPages(allItems.Count, pageSize.Value);

        // Act & Assert - 检查每页的大小
        for (var page = 1; page <= totalPages; page++)
        {
            var paginator = service.Paginate(allItems, page, pageSize.Value);

            if (page < totalPages)
            {
                // 非最后一页应该正好是 pageSize
                if (paginator.Items.Count != pageSize.Value)
                {
                    return false.ToProperty()
                        .Label($"第 {page} 页应该有 {pageSize.Value} 项，实际有 {paginator.Items.Count} 项");
                }
            }
            else
            {
                // 最后一页应该 <= pageSize
                if (paginator.Items.Count > pageSize.Value)
                {
                    return false.ToProperty()
                        .Label($"最后一页不应超过 {pageSize.Value} 项，实际有 {paginator.Items.Count} 项");
                }
            }
        }

        return true.ToProperty()
            .Label($"所有页面大小正确: 项目数={allItems.Count}, 页大小={pageSize.Value}");
    }

    /// <summary>
    /// **Property 8: 分页器的 HasPrev/HasNext 属性正确性**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(PaginationArbitrary)])]
    public Property Navigation_Properties_ShouldBeCorrect(
        ValidItemList items,
        ValidPageSize pageSize,
        ValidPageNumber pageNumber)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(pageSize);
        ArgumentNullException.ThrowIfNull(pageNumber);

        // Arrange
        var allItems = items.Value;
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)allItems.Count / pageSize.Value));
        var actualPage = Math.Max(1, Math.Min(pageNumber.Value, totalPages));

        // Act
        var paginator = Paginator<int>.Create(allItems, actualPage, pageSize.Value);

        // Assert
        var hasPrevCorrect = paginator.HasPrev == (paginator.PageNumber > 1);
        var hasNextCorrect = paginator.HasNext == (paginator.PageNumber < paginator.TotalPages);
        var isFirstCorrect = paginator.IsFirst == (paginator.PageNumber == 1);
        var isLastCorrect = paginator.IsLast == (paginator.PageNumber == paginator.TotalPages);

        return (hasPrevCorrect && hasNextCorrect && isFirstCorrect && isLastCorrect)
            .ToProperty()
            .Label($"页码={actualPage}, 总页数={totalPages}, HasPrev={paginator.HasPrev}, HasNext={paginator.HasNext}");
    }

    /// <summary>
    /// **Property 8: 总页数计算正确性**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(PaginationArbitrary)])]
    public Property TotalPages_ShouldBeCalculatedCorrectly(
        ValidItemList items,
        ValidPageSize pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(pageSize);

        // Arrange
        var service = new PaginationService();
        var itemCount = items.Value.Count;

        // Act
        var totalPages = service.CalculateTotalPages(itemCount, pageSize.Value);

        // Assert
        var expectedPages = itemCount > 0
            ? (int)Math.Ceiling((double)itemCount / pageSize.Value)
            : 1;

        return (totalPages == expectedPages)
            .ToProperty()
            .Label($"项目数={itemCount}, 页大小={pageSize.Value}, 期望页数={expectedPages}, 实际页数={totalPages}");
    }

    /// <summary>
    /// **Property 8: 空列表分页应该返回单页空结果**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = [typeof(PaginationArbitrary)])]
    public Property EmptyList_ShouldReturnSingleEmptyPage(ValidPageSize pageSize)
    {
        ArgumentNullException.ThrowIfNull(pageSize);

        // Arrange
        var emptyList = new List<int>();

        // Act
        var paginator = Paginator<int>.Create(emptyList, 1, pageSize.Value);

        // Assert
        return (paginator.Items.Count == 0 &&
                paginator.TotalPages == 1 &&
                paginator.TotalItems == 0 &&
                paginator.PageNumber == 1 &&
                !paginator.HasPrev &&
                !paginator.HasNext)
            .ToProperty()
            .Label("空列表应该返回单页空结果");
    }
}

#region 测试数据类型

/// <summary>
/// 有效项目列表
/// </summary>
public sealed class ValidItemList
{
    public IReadOnlyList<int> Value { get; }

    public ValidItemList(IReadOnlyList<int> value)
    {
        Value = value;
    }

    public override string ToString() => $"ItemList(Count={Value.Count})";
}

/// <summary>
/// 有效页大小
/// </summary>
public sealed class ValidPageSize
{
    public int Value { get; }

    public ValidPageSize(int value)
    {
        Value = value;
    }

    public override string ToString() => $"PageSize({Value})";
}

/// <summary>
/// 有效页码
/// </summary>
public sealed class ValidPageNumber
{
    public int Value { get; }

    public ValidPageNumber(int value)
    {
        Value = value;
    }

    public override string ToString() => $"PageNumber({Value})";
}

/// <summary>
/// 有效基础路径
/// </summary>
public sealed class ValidBasePath
{
    public string Value { get; }

    public ValidBasePath(string value)
    {
        Value = value;
    }

    public override string ToString() => $"BasePath({Value})";
}

/// <summary>
/// 有效总页数
/// </summary>
public sealed class ValidTotalPages
{
    public int Value { get; }

    public ValidTotalPages(int value)
    {
        Value = value;
    }

    public override string ToString() => $"TotalPages({Value})";
}

/// <summary>
/// 有效窗口大小
/// </summary>
public sealed class ValidWindowSize
{
    public int Value { get; }

    public ValidWindowSize(int value)
    {
        Value = value;
    }

    public override string ToString() => $"WindowSize({Value})";
}

#endregion

#region 生成器

/// <summary>
/// 分页测试数据生成器
/// </summary>
public static class PaginationArbitrary
{
    /// <summary>
    /// 生成有效的项目列表
    /// </summary>
    public static Arbitrary<ValidItemList> ValidItemList()
    {
        var gen = Gen.Choose(0, 200)
            .SelectMany(count => Gen.ListOf<int>(Gen.Choose(1, 10000), count))
            .Select(list => new ValidItemList(list.ToList()));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的页大小
    /// </summary>
    public static Arbitrary<ValidPageSize> ValidPageSize()
    {
        var gen = Gen.Choose(1, 50)
            .Select(size => new ValidPageSize(size));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的页码
    /// </summary>
    public static Arbitrary<ValidPageNumber> ValidPageNumber()
    {
        var gen = Gen.Choose(1, 100)
            .Select(page => new ValidPageNumber(page));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的基础路径
    /// </summary>
    public static Arbitrary<ValidBasePath> ValidBasePath()
    {
        var gen = Gen.Elements(
            "/",
            "/blog/",
            "/posts/",
            "/articles/",
            "blog",
            "posts",
            "/category/tech/",
            "/tags/csharp/"
        ).Select(path => new ValidBasePath(path));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的总页数
    /// </summary>
    public static Arbitrary<ValidTotalPages> ValidTotalPages()
    {
        var gen = Gen.Choose(1, 100)
            .Select(pages => new ValidTotalPages(pages));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的窗口大小
    /// </summary>
    public static Arbitrary<ValidWindowSize> ValidWindowSize()
    {
        var gen = Gen.Choose(3, 11)
            .Select(size => new ValidWindowSize(size));

        return gen.ToArbitrary();
    }
}

#endregion

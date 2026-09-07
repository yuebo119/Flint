// Flint 静态站点生成器
// 分页服务实现

using Flint.Core.Abstractions;

namespace Flint.Core.Site;

/// <summary>
/// 分页服务
/// 提供分页逻辑和 URL 生成功能
/// </summary>
public sealed class PaginationService
{
    private readonly int _defaultPageSize;

    /// <summary>
    /// 创建分页服务
    /// </summary>
    /// <param name="paginatePath">分页路径模式（保留参数兼容历史调用；URL 生成能力移除后暂无消费方）</param>
    /// <param name="defaultPageSize">默认每页大小，默认为 10</param>
    public PaginationService(string paginatePath = "page", int defaultPageSize = 10)
    {
        _ = paginatePath;
        _defaultPageSize = defaultPageSize;
    }

    /// <summary>
    /// 创建分页结果
    /// </summary>
    /// <typeparam name="T">项目类型</typeparam>
    /// <param name="items">所有项目</param>
    /// <param name="pageNumber">页码（从 1 开始）</param>
    /// <param name="pageSize">每页大小（可选，使用默认值）</param>
    /// <returns>分页器实例</returns>
    public Paginator<T> Paginate<T>(
        IReadOnlyList<T> items,
        int pageNumber,
        int? pageSize = null)
    {
        return Paginator<T>.Create(items, pageNumber, pageSize ?? _defaultPageSize);
    }

    /// <summary>
    /// 计算总页数
    /// </summary>
    /// <param name="totalItems">总项目数</param>
    /// <param name="pageSize">每页大小</param>
    /// <returns>总页数</returns>
    public int CalculateTotalPages(int totalItems, int? pageSize = null)
    {
        var size = pageSize ?? _defaultPageSize;
        return totalItems > 0 ? (int)Math.Ceiling((double)totalItems / size) : 1;
    }
}

/// <summary>
/// 分页器
/// </summary>
/// <typeparam name="T">分页项类型</typeparam>
public sealed class Paginator<T>
{
    /// <summary>
    /// 当前页的项目
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// 当前页码（从 1 开始）
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// 每页大小
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// 总页数
    /// </summary>
    public required int TotalPages { get; init; }

    /// <summary>
    /// 总项目数
    /// </summary>
    public required int TotalItems { get; init; }

    /// <summary>
    /// 是否有上一页
    /// </summary>
    public bool HasPrev => PageNumber > 1;

    /// <summary>
    /// 是否有下一页
    /// </summary>
    public bool HasNext => PageNumber < TotalPages;

    /// <summary>
    /// 上一页页码
    /// </summary>
    public int? PrevPageNumber => HasPrev ? PageNumber - 1 : null;

    /// <summary>
    /// 下一页页码
    /// </summary>
    public int? NextPageNumber => HasNext ? PageNumber + 1 : null;

    /// <summary>
    /// 是否为第一页
    /// </summary>
    public bool IsFirst => PageNumber == 1;

    /// <summary>
    /// 是否为最后一页
    /// </summary>
    public bool IsLast => PageNumber == TotalPages;

    /// <summary>
    /// 创建分页器
    /// </summary>
    /// <param name="allItems">所有项目</param>
    /// <param name="pageNumber">页码</param>
    /// <param name="pageSize">每页大小</param>
    /// <returns>分页器实例</returns>
    public static Paginator<T> Create(
        IReadOnlyList<T> allItems,
        int pageNumber,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(allItems);

        var totalItems = allItems.Count;
        var totalPages = totalItems > 0 ? (int)Math.Ceiling((double)totalItems / pageSize) : 1;
        pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

        var skip = (pageNumber - 1) * pageSize;
        var items = allItems.Skip(skip).Take(pageSize).ToList();

        return new Paginator<T>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = totalPages,
            TotalItems = totalItems
        };
    }
}

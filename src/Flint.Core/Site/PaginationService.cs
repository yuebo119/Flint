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
    private readonly string _paginatePath;
    private readonly int _defaultPageSize;

    /// <summary>
    /// 创建分页服务
    /// </summary>
    /// <param name="paginatePath">分页路径模式，默认为 "page"</param>
    /// <param name="defaultPageSize">默认每页大小，默认为 10</param>
    public PaginationService(string paginatePath = "page", int defaultPageSize = 10)
    {
        _paginatePath = paginatePath.Trim('/');
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
    /// 创建分页结果（带 URL 生成）
    /// </summary>
    /// <typeparam name="T">项目类型</typeparam>
    /// <param name="items">所有项目</param>
    /// <param name="pageNumber">页码</param>
    /// <param name="basePath">基础路径</param>
    /// <param name="pageSize">每页大小</param>
    /// <returns>带 URL 的分页结果</returns>
    public PaginatedResult<T> PaginateWithUrls<T>(
        IReadOnlyList<T> items,
        int pageNumber,
        string basePath,
        int? pageSize = null)
    {
        var paginator = Paginate(items, pageNumber, pageSize);
        return new PaginatedResult<T>
        {
            Paginator = paginator,
            BasePath = NormalizePath(basePath),
            PaginatePath = _paginatePath
        };
    }

    /// <summary>
    /// 生成分页 URL
    /// </summary>
    /// <param name="basePath">基础路径</param>
    /// <param name="pageNumber">页码</param>
    /// <returns>分页 URL</returns>
    public string GeneratePageUrl(string basePath, int pageNumber)
    {
        basePath = NormalizePath(basePath);

        if (pageNumber <= 1)
        {
            return basePath;
        }

        return $"{basePath}{_paginatePath}/{pageNumber}/";
    }

    /// <summary>
    /// 生成所有分页的 URL 列表
    /// </summary>
    /// <param name="basePath">基础路径</param>
    /// <param name="totalPages">总页数</param>
    /// <returns>URL 列表</returns>
    public IReadOnlyList<PageUrlInfo> GenerateAllPageUrls(string basePath, int totalPages)
    {
        var urls = new List<PageUrlInfo>(totalPages);

        for (var i = 1; i <= totalPages; i++)
        {
            urls.Add(new PageUrlInfo
            {
                PageNumber = i,
                Url = GeneratePageUrl(basePath, i),
                IsFirst = i == 1,
                IsLast = i == totalPages
            });
        }

        return urls;
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

    /// <summary>
    /// 获取页面范围（用于分页导航）
    /// </summary>
    /// <param name="currentPage">当前页码</param>
    /// <param name="totalPages">总页数</param>
    /// <param name="windowSize">窗口大小（显示多少个页码）</param>
    /// <returns>页码范围</returns>
    public PageRange GetPageRange(int currentPage, int totalPages, int windowSize = 5)
    {
        var halfWindow = windowSize / 2;
        var start = Math.Max(1, currentPage - halfWindow);
        var end = Math.Min(totalPages, start + windowSize - 1);

        // 调整起始位置以确保窗口大小
        if (end - start + 1 < windowSize)
        {
            start = Math.Max(1, end - windowSize + 1);
        }

        return new PageRange
        {
            Start = start,
            End = end,
            CurrentPage = currentPage,
            TotalPages = totalPages,
            ShowFirst = start > 1,
            ShowLast = end < totalPages,
            ShowPrevEllipsis = start > 2,
            ShowNextEllipsis = end < totalPages - 1
        };
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        path = path.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }
        if (!path.EndsWith('/'))
        {
            path += "/";
        }

        return path;
    }
}

/// <summary>
/// 带 URL 的分页结果
/// </summary>
/// <typeparam name="T">项目类型</typeparam>
public sealed class PaginatedResult<T>
{
    /// <summary>
    /// 分页器
    /// </summary>
    public required Paginator<T> Paginator { get; init; }

    /// <summary>
    /// 基础路径
    /// </summary>
    public required string BasePath { get; init; }

    /// <summary>
    /// 分页路径
    /// </summary>
    public required string PaginatePath { get; init; }

    /// <summary>
    /// 当前页 URL
    /// </summary>
    public string CurrentUrl => GetPageUrl(Paginator.PageNumber);

    /// <summary>
    /// 上一页 URL
    /// </summary>
    public string? PrevUrl => Paginator.HasPrev ? GetPageUrl(Paginator.PageNumber - 1) : null;

    /// <summary>
    /// 下一页 URL
    /// </summary>
    public string? NextUrl => Paginator.HasNext ? GetPageUrl(Paginator.PageNumber + 1) : null;

    /// <summary>
    /// 第一页 URL
    /// </summary>
    public string FirstUrl => GetPageUrl(1);

    /// <summary>
    /// 最后一页 URL
    /// </summary>
    public string LastUrl => GetPageUrl(Paginator.TotalPages);

    /// <summary>
    /// 获取指定页码的 URL
    /// </summary>
    public string GetPageUrl(int pageNumber)
    {
        if (pageNumber <= 1)
        {
            return BasePath;
        }

        return $"{BasePath}{PaginatePath}/{pageNumber}/";
    }
}

/// <summary>
/// 页面 URL 信息
/// </summary>
public sealed class PageUrlInfo
{
    /// <summary>
    /// 页码
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// URL
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// 是否为第一页
    /// </summary>
    public required bool IsFirst { get; init; }

    /// <summary>
    /// 是否为最后一页
    /// </summary>
    public required bool IsLast { get; init; }
}

/// <summary>
/// 页码范围（用于分页导航）
/// </summary>
public sealed class PageRange
{
    /// <summary>
    /// 起始页码
    /// </summary>
    public required int Start { get; init; }

    /// <summary>
    /// 结束页码
    /// </summary>
    public required int End { get; init; }

    /// <summary>
    /// 当前页码
    /// </summary>
    public required int CurrentPage { get; init; }

    /// <summary>
    /// 总页数
    /// </summary>
    public required int TotalPages { get; init; }

    /// <summary>
    /// 是否显示第一页链接
    /// </summary>
    public required bool ShowFirst { get; init; }

    /// <summary>
    /// 是否显示最后一页链接
    /// </summary>
    public required bool ShowLast { get; init; }

    /// <summary>
    /// 是否显示前省略号
    /// </summary>
    public required bool ShowPrevEllipsis { get; init; }

    /// <summary>
    /// 是否显示后省略号
    /// </summary>
    public required bool ShowNextEllipsis { get; init; }

    /// <summary>
    /// 获取范围内的所有页码
    /// </summary>
    public IEnumerable<int> Pages
    {
        get
        {
            for (var i = Start; i <= End; i++)
            {
                yield return i;
            }
        }
    }
}

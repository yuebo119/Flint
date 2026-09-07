// Flint 静态站点生成器
// 分类系统服务实现

using System.Text.RegularExpressions;
using Flint.Core.Abstractions;

namespace Flint.Core.Site;

/// <summary>
/// 分类系统服务
/// 管理标签、分类等分类法
/// </summary>
public sealed partial class TaxonomyService
{
    private readonly Dictionary<string, TaxonomyConfig> _taxonomyConfigs;
    private readonly string _baseUrl;

    /// <summary>
    /// 创建分类系统服务
    /// </summary>
    /// <param name="baseUrl">站点基础 URL</param>
    /// <param name="taxonomyConfigs">分类配置（可选）</param>
    public TaxonomyService(
        string baseUrl = "",
        Dictionary<string, TaxonomyConfig>? taxonomyConfigs = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _taxonomyConfigs = taxonomyConfigs ?? GetDefaultTaxonomyConfigs();
    }

    /// <summary>
    /// 从页面列表构建分类集合
    /// </summary>
    /// <param name="pages">页面上下文列表</param>
    /// <returns>分类集合</returns>
    public TaxonomyCollection BuildTaxonomies(IReadOnlyList<PageContext> pages)
    {
        var taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>();

        foreach (var (taxonomyName, config) in _taxonomyConfigs)
        {
            var terms = BuildTaxonomyTerms(pages, taxonomyName, config);
            taxonomies[taxonomyName] = terms;
        }

        return new TaxonomyCollection { Taxonomies = taxonomies };
    }

    /// <summary>
    /// 为指定分类构建术语列表
    /// </summary>
    private List<TaxonomyTerm> BuildTaxonomyTerms(
        IReadOnlyList<PageContext> pages,
        string taxonomyName,
        TaxonomyConfig config)
    {
        // 收集所有术语及其关联的页面
        var termPages = new Dictionary<string, List<PageContext>>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in pages)
        {
            var termValues = GetTermValues(page, taxonomyName);
            foreach (var termValue in termValues)
            {
                var normalizedTerm = NormalizeTerm(termValue);
                // 统一过滤点（内置 tags/categories 与自定义分类一并覆盖）：
                // 空白值、或 slug 化后为空（如纯符号 "---"）的 term 跳过，
                // 否则产出 /tags// 畸形链接
                if (string.IsNullOrWhiteSpace(normalizedTerm) ||
                    string.IsNullOrWhiteSpace(GenerateSlug(normalizedTerm)))
                {
                    continue;
                }
                if (!termPages.TryGetValue(normalizedTerm, out var pageList))
                {
                    pageList = [];
                    termPages[normalizedTerm] = pageList;
                }
                pageList.Add(page);
            }
        }

        // 构建术语对象
        var terms = new List<TaxonomyTerm>();
        foreach (var (termName, associatedPages) in termPages)
        {
            var slug = GenerateSlug(termName);
            var permalink = GenerateTermPermalink(taxonomyName, slug);

            // 按日期排序页面
            var sortedPages = associatedPages
                .OrderByDescending(p => p.Date)
                .ToList();

            terms.Add(new TaxonomyTerm
            {
                Name = termName,
                Slug = slug,
                Pages = sortedPages,
                Permalink = permalink
            });
        }

        // 按配置排序术语
        return config.SortBy switch
        {
            TaxonomySortBy.Name => [.. terms.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)],
            TaxonomySortBy.Count => [.. terms.OrderByDescending(t => t.Count)],
            TaxonomySortBy.Weight => terms, // 权重排序需要额外的权重信息
            _ => terms
        };
    }

    /// <summary>
    /// 从页面获取指定分类的术语值
    /// </summary>
    private static IEnumerable<string> GetTermValues(PageContext page, string taxonomyName)
    {
        return taxonomyName.ToLowerInvariant() switch
        {
            "tags" => page.Tags,
            "categories" => page.Categories,
            _ => GetCustomTaxonomyValues(page, taxonomyName)
        };
    }

    /// <summary>
    /// 获取自定义分类的值
    /// </summary>
    private static IEnumerable<string> GetCustomTaxonomyValues(PageContext page, string taxonomyName)
    {
        if (page.Params.TryGetValue(taxonomyName, out var value))
        {
            return value switch
            {
                // 各分支统一过滤空白值：空 tag 经 slug 规范化得空串，产出 /tags// 畸形链接
                IEnumerable<string> strings => strings.Where(s => !string.IsNullOrWhiteSpace(s)),
                IEnumerable<object> objects => objects.Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)),
                string str => string.IsNullOrWhiteSpace(str) ? [] : [str],
                _ => []
            };
        }
        return [];
    }

    /// <summary>
    /// 规范化术语名称
    /// </summary>
    private static string NormalizeTerm(string term)
    {
        return term.Trim();
    }

    /// <summary>
    /// 生成 URL 友好的 slug
    /// </summary>
    public static string GenerateSlug(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return string.Empty;
        }

        // 转换为小写
        var slug = term.ToLowerInvariant();

        // 替换空格和特殊字符为连字符
        slug = SlugInvalidCharsRegex().Replace(slug, "-");

        // 移除连续的连字符
        slug = MultipleHyphensRegex().Replace(slug, "-");

        // 移除首尾的连字符
        slug = slug.Trim('-');

        return slug;
    }

    /// <summary>
    /// 生成术语的永久链接
    /// </summary>
    private string GenerateTermPermalink(string taxonomyName, string slug)
    {
        var taxonomySlug = GenerateSlug(taxonomyName);
        return $"{_baseUrl}/{taxonomySlug}/{slug}/";
    }

    /// <summary>
    /// 生成分类列表页的永久链接
    /// </summary>
    public string GenerateTaxonomyListPermalink(string taxonomyName)
    {
        var taxonomySlug = GenerateSlug(taxonomyName);
        return $"{_baseUrl}/{taxonomySlug}/";
    }

    /// <summary>
    /// 获取默认分类配置
    /// </summary>
    private static Dictionary<string, TaxonomyConfig> GetDefaultTaxonomyConfigs()
    {
        return new Dictionary<string, TaxonomyConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["tags"] = new TaxonomyConfig
            {
                Singular = "tag",
                Plural = "tags",
                SortBy = TaxonomySortBy.Name
            },
            ["categories"] = new TaxonomyConfig
            {
                Singular = "category",
                Plural = "categories",
                SortBy = TaxonomySortBy.Name
            }
        };
    }

    [GeneratedRegex(@"[^a-z0-9\u4e00-\u9fff\-]")]
    private static partial Regex SlugInvalidCharsRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleHyphensRegex();
}

/// <summary>
/// 分类配置
/// </summary>
public sealed class TaxonomyConfig
{
    /// <summary>
    /// 单数形式
    /// </summary>
    public required string Singular { get; init; }

    /// <summary>
    /// 复数形式
    /// </summary>
    public required string Plural { get; init; }

    /// <summary>
    /// 排序方式
    /// </summary>
    public TaxonomySortBy SortBy { get; init; } = TaxonomySortBy.Name;

    /// <summary>
    /// 是否在 URL 中使用复数形式
    /// </summary>
    public bool UsePluralInUrl { get; init; } = true;
}

/// <summary>
/// 分类排序方式
/// </summary>
public enum TaxonomySortBy
{
    /// <summary>
    /// 按名称排序
    /// </summary>
    Name,

    /// <summary>
    /// 按数量排序
    /// </summary>
    Count,

    /// <summary>
    /// 按权重排序
    /// </summary>
    Weight
}

/// <summary>
/// 分类页面生成器
/// </summary>
public sealed class TaxonomyPageGenerator
{
    private readonly TaxonomyService _taxonomyService;
    private readonly PaginationService _paginationService;

    /// <summary>
    /// 创建分类页面生成器
    /// </summary>
    public TaxonomyPageGenerator(
        TaxonomyService taxonomyService,
        PaginationService paginationService)
    {
        _taxonomyService = taxonomyService;
        _paginationService = paginationService;
    }

    /// <summary>
    /// 生成所有分类页面信息
    /// </summary>
    /// <param name="taxonomies">分类集合</param>
    /// <param name="pageSize">每页大小</param>
    /// <returns>分类页面信息列表</returns>
    public IReadOnlyList<TaxonomyPageInfo> GeneratePages(
        TaxonomyCollection taxonomies,
        int pageSize = 10)
    {
        var pages = new List<TaxonomyPageInfo>();

        foreach (var (taxonomyName, terms) in taxonomies.Taxonomies)
        {
            // 生成分类列表页
            var listPermalink = _taxonomyService.GenerateTaxonomyListPermalink(taxonomyName);
            pages.Add(new TaxonomyPageInfo
            {
                TaxonomyName = taxonomyName,
                TermName = null,
                PageType = TaxonomyPageType.TaxonomyList,
                Permalink = listPermalink,
                Terms = terms,
                Pages = null,
                PageNumber = 1,
                TotalPages = 1
            });

            // 为每个术语生成页面
            foreach (var term in terms)
            {
                var totalPages = _paginationService.CalculateTotalPages(term.Pages.Count, pageSize);

                for (var pageNum = 1; pageNum <= totalPages; pageNum++)
                {
                    var paginator = _paginationService.Paginate(term.Pages, pageNum, pageSize);
                    var permalink = pageNum == 1
                        ? term.Permalink
                        : $"{term.Permalink?.TrimEnd('/')}/page/{pageNum}/";

                    pages.Add(new TaxonomyPageInfo
                    {
                        TaxonomyName = taxonomyName,
                        TermName = term.Name,
                        TermSlug = term.Slug,
                        PageType = TaxonomyPageType.TermPage,
                        Permalink = permalink,
                        Terms = null,
                        Pages = paginator.Items,
                        PageNumber = pageNum,
                        TotalPages = totalPages,
                        TotalItems = term.Pages.Count
                    });
                }
            }
        }

        return pages;
    }
}

/// <summary>
/// 分类页面信息
/// </summary>
public sealed class TaxonomyPageInfo
{
    /// <summary>
    /// 分类名称
    /// </summary>
    public required string TaxonomyName { get; init; }

    /// <summary>
    /// 术语名称（如果是术语页面）
    /// </summary>
    public string? TermName { get; init; }

    /// <summary>
    /// 术语 Slug
    /// </summary>
    public string? TermSlug { get; init; }

    /// <summary>
    /// 页面类型
    /// </summary>
    public required TaxonomyPageType PageType { get; init; }

    /// <summary>
    /// 永久链接
    /// </summary>
    public required string? Permalink { get; init; }

    /// <summary>
    /// 术语列表（分类列表页使用）
    /// </summary>
    public IReadOnlyList<TaxonomyTerm>? Terms { get; init; }

    /// <summary>
    /// 页面列表（术语页面使用）
    /// </summary>
    public IReadOnlyList<PageContext>? Pages { get; init; }

    /// <summary>
    /// 当前页码
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// 总页数
    /// </summary>
    public required int TotalPages { get; init; }

    /// <summary>
    /// 总项目数
    /// </summary>
    public int TotalItems { get; init; }
}

/// <summary>
/// 分类页面类型
/// </summary>
public enum TaxonomyPageType
{
    /// <summary>
    /// 分类列表页（显示所有术语）
    /// </summary>
    TaxonomyList,

    /// <summary>
    /// 术语页面（显示术语下的所有内容）
    /// </summary>
    TermPage
}

// Flint 静态站点生成器
// 分类系统服务实现

using System.Text.RegularExpressions;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;

namespace Flint.Core.Site;

/// <summary>
/// 分类系统服务
/// 管理标签、分类等分类法
/// </summary>
public sealed partial class TaxonomyService
{
    /// <summary>
    /// 分类页标题（Hugo v0.166 实测）：
    /// <list type="bullet">
    /// <item>taxonomy 列表页：复数名把 <c>-</c> 换成空格后 Title 化——
    /// <c>/tags/</c> → "Tags"、<c>/my-series/</c> → "My Series"</item>
    /// <item>term 词条页：词条值**原样** Title 化（连字符保留）——
    /// <c>/tags/alpha/</c> → "Alpha"、<c>/my-series/first-run/</c> → "First-Run"</item>
    /// </list>
    /// Title 化采用 Go <c>strings.Title</c> 的语义：只把"非字母数字之后的首字母"
    /// 大写，其余字符原样保留（不做小写化、不按单词表处理）。
    /// </summary>
    /// <param name="name">taxonomy 复数名（列表页）或词条值（term 页）</param>
    /// <param name="dashToSpace">是否先把连字符转空格（taxonomy 列表页为真）</param>
    internal static string HugoTitle(string name, bool dashToSpace)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var source = dashToSpace ? name.Replace('-', ' ') : name;
        var sb = new System.Text.StringBuilder(source.Length);
        var atWordStart = true;
        foreach (var ch in source)
        {
            sb.Append(atWordStart && char.IsLetter(ch) ? char.ToUpperInvariant(ch) : ch);
            atWordStart = !char.IsLetterOrDigit(ch);
        }
        return sb.ToString();
    }

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
            // Hugo 实测：**没有词条的分类不产出任何页面**（声明了 [taxonomies]
            // 但内容未使用的分类，/tags/ 这类目录不会出现——FixIt 的
            // `collection = "collections"` 实测）。此前会产出空目录 → 与 Hugo 不对称
            if (terms.Count > 0)
            {
                taxonomies[taxonomyName] = terms;
            }
        }

        return new TaxonomyCollection { Taxonomies = taxonomies };
    }

    /// <summary>
    /// 取分类的单数/复数名（对齐 Hugo <c>.Data.Singular</c> / <c>.Data.Plural</c>）。
    /// 未注册的自定义分类退化为名字本身（不抛异常，宽容对齐 Hugo）
    /// </summary>
    public (string Singular, string Plural) GetTaxonomyNames(string taxonomyName)
    {
        return _taxonomyConfigs.TryGetValue(taxonomyName, out var config)
            ? (config.Singular, config.Plural)
            : (taxonomyName, taxonomyName);
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
    /// <summary>
    /// 由站点配置构造分类注册表（Hugo v0.166 语义，实测）：
    /// <list type="bullet">
    /// <item>配置形如 <c>[taxonomies] series = "my-series"</c>——**键=单数名、值=复数名**；
    /// 复数名同时是 front matter 字段名与 URL 段（内容里写 <c>my-series: [...]</c>，
    /// 产出 <c>/my-series/</c>）</item>
    /// <item>声明了 <c>[taxonomies]</c> 就**替换**默认的 tags/categories
    ///（实测：只声明 <c>series = "my-series"</c> 时，即使页面有 tags，也不产出 /tags/）</item>
    /// <item>未声明时用默认 tags/categories</item>
    /// </list>
    /// 此前构造点从不传配置，注册表恒为默认两项 → 自定义分类页**从不产出**
    ///（探针：内容含 my-series/moods 时 Flint 只产出 tags/categories）
    /// </summary>
    /// <param name="configured">
    /// 站点配置里的 [taxonomies]（键=单数、值=复数）。注意类型名冲突：
    /// <see cref="Flint.Core.Configuration.TaxonomyConfig"/> 是**站点配置**（一个字典），
    /// 而本命名空间下的 <see cref="TaxonomyConfig"/> 是**单个分类的描述符**——故此处写全名
    /// </param>
    public static Dictionary<string, TaxonomyConfig> FromConfigured(
        Flint.Core.Configuration.TaxonomyConfig? configured)
    {
        if (configured?.Taxonomies is not { Count: > 0 } declared)
        {
            return GetDefaultTaxonomyConfigs();
        }

        var result = new Dictionary<string, TaxonomyConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var (singular, plural) in declared)
        {
            if (string.IsNullOrWhiteSpace(plural))
            {
                continue;
            }
            // 注册表按**复数**索引（= front matter 字段名 = URL 段）
            result[plural] = new TaxonomyConfig
            {
                Singular = string.IsNullOrWhiteSpace(singular) ? plural : singular,
                Plural = plural,
                SortBy = TaxonomySortBy.Name
            };
        }
        return result.Count > 0 ? result : GetDefaultTaxonomyConfigs();
    }

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
            var (singular, plural) = _taxonomyService.GetTaxonomyNames(taxonomyName);
            // 生成分类列表页：**词条集合**按 pagerSize 分页（Hugo 实测：/tags/ 的
            // .Paginator 切词条页 → 产出 /tags/page/2/；此前只产 1 页，缺分页页）
            var listPermalink = _taxonomyService.GenerateTaxonomyListPermalink(taxonomyName);
            var listTotalPages = _paginationService.CalculateTotalPages(terms.Count, pageSize);
            for (var listPageNum = 1; listPageNum <= listTotalPages; listPageNum++)
            {
                pages.Add(new TaxonomyPageInfo
                {
                    TaxonomyName = taxonomyName,
                    TaxonomySingular = singular,
                    TaxonomyPlural = plural,
                    TermName = null,
                    PageType = TaxonomyPageType.TaxonomyList,
                    Permalink = listPageNum == 1
                        ? listPermalink
                        : $"{listPermalink?.TrimEnd('/')}/page/{listPageNum}/",
                    Terms = terms,
                    Pages = null,
                    PageNumber = listPageNum,
                    TotalPages = listTotalPages
                });
            }

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
                        TaxonomySingular = singular,
                        TaxonomyPlural = plural,
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
    /// 分类单数名（对齐 Hugo <c>.Data.Singular</c>，如 category）
    /// </summary>
    public string? TaxonomySingular { get; init; }

    /// <summary>
    /// 分类复数名（对齐 Hugo <c>.Data.Plural</c>，如 categories）
    /// </summary>
    public string? TaxonomyPlural { get; init; }

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

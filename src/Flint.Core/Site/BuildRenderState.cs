// Flint 静态站点生成器
// 构建渲染状态（引擎中立）：分页追踪/全量页面/站点对象/分类查找的构建级
// 共享状态。从 ScribanTemplateRenderer 搬移至此（2026-10-21 架构居中于
// Fluid：SiteBuilder 不再静态调用 ScribanTemplateRenderer，Scriban 渲染器
// 内部经本类访问同一份状态）。
//
// AsyncLocal 按执行流隔离：同进程并行构建（集成测试多站点 in-process）
// 互不串写；分页字典用并发类型（同一次构建内页面渲染本身是并行的）。

using System.Collections.Concurrent;
using Flint.Core.Abstractions;

namespace Flint.Core.Site;

/// <summary>
/// 构建级共享状态：分页追踪、全量页面、站点对象、分类查找注册表。
/// 经 <see cref="BeginBuildScope"/> 在构建入口同步前缀开新作用域，
/// 后续 await/并行渲染子任务继承同一实例
/// </summary>
internal sealed class BuildRenderState
{
    /// <summary>
    /// 本次构建中被模板调用过 <c>.Paginate</c> 的列表页 URL。
    /// Hugo 的分页页由**模板调用**驱动：探测实证（v0.166）home 模板不调用
    /// <c>.Paginate</c> 时站点不产出 <c>/page/2</c>，而列表模板调用时产出
    /// <c>/page/1</c>（跳转页）+ <c>/page/2..N</c>。故分页页产出前先看是否有标记
    /// </summary>
    public ConcurrentDictionary<string, byte> PaginatedListUrls { get; } = new(StringComparer.Ordinal);

    /// <summary>模板传给 <c>.Paginate</c> 的显式集合与页大小（按列表页 URL 登记）</summary>
    public ConcurrentDictionary<string, PaginateRegistration> PaginateCollections { get; } = new(StringComparer.Ordinal);

    /// <summary>模板 <c>.Paginate</c> 实际创建的分页器（按列表页 URL 登记）</summary>
    public ConcurrentDictionary<string, PaginatorView> PaginatePagers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前构建的**全量站点页面**（含 section/term 页）——<c>.GetPage</c> 用它解析路径。
    /// 页面对象按引用共享缓存（CWT），创建时机不同会拿到不同快照（列表渲染早于页面渲染），
    /// 只靠构造参数会让 `<c>.GetPage "docs"</c>` 时有时无地找不到 section（hugo-book 实测），
    /// 故由站点渲染入口统一登记，调用时读取（与分页标记同一模式）
    /// </summary>
    public IReadOnlyList<PageContext>? CurrentSitePages { get; set; }

    /// <summary>
    /// 分类/词条页的**查找注册表**（Hugo 的 .Site.GetPage "/tags/x" 语义）。词条页在
    /// 构建期第 9 阶段才渲染，而列表模板（loveit 的 summary）在第 8 阶段就要按
    /// "/categories/xxx" 查它们拿 rel_permalink/title——故第 7 阶段用 TaxonomyService
    /// 已建好的分类数据预建最小 PageContext 投影登记于此，GetPage 页面列表未命中时
    /// 回退查本表。**不进 site.Pages**：避免 range site.pages 的主题行为变化
    /// </summary>
    public IReadOnlyList<PageContext>? TaxonomyLookupPages { get; set; }

    /// <summary>
    /// 当前构建的**站点对象**——页面对象上的 <c>.Site</c> 用它（Hugo 的 `.Site` 在任意
    /// 页面上可用；迁移产物里的 `$page.Site.Params…` 形态会落到 `$page.site.params`）。
    /// 与 <see cref="CurrentSitePages"/> 同一模式：页面对象按引用共享、构造时机不定，
    /// 故由站点渲染入口统一登记
    /// </summary>
    public SiteContext? CurrentSite { get; set; }
}

/// <summary>
/// 模板传给 <c>.Paginate</c> 的**显式集合与页大小**（Hugo 语义：按传入集合与尺寸分页）
/// </summary>
/// <param name="Items">显式集合（Hugo 的 `.Paginate $pages` 第一参）</param>
/// <param name="Size">显式页大小（`.Paginate $pages N` 第二参）；未给时为 0 → 用站点配置</param>
internal sealed record PaginateRegistration(
    IReadOnlyList<PageContext> Items, int Size);

/// <summary>
/// 构建渲染状态的静态访问点（AsyncLocal 按执行流隔离）。
/// 从 ScribanTemplateRenderer 搬移至此（2026-10-21 架构居中于 Fluid）
/// </summary>
internal static class BuildRenderStateAccess
{
    private static readonly AsyncLocal<BuildRenderState> BuildScope = new();

    /// <summary>
    /// 当前构建作用域。惰性创建兜底"不经 BuildAsync 直接登记/渲染"的测试直调路径；
    /// SiteBuilder 构建入口经 <see cref="BeginBuildScope"/> 显式开新作用域——
    /// 必须在入口**同步前缀**创建（首个 await 之前），后续全部 await/并行渲染子任务
    /// 才会继承同一实例；若首访问落在并行子任务里，各子任务会各自惰性创建互不相通
    /// </summary>
    private static BuildRenderState Scope => BuildScope.Value ??= new BuildRenderState();

    /// <summary>开新构建作用域（BuildAsync/IncrementalBuildAsync 入口同步前缀调用）</summary>
    public static void BeginBuildScope() => BuildScope.Value = new BuildRenderState();

    /// <summary>登记当前构建的全量站点页面（站点渲染开始时调用）</summary>
    public static void SetCurrentSitePages(IReadOnlyList<PageContext> pages) => Scope.CurrentSitePages = pages;

    /// <summary>取当前构建的全量站点页面（未登记时为 null）</summary>
    public static IReadOnlyList<PageContext>? GetCurrentSitePages() => Scope.CurrentSitePages;

    /// <summary>登记分类/词条查找注册表（每次构建前由 SiteBuilder 调用；null 清空）</summary>
    public static void SetTaxonomyLookupPages(IReadOnlyList<PageContext>? pages) =>
        Scope.TaxonomyLookupPages = pages;

    /// <summary>取分类/词条查找注册表（未登记时为 null）</summary>
    public static IReadOnlyList<PageContext>? GetTaxonomyLookupPages() =>
        Scope.TaxonomyLookupPages;

    /// <summary>登记当前构建的站点对象（站点渲染开始时调用）</summary>
    public static void SetCurrentSite(SiteContext site) => Scope.CurrentSite = site;

    /// <summary>取当前构建的站点对象（未登记时为 null）</summary>
    public static SiteContext? GetCurrentSite() => Scope.CurrentSite;

    /// <summary>标记某列表页被模板分页（由 <c>.Paginate</c> 调用触发）</summary>
    public static void NotePaginateInvoked(string? relPermalink)
    {
        if (!string.IsNullOrEmpty(relPermalink))
        {
            Scope.PaginatedListUrls[relPermalink] = 0;
        }
    }

    /// <summary>登记显式分页集合与页大小</summary>
    public static void NotePaginateCollection(
        string? relPermalink, IReadOnlyList<PageContext> items, int size = 0)
    {
        if (!string.IsNullOrEmpty(relPermalink))
        {
            Scope.PaginateCollections[relPermalink] = new PaginateRegistration(items, size);
        }
    }

    /// <summary>取显式分页集合与页大小（无则 null）</summary>
    public static PaginateRegistration? GetPaginateCollection(string relPermalink) =>
        Scope.PaginateCollections.TryGetValue(relPermalink, out var registration) ? registration : null;

    /// <summary>登记模板创建的分页器</summary>
    public static void NotePaginatePager(string? relPermalink, PaginatorView pager)
    {
        if (!string.IsNullOrEmpty(relPermalink))
        {
            Scope.PaginatePagers[relPermalink] = pager;
        }
    }

    /// <summary>取模板创建的分页器（无则 null → 用预绑定的隐式分页器）</summary>
    public static PaginatorView? GetPaginatePager(string relPermalink) =>
        Scope.PaginatePagers.TryGetValue(relPermalink, out var pager) ? pager : null;

    /// <summary>该列表页是否被模板分页过</summary>
    public static bool WasPaginateInvoked(string relPermalink) =>
        Scope.PaginatedListUrls.ContainsKey(relPermalink);

    /// <summary>
    /// 清空分页标记（站点渲染开始时调用）。同一构建内串行重建（增量/热重载）时
    /// 复用同一作用域实例，跨构建串味仍靠此清空；全量/增量入口的
    /// <see cref="BeginBuildScope"/> 则直接换新实例（并行构建互不干扰）
    /// </summary>
    public static void ResetPaginateTracking()
    {
        Scope.PaginatedListUrls.Clear();
        Scope.PaginateCollections.Clear();
        Scope.PaginatePagers.Clear();
    }
}

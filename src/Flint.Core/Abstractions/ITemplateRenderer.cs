// Flint 静态站点生成器
// 模板渲染器接口

using Flint.Core.Configuration;
using Flint.Core.Templates;

namespace Flint.Core.Abstractions;

/// <summary>
/// 模板渲染器接口
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// 异步渲染模板
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <param name="context">模板上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>渲染后的 HTML</returns>
    ValueTask<string> RenderAsync(
        string templateName,
        TemplateContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 页面感知渲染（A 组查找链）：按页面特征（kind/layout/type/section/taxonomy
    /// + 输出格式）构造有序候选链，逐级解析并渲染。
    /// 对齐 Hugo lookup order 的"页面 → 模板"语义；候选链全部未命中时
    /// 回退内置模板（taxonomy/term/list），仍无则抛 <c>TemplateNotFoundException</c>。
    /// </summary>
    ValueTask<string> RenderPageAsync(
        PageTemplateQuery query,
        TemplateContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 页面感知的物理模板存在性（不含内置兜底模板）：
    /// 候选链命中任一物理模板即 true。用于输出格式变体产出判定。
    /// </summary>
    bool PageTemplateExists(PageTemplateQuery query);

    /// <summary>
    /// 检查模板是否存在
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <returns>是否存在</returns>
    bool TemplateExists(string templateName);

    /// <summary>
    /// 渲染指定模板文件（不走模板名查找链，用于 robots.txt 等非 .html 形态辅助模板）
    /// </summary>
    ValueTask<string> RenderTemplateFileAsync(
        string filePath,
        TemplateContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取模板的依赖列表
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <returns>依赖的模板名称列表</returns>
    IReadOnlyList<string> GetDependencies(string templateName);

    /// <summary>
    /// 预编译所有模板（可选优化：首次构建时把模板加载进内存）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>预编译的模板数量</returns>
    Task<int> PrecompileTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空模板 mtime 短窗缓存（构建边界调用，保证每次构建看到模板最新状态）
    /// </summary>
    void InvalidateMtimeCache();

    /// <summary>
    /// 本构建内模板函数产生的资源产物（resources.Concat/FromString/Fingerprint 等），
    /// 需由输出阶段写盘——否则模板引用的 RelPermalink 会 404。
    /// 非资源类渲染器实现可返回空列表
    /// </summary>
    IReadOnlyList<Templates.TemplateResource> GeneratedResources => [];

    /// <summary>
    /// 取走渲染期由模板 <c>errorf</c> 记录的错误（取走后清空）。
    /// 对齐 Hugo 语义：<c>errorf</c> 只记录错误并继续渲染（页面照常产出），
    /// 构建收尾按错误计数判定失败——见 <see cref="BuiltinTemplateFunctions"/> 的 errorf。
    /// 不汇聚模板错误的实现可返回空列表
    /// </summary>
    IReadOnlyList<string> DrainTemplateErrors() => [];
}

/// <summary>
/// 模板上下文
/// </summary>
public sealed class TemplateContext
{
    /// <summary>
    /// 页面上下文
    /// </summary>
    public required PageContext Page { get; init; }

    /// <summary>
    /// 站点上下文
    /// </summary>
    public required SiteContext Site { get; init; }

    /// <summary>
    /// 自定义参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 数据文件内容
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 当前语言
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// 是否为首页
    /// </summary>
    public bool IsHome { get; init; }

    /// <summary>
    /// 是否为列表页
    /// </summary>
    public bool IsList { get; init; }

    /// <summary>
    /// 是否为单页
    /// </summary>
    public bool IsSingle { get; init; }

    /// <summary>
    /// 当前页面集合（Hugo <c>.Pages</c> 语义）：term 页为该词条的页面列表；
    /// null 时模板变量 <c>pages</c> 回落为全站 regular_pages（旧行为）
    /// </summary>
    public IReadOnlyList<PageContext>? Pages { get; init; }

    /// <summary>
    /// 渲染期依赖收集结果（T4.1）：渲染完成后由渲染器填充——
    /// 实际 include 的模板物理路径与 <c>data:site.*</c> 数据访问键
    /// </summary>
    public IReadOnlySet<string>? RenderedDependencies { get; set; }
}

/// <summary>
/// 分页视图（C1）：一个列表页被切成 N 个 pager 时，某一页的绑定数据。
/// 对齐 Hugo Pager 字段子集（Pages/PageNumber/TotalPages/PagerSize/URL/
/// NumberOfElements/TotalNumberOfElements/HasPrev/HasNext/Prev/Next/First/Last/Pagers）。
/// 由构建器逐页构造，渲染器转成模板对象（AOT 安全，无反射）。
/// </summary>
public sealed class PaginatorView
{
    /// <summary>全量页面集合（各 pager 的 Pages 由它按页切片）</summary>
    public required IReadOnlyList<PageContext> AllItems { get; init; }

    /// <summary>当前页码（从 1 开始）</summary>
    public required int PageNumber { get; init; }

    /// <summary>每页大小</summary>
    public required int PageSize { get; init; }

    /// <summary>列表页基准 URL（以 / 结尾，如 /posts/）</summary>
    public required string BaseRelPermalink { get; init; }

    /// <summary>分页路径段（对齐 Hugo paginatePath，默认 page）</summary>
    public required string PaginatePath { get; init; }

    private IReadOnlyList<PageContext>? _currentSlice;

    /// <summary>当前页的项目切片</summary>
    public IReadOnlyList<PageContext> Pages => _currentSlice ??= Slice(PageNumber);

    /// <summary>总项目数</summary>
    public int TotalItems => AllItems.Count;

    /// <summary>总页数（空集合恒 1，对齐 Hugo：空列表也有一页）</summary>
    public int TotalPages => TotalItems > 0
        ? (int)Math.Ceiling(TotalItems / (double)PageSize)
        : 1;

    /// <summary>当前页项目数</summary>
    public int NumberOfElements => Pages.Count;

    /// <summary>是否有上一页</summary>
    public bool HasPrev => PageNumber > 1;

    /// <summary>是否有下一页</summary>
    public bool HasNext => PageNumber < TotalPages;

    /// <summary>是否首页</summary>
    public bool IsFirst => PageNumber == 1;

    /// <summary>是否末页</summary>
    public bool IsLast => PageNumber == TotalPages;

    /// <summary>指定页码的切片（页码越界时钳制到有效区间）</summary>
    public IReadOnlyList<PageContext> Slice(int pageNumber)
    {
        var clamped = Math.Max(1, Math.Min(pageNumber, TotalPages));
        var skip = (clamped - 1) * PageSize;
        return AllItems.Skip(skip).Take(PageSize).ToList();
    }

    /// <summary>指定页码的 URL（首页 = 基准 URL，第 N 页 = {基准}{paginatePath}/{N}/）</summary>
    public string UrlForPage(int pageNumber)
    {
        if (pageNumber <= 1)
        {
            return BaseRelPermalink;
        }
        return $"{BaseRelPermalink.TrimEnd('/')}/{PaginatePath}/{pageNumber}/";
    }

    /// <summary>当前页 URL</summary>
    public string URL => UrlForPage(PageNumber);

    /// <summary>指定页码的分页视图（共享 AllItems，切片按需）</summary>
    public PaginatorView ForPage(int pageNumber) => new()
    {
        AllItems = AllItems,
        PageNumber = Math.Max(1, Math.Min(pageNumber, TotalPages)),
        PageSize = PageSize,
        BaseRelPermalink = BaseRelPermalink,
        PaginatePath = PaginatePath
    };

    /// <summary>首页 pager（对齐 Hugo .Paginator.First）</summary>
    public PaginatorView First => ForPage(1);

    /// <summary>末页 pager</summary>
    public PaginatorView Last => ForPage(TotalPages);

    /// <summary>上一页 pager（无则 null）</summary>
    public PaginatorView? Prev => HasPrev ? ForPage(PageNumber - 1) : null;

    /// <summary>下一页 pager（无则 null）</summary>
    public PaginatorView? Next => HasNext ? ForPage(PageNumber + 1) : null;

    /// <summary>全部分页视图（对齐 Hugo .Paginator.Pagers）</summary>
    public IReadOnlyList<PaginatorView> Pagers => _pagers ??=
        Enumerable.Range(1, TotalPages).Select(ForPage).ToList();

    private IReadOnlyList<PaginatorView>? _pagers;

    /// <summary>
    /// 构造分页视图：pageSize &lt; 1 视为 1（防御除零），页码钳制到 [1, TotalPages]
    /// </summary>
    public static PaginatorView Create(
        IReadOnlyList<PageContext> allItems,
        int pageNumber,
        int pageSize,
        string baseRelPermalink,
        string paginatePath)
    {
        ArgumentNullException.ThrowIfNull(allItems);
        var size = Math.Max(1, pageSize);
        var totalPages = allItems.Count > 0
            ? (int)Math.Ceiling(allItems.Count / (double)size)
            : 1;
        var baseRel = string.IsNullOrEmpty(baseRelPermalink) ? "/" : baseRelPermalink;
        if (baseRel[0] != '/')
        {
            baseRel = "/" + baseRel;
        }
        if (!baseRel.EndsWith('/'))
        {
            baseRel += "/";
        }

        return new PaginatorView
        {
            AllItems = allItems,
            PageNumber = Math.Max(1, Math.Min(pageNumber, totalPages)),
            PageSize = size,
            BaseRelPermalink = baseRel,
            PaginatePath = string.IsNullOrEmpty(paginatePath) ? "page" : paginatePath
        };
    }
}

/// <summary>
/// 页面上下文
/// </summary>
public sealed class PageContext
{
    /// <summary>
    /// 页面标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 本次渲染的输出格式（html/rss/json，对齐 Hugo per-output-format）；
    /// 同一页面的不同格式实例共享页面不变部分（含 Markdown 转换结果）
    /// </summary>
    public string OutputFormat { get; init; } = "html";

    /// <summary>
    /// 页面内容（HTML）
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 永久链接（绝对 URL）
    /// </summary>
    public required string Permalink { get; init; }

    /// <summary>
    /// 相对永久链接
    /// </summary>
    public required string RelPermalink { get; init; }

    /// <summary>
    /// 发布日期
    /// </summary>
    public required DateTimeOffset Date { get; init; }

    /// <summary>
    /// 最后修改日期
    /// </summary>
    public DateTimeOffset? LastMod { get; init; }

    /// <summary>
    /// 标签列表
    /// </summary>
    public required IReadOnlyList<string> Tags { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// 分类列表
    /// </summary>
    public required IReadOnlyList<string> Categories { get; init; }

    /// <summary>
    /// 字数统计
    /// </summary>
    public required int WordCount { get; init; }

    /// <summary>
    /// 阅读时间
    /// </summary>
    public required TimeSpan ReadingTime { get; init; }

    /// <summary>
    /// 页面描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 页面摘要
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// 上一页
    /// </summary>
    public PageContext? PrevPage { get; init; }

    /// <summary>
    /// 下一页
    /// </summary>
    public PageContext? NextPage { get; init; }

    /// <summary>
    /// 页面类型
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// 页面 kind（home / section / page / taxonomy / term）——模板查找链的 kind
    /// 维度与模板 <c>.Kind</c> 的来源。与 <see cref="Type"/> 分离：Type 保留
    /// "front matter 显式 type ?? kind" 的既有模板语义（Hugo 中 .Kind 与 .Type
    /// 是两个独立概念，此前 Flint 用单一字段承载导致查找链无法区分）
    /// </summary>
    public string Kind { get; init; } = "page";

    /// <summary>
    /// front matter 显式声明的 type（未声明为 null）——模板查找链的 type 维度。
    /// 缺省时由 <see cref="Templates.PageTemplateQuery.EffectiveType"/> 回退到 section 名
    /// （对齐 Hugo：type 默认取根 section 名）。Ananke 的 <c>type: page</c> 页面
    /// 依赖此字段命中 <c>layouts/page/single.html</c>
    /// </summary>
    public string? DeclaredType { get; init; }

    /// <summary>
    /// taxonomy 名（仅 taxonomy/term 页非空，如 categories）——模板查找链的
    /// taxonomy 维度（<c>layouts/{taxonomy}/terms.html</c> 等候选级）
    /// </summary>
    public string? TaxonomyName { get; init; }

    /// <summary>链接标题（对齐 Hugo .LinkTitle：缺省回退 Title；菜单/列表常用）</summary>
    public string? LinkTitle { get; init; }

    /// <summary>摘要是否被截断过（对齐 Hugo .Truncated）</summary>
    public bool Truncated { get; init; }

    /// <summary>页面逻辑路径（对齐 Hugo .Path，如 /posts/p1；home 为 /）</summary>
    public string? PagePath { get; init; }

    /// <summary>bundle 类型（leaf/branch/single，对齐 Hugo .BundleType）</summary>
    public string? BundleType { get; init; }

    /// <summary>发布日期（对齐 Hugo .PublishDate，缺省回退 Date）</summary>
    public DateTimeOffset? PublishDate { get; init; }

    /// <summary>失效日期（对齐 Hugo .ExpiryDate）</summary>
    public DateTimeOffset? ExpiryDate { get; init; }

    /// <summary>
    /// 页面语言代码（对齐 Hugo .Page.Language；单语言站点为站点语言）
    /// </summary>
    public string? Language { get; init; }

    /// <summary>模糊字数（对齐 Hugo .FuzzyWordCount：百位近似）</summary>
    public int FuzzyWordCount => WordCount < 100
        ? WordCount
        : (int)Math.Round(WordCount / 100.0, MidpointRounding.AwayFromZero) * 100;

    /// <summary>
    /// 分类单数名（对齐 Hugo <c>.Data.Singular</c>）
    /// </summary>
    public string? TaxonomySingular { get; init; }

    /// <summary>
    /// 分类复数名（对齐 Hugo <c>.Data.Plural</c>）
    /// </summary>
    public string? TaxonomyPlural { get; init; }

    /// <summary>
    /// 页面布局
    /// </summary>
    public string? Layout { get; init; }

    /// <summary>
    /// 页面输出格式（对齐 Hugo 页面级 outputs 覆盖）：空 = 按 kind 默认（html）。
    /// 非 html 格式（如 json）需存在对应输出格式变体模板（如 single.json.html）
    /// 才会产出
    /// </summary>
    public IReadOnlyList<string> Outputs { get; init; } = [];

    /// <summary>
    /// 源内容文件路径（用于增量构建的依赖追踪）
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// 是否为草稿
    /// </summary>
    public bool Draft { get; init; }

    /// <summary>
    /// 页面权重
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 当前页面集合（Hugo <c>.Pages</c> 语义）：term 页为该词条下的页面列表；
    /// 其余页面 null——模板 page.pages 无值时应回落 site.regular_pages
    /// </summary>
    public IReadOnlyList<PageContext>? Pages { get; init; }

    /// <summary>
    /// 本页**直属**的 section 子页集合（对齐 Hugo <c>.Sections</c>）：home 为一级
    /// section 页，section 为下级 section 页，其余页面为空。与 <see cref="Pages"/>
    /// 同在站点构建的树阶段装配，随页面对象一起流动——供主题遍历站点结构
    /// （techdoc 的菜单 partial 用 <c>site.home.sections.by_weight</c>）
    /// </summary>
    public IReadOnlyList<PageContext>? Sections { get; init; }

    /// <summary>
    /// 本页绑定的分页器（C1）：列表页的第 N 页渲染实例携带该 pager；
    /// 非列表页/非分页渲染为 null（模板 <c>page.paginator</c> 返回空）
    /// </summary>
    public PaginatorView? Paginator { get; init; }

    /// <summary>
    /// 页面所属一级 section 名（对齐 Hugo <c>.Section</c>）：路径首段；
    /// 首页为空串。主题常用 <c>where ... "Section" ...</c> 做 section 过滤
    /// </summary>
    public string Section { get; init; } = "";

    /// <summary>
    /// taxonomy 列表页的词条集合（对齐 Hugo <c>.Data.Terms</c>）：仅 taxonomy 页
    /// 非 null，供模板 page.terms 枚举词条（默认主题模板 page.pages/page.terms
    /// 依赖此二者，缺省时词条/分类页渲染为空列表）
    /// </summary>
    public IReadOnlyList<TaxonomyTerm>? Terms { get; init; }

    /// <summary>
    /// 页面 front matter 声明的菜单条目（menu 标识 → 条目配置）：
    /// 供 MenuBuilder 汇入全站菜单；URL 缺省时由页面自身 RelPermalink 补齐
    /// </summary>
    public IReadOnlyDictionary<string, MenuItemConfig>? MenuEntries { get; init; }

    /// <summary>
    /// 自定义参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 页面资源
    /// </summary>
    public IReadOnlyList<object> Resources { get; init; } = [];

    /// <summary>
    /// 目录 HTML（对齐 Hugo <c>.TableOfContents</c>：<c>&lt;nav id="TableOfContents"&gt;</c>
    /// + 层级嵌套的 <c>&lt;ul&gt;</c>；层级区间由 <c>markup.tableOfContents</c> 配置决定，
    /// 无标题时为空串）。注意是**字符串**不是列表——主题用 <c>replace</c>/<c>in</c>
    /// 做字符串判定（Blowfish/Congo 实测）
    /// </summary>
    public string TableOfContents { get; init; } = "";

    /// <summary>
    /// 文档标题列表（模板 <c>.Fragments</c> 的数据源：Identifiers / Headings / ToHTML）
    /// </summary>
    public IReadOnlyList<MarkdownHeading> Headings { get; init; } = [];

    /// <summary>
    /// 纯文本内容
    /// </summary>
    public string? Plain { get; init; }

    /// <summary>
    /// 原始 Markdown 内容
    /// </summary>
    public string? RawContent { get; init; }

    /// <summary>
    /// 以指定集合派生一个页面实例（列表页装配 <c>.Pages</c> 用）：
    /// 浅拷贝全部字段，仅替换 Pages
    /// </summary>
    public PageContext WithPages(IReadOnlyList<PageContext> pages) =>
        Clone(Permalink, RelPermalink, pages, Paginator);

    /// <summary>
    /// 以指定 section 集合派生一个页面实例（对齐 Hugo <c>.Sections</c>）：
    /// 浅拷贝全部字段，仅替换 Sections
    /// </summary>
    public PageContext WithSections(IReadOnlyList<PageContext> sections) =>
        Clone(Permalink, RelPermalink, Pages, Paginator, sections);

    /// <summary>
    /// 以指定分页器派生一个页面实例（C1 分页多页）：浅拷贝全部字段，
    /// Paginator 绑定为该 pager，RelPermalink/Permalink/Pages 指向该页
    /// （第 1 页即列表页自身 URL，与分页前一致）。
    /// 页面对象在构建内共享，分页实例必须是独立对象——不得原地修改共享实例
    /// </summary>
    public PageContext WithPaginator(PaginatorView paginator, string baseUrl)
    {
        var rel = paginator.URL;
        // Hugo 语义：pager 页面的 .Pages 即该页切片
        return Clone(
            baseUrl.TrimEnd('/') + rel, rel, paginator.Pages, paginator);
    }

    /// <summary>共享浅拷贝实现：仅 Permalink/RelPermalink/Pages/Paginator 可变</summary>
    private PageContext Clone(
        string permalink,
        string relPermalink,
        IReadOnlyList<PageContext>? pages,
        PaginatorView? paginator,
        IReadOnlyList<PageContext>? sections = null)
    {
        return new PageContext
        {
            Title = Title,
            OutputFormat = OutputFormat,
            Content = Content,
            Permalink = permalink,
            RelPermalink = relPermalink,
            Date = Date,
            LastMod = LastMod,
            Tags = Tags,
            Aliases = Aliases,
            Categories = Categories,
            WordCount = WordCount,
            ReadingTime = ReadingTime,
            Description = Description,
            Summary = Summary,
            PrevPage = PrevPage,
            NextPage = NextPage,
            Type = Type,
            Kind = Kind,
            DeclaredType = DeclaredType,
            TaxonomyName = TaxonomyName,
            TaxonomySingular = TaxonomySingular,
            TaxonomyPlural = TaxonomyPlural,
            LinkTitle = LinkTitle,
            Language = Language,
            Truncated = Truncated,
            PagePath = PagePath,
            BundleType = BundleType,
            PublishDate = PublishDate,
            ExpiryDate = ExpiryDate,
            Layout = Layout,
            Outputs = Outputs,
            SourcePath = SourcePath,
            Draft = Draft,
            Weight = Weight,
            Pages = pages,
            Sections = sections ?? Sections,
            Terms = Terms,
            MenuEntries = MenuEntries,
            Params = Params,
            Resources = Resources,
            TableOfContents = TableOfContents,
            Headings = Headings,
            Plain = Plain,
            RawContent = RawContent,
            Paginator = paginator,
            Section = Section
        };
    }
}

/// <summary>
/// 站点上下文
/// </summary>
public sealed class SiteContext
{
    /// <summary>
    /// 站点标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 基础 URL
    /// </summary>
    public required string BaseURL { get; init; }

    /// <summary>
    /// 语言代码
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// 所有页面
    /// </summary>
    public required IReadOnlyList<PageContext> Pages { get; init; }

    /// <summary>
    /// 常规页面（不含列表页）
    /// </summary>
    public required IReadOnlyList<PageContext> RegularPages { get; init; }

    /// <summary>
    /// 分类系统
    /// </summary>
    public required TaxonomyCollection Taxonomies { get; init; }

    /// <summary>
    /// 菜单集合
    /// </summary>
    public required MenuCollection Menus { get; init; }

    /// <summary>
    /// 站点配置
    /// </summary>
    public required SiteConfig Config { get; init; }

    /// <summary>
    /// 数据文件
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 站点参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 构建时间
    /// </summary>
    public DateTimeOffset BuildDate { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTimeOffset? LastChange { get; init; }

    /// <summary>
    /// 是否为多语言站点
    /// </summary>
    public bool IsMultiLingual { get; init; }

    /// <summary>
    /// 可用语言列表
    /// </summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>
    /// i18n 翻译表（站点覆盖主题，键为翻译 ID）；
    /// i18n 模板函数消费，缺键返回空串（对齐 Hugo）
    /// </summary>
    public IReadOnlyDictionary<string, string> Translations { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// 站点级分页回落值（首页切片）：列表页未逐页绑定分页器时（如非列表页或
    /// 分页关闭）模板 <c>site.paginator</c> / 全局 <c>paginator</c> 的取值。
    /// 分页多页产出见 <see cref="PageContext.Paginator"/> 的逐页绑定
    /// </summary>
    public IReadOnlyList<PageContext> PaginatorPages { get; init; } = [];

    /// <summary>站点级分页总页数（回落值；分页渲染以 PageContext.Paginator 为准）</summary>
    public int PaginatorTotalPages { get; init; } = 1;

    /// <summary>站点级分页页码（回落值，恒 1；分页渲染以 PageContext.Paginator 为准）</summary>
    public int PaginatorPageNumber { get; init; } = 1;
}

/// <summary>
/// 分类系统集合
/// </summary>
public sealed class TaxonomyCollection
{
    /// <summary>
    /// 分类字典
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>> Taxonomies { get; init; }

    /// <summary>
    /// 获取指定分类的所有术语
    /// </summary>
    public IReadOnlyList<TaxonomyTerm> this[string taxonomy] =>
        Taxonomies.TryGetValue(taxonomy, out var terms) ? terms : [];
}

/// <summary>
/// 分类术语
/// </summary>
public sealed class TaxonomyTerm
{
    /// <summary>
    /// 术语名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL 友好的名称
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// 关联的页面列表
    /// </summary>
    public required IReadOnlyList<PageContext> Pages { get; init; }

    /// <summary>
    /// 页面数量
    /// </summary>
    public int Count => Pages.Count;

    /// <summary>
    /// 永久链接
    /// </summary>
    public string? Permalink { get; init; }
}

/// <summary>
/// 菜单集合
/// </summary>
public sealed class MenuCollection
{
    /// <summary>
    /// 菜单字典
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<MenuItem>> Menus { get; init; }

    /// <summary>
    /// 获取指定名称的菜单
    /// </summary>
    public IReadOnlyList<MenuItem> this[string name] =>
        Menus.TryGetValue(name, out var items) ? items : [];
}

/// <summary>
/// 菜单项
/// </summary>
public sealed record MenuItem
{
    /// <summary>
    /// 菜单项名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 菜单项 URL
    /// </summary>
    public required string URL { get; init; }

    /// <summary>
    /// 菜单项权重
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 菜单项标识
    /// </summary>
    public string? Identifier { get; init; }

    /// <summary>
    /// 父菜单项标识
    /// </summary>
    public string? Parent { get; init; }

    /// <summary>
    /// 前置内容
    /// </summary>
    public string? Pre { get; init; }

    /// <summary>
    /// 后置内容
    /// </summary>
    public string? Post { get; init; }

    /// <summary>
    /// 子菜单项
    /// </summary>
    public IReadOnlyList<MenuItem> Children { get; init; } = [];

    /// <summary>
    /// 是否有子菜单
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// 是否为当前活动项
    /// </summary>
    public bool IsActive { get; init; }
}

// Flint 静态站点生成器
// ScribanTemplateRenderer 的对象构建部分：page/site/menus ScriptObject 工厂、
// 惰加载包装器与日期对象注册（从 ScribanTemplateRenderer.cs 按 partial 拆出）

using System.Runtime.CompilerServices;
using Scriban.Runtime;
using Scriban.Parsing;
using Flint.Core.Abstractions;
using FlintMenuCollection = Flint.Core.Abstractions.MenuCollection;
using FlintMenuItem = Flint.Core.Abstractions.MenuItem;
using FlintPageContext = Flint.Core.Abstractions.PageContext;
using FlintSiteContext = Flint.Core.Abstractions.SiteContext;
using FlintTaxonomyCollection = Flint.Core.Abstractions.TaxonomyCollection;

namespace Flint.Core.Templates;

/// <summary>
/// 基于 Scriban 的模板渲染器实现——对象构建部分
/// （主文件：ScribanTemplateRenderer.cs，含编译缓存/渲染入口/依赖收集）
/// </summary>
public sealed partial class ScribanTemplateRenderer
{
    // 页面集合的跨渲染包装缓存（T4.3 值缓存的分层落地）：
    // 同一源列表（同一次构建的 SiteContext.Pages 等）复用同一 LazyPageList——
    // PageContext→ScriptObject 转换全构建只做一次（万页站点每次渲染省 O(n) 转换）；
    // 新构建产生新列表实例，按引用自动失效。CWT 防列表驻留导致缓存泄漏
    private static readonly ConditionalWeakTable<IReadOnlyList<FlintPageContext>, LazyPageList> SharedPageLists = new();

    // 页面对象复用缓存：同一 PageContext 的 ScriptObject 跨渲染共享——
    // prev/next 交叉引用（A 的 prev 是 B）时复用 B 的对象避免重复全键构建；
    // CWT 键为 PageContext 引用，随构建周期回收
    private static readonly ConditionalWeakTable<FlintPageContext, LazyPageObject> SharedPageObjects = new();

    // 站点对象复用缓存：同一 SiteContext 的 site ScriptObject 跨渲染共享——
    // 站点级数据（title/params/menus/taxonomies）在单次构建内对所有页面相同，
    // 万页构建下每页重建 ~30 键 ScriptObject + menus 双份列表是纯浪费。
    // 依赖记录写入 per-render TemplateContext.Tags（非对象自身状态），复用安全；
    // 模板对 site.* 显式赋值属非常规用法（与 SharedPageObjects 同一风险模型）。
    // CWT 键为 SiteContext 引用，随构建周期回收
    private static readonly ConditionalWeakTable<FlintSiteContext, ScriptObject> SharedSiteObjects = new();

    private static ScriptObject CreateSiteObject(FlintSiteContext site)
    {
        return SharedSiteObjects.GetValue(site, static s => BuildSiteObject(s));
    }

        /// <summary>
        /// 识别"分组集合"并返回分组边界（`GroupByDate`/`GroupBy` 的产物：元素带
        /// <c>key</c> 与 <c>pages</c>）。非分组形状返回 null
        /// </summary>
        /// <summary>
        /// 页面序列的**元素个数**：**不能**用非泛型 <see cref="System.Collections.IEnumerable"/>
        /// 枚举——页面集合（LazyPageList）继承 Scriban 的 ScriptObject，而
        /// <c>IEnumerable.GetEnumerator()</c> 会落到 ScriptObject 的**成员枚举器**
        /// （数出 56 个成员而不是 2 个页面，实测）。故按"页面序列接口优先"取数
        /// </summary>
        private static int CountPages(object? value) => value switch
        {
            // **页面集合接口必须先判**：LazyPageList 继承 ScriptObject，而 ScriptObject
            // 自身实现 System.Collections.ICollection（Count = **成员数**，实测 56）——
            // 先判 ICollection 会拿到成员数而不是页面数
            IList<ScriptObject> list => list.Count,
            IEnumerable<ScriptObject> sequence => sequence.Count(),
            System.Collections.ICollection collection when value is not ScriptObject => collection.Count,
            System.Collections.IEnumerable other when value is not ScriptObject => other.Cast<object?>().Count(),
            _ => 0
        };

        /// <summary>页面序列枚举（同上：优先泛型接口，避免 ScriptObject 的成员枚举器）</summary>
        private static IEnumerable<object?> EnumeratePages(object? value) => value switch
        {
            IEnumerable<ScriptObject> sequence => sequence.Cast<object?>(),
            System.Collections.IEnumerable other when value is not ScriptObject => other.Cast<object?>(),
            _ => []
        };

        private static List<PaginatorGroupBoundary>? ResolveGroupBoundaries(object? value)
        {
            if (value is not System.Collections.IEnumerable sequence || value is string)
            {
                return null;
            }

            var boundaries = new List<PaginatorGroupBoundary>();
            foreach (var item in sequence)
            {
                if (item is not ScriptObject group ||
                    !group.TryGetValue(null, default, "key", out var key) ||
                    !group.TryGetValue(null, default, "pages", out var pages) ||
                    pages is not System.Collections.IEnumerable pageSequence ||
                    pages is string)
                {
                    return null;
                }

                boundaries.Add(new PaginatorGroupBoundary(
                    key?.ToString() ?? "", CountPages(pageSequence)));
            }

            return boundaries;
        }

        /// <summary>分组集合 → 扁平页面序列（按组顺序，组内保持原序）</summary>
        private static List<FlintPageContext> FlattenGroupedPages(object? value)
        {
            var result = new List<FlintPageContext>();
            if (value is not System.Collections.IEnumerable sequence || value is string)
            {
                return result;
            }

            foreach (var item in sequence)
            {
                if (item is not ScriptObject group ||
                    !group.TryGetValue(null, default, "pages", out var pages) ||
                    pages is not System.Collections.IEnumerable pageSequence ||
                    pages is string)
                {
                    continue;
                }

                foreach (var page in EnumeratePages(pageSequence))
                {
                    switch (page)
                    {
                        case LazyPageObject lazy:
                            result.Add(lazy.PageContext);
                            break;
                        case FlintPageContext context:
                            result.Add(context);
                            break;
                    }
                }
            }

            return result;
        }

    internal static ScriptObject CreatePageObject(
        FlintPageContext page,
        IReadOnlyList<FlintPageContext>? siteRegularPages = null,
        int paginateSize = 0,
        string paginatePath = "page",
        IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>>? siteTaxonomies = null,
        IReadOnlyList<FlintPageContext>? siteAllPages = null)
    {
        return SharedPageObjects.GetValue(
            page, p => new LazyPageObject(
                p, siteRegularPages, paginateSize, paginatePath, siteTaxonomies, siteAllPages));
    }

    /// <summary>同上但返回具体类型（partialValue 需访问 LazyPageObject.Store）</summary>
    internal static LazyPageObject CreatePageObjectTyped(FlintPageContext page)
    {
        return SharedPageObjects.GetValue(page, static p => new LazyPageObject(p));
    }

    /// <summary>
    /// 把筛选结果还原成**带页面集合方法族**的对象；返回 null 表示"不是页面序列"，
    /// 调用方改用普通列表。
    /// </summary>
    /// <remarks>
    /// Hugo 里页面集合的筛选结果仍是页面集合，主题会在其上继续调用
    /// （yinyang 的 <c>(where .Data.Pages "Type" "in" …).GroupByDate "2006"</c> 实测
    /// 失败 22 处）。判据分两种：
    /// <list type="bullet">
    /// <item>结果非空：元素**全部**是内容页对象 → 按这些页面重建集合</item>
    /// <item>结果为空：源集合的元素全是页面/词条页对象（或源本身就是页面集合）→
    /// 返回**空页面集合**。Hugo 实测（v0.166，主题 yinyang 的 /tags/ 页）：
    /// <c>.Data.Pages</c> 是词条页集合，按 Type 过滤后为空，随后 <c>.GroupByDate</c>
    /// 返回空而不报错——空集合仍带方法族</item>
    /// </list>
    /// 元素不是页面对象时（如对字符串列表做筛选）返回 null，维持普通列表语义，
    /// 不把方法族扩到任意集合上。
    /// </remarks>
    internal static object? RewrapPageSequence(object? source, IReadOnlyList<object?> items)
    {
        if (items.Count > 0)
        {
            var pages = new List<FlintPageContext>(items.Count);
            foreach (var item in items)
            {
                if (item is LazyPageObject page)
                {
                    pages.Add(page.PageContext);
                }
                else
                {
                    return null;
                }
            }

            return GetSharedPageList(pages);
        }

        // 空结果：看源集合的元素种类（源为空时退回"源本身是页面集合"）
        if (source is LazyPageList)
        {
            return GetSharedPageList([]);
        }

        var sawPageLike = false;
        if (source is System.Collections.IEnumerable sourceSeq and not string)
        {
            foreach (var item in sourceSeq)
            {
                if (item is LazyPageObject or LazyTermPage)
                {
                    sawPageLike = true;
                    continue;
                }
                return null;
            }
        }

        return sawPageLike ? GetSharedPageList([]) : null;
    }

    /// <summary>
    /// 惰性页面对象：常规键构造时直接绑定；高成本的 prev/next 递归页对象延迟到
    /// 模板实际访问时构建（默认主题不访问 prev/next——万页构建可省 2×N 次全键
    /// 构建），并经 SharedPageObjects 复用。TryGetValue 只读不回写（并发渲染下
    /// ScriptObject 的写入非线程安全），全部可变状态在构造期完成
    /// </summary>
    internal sealed class LazyPageObject : ScriptObject
    {
        private readonly FlintPageContext _page;

        /// <summary>站点常规页集合（Hugo 的 .RegularPages 在任何页面都可用）</summary>
        private readonly IReadOnlyList<FlintPageContext>? _siteRegularPages;
        private readonly object? _pagesValue;
        private readonly object? _termsValue;
        private readonly object? _paginatorValue;

        /// <summary>页面级 Store（partial 返回值的传递通道，供 partialValue 读取）</summary>
        private PageStoreObject? _store;

        /// <summary>页面级 Store 实例（partialValue 机制用；构造后非 null）</summary>
        internal PageStoreObject? Store => _store;

        private readonly int _paginateSize;
        private readonly string _paginatePath;

        /// <summary>站点分类表（.GetTerms 需按当前页过滤词条）</summary>
        private readonly IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>>? _siteTaxonomies;

        /// <summary>
        /// 站点**全部**页面（含 section/taxonomy 页）——`.GetPage` 需要，
        /// 常规页集合里没有 section（hugo-book 的 `menu-section` 用
        /// `.GetPage "docs"` 取章节，缺它时报 "Section 'docs' not found"）
        /// </summary>
        private readonly IReadOnlyList<FlintPageContext>? _siteAllPages;

        /// <summary>
        /// .Ancestors 惰性缓存。**必须惰性**：页面对象按引用跨渲染共享（CWT），
        /// 而"某个页面第一次被创建"的时机不定——列表迭代里首次取到某页时，
        /// LazyPageList 只拿得到 PageContext，构造参数里没有站点页集，
        /// 构造期算祖先会得到空链（测试 `词条页祖先含分类列表页` 实测：
        /// term 页的祖先 tax 页被提前构造 → 轮到它自己渲染时祖先为空）。
        /// 改为访问时读取：先取构造参数，再退到本次构建登记的全站页集
        /// </summary>
        private object? _ancestorsValue;

        /// <summary>
        /// 容器祖先链缓存（最近祖先在前、home 在末位；见 <c>BuildContainerChain</c>）。
        /// .Ancestors/.Parent/.CurrentSection/.FirstSection 共用同一条链
        /// </summary>
        private List<FlintPageContext>? _containerChain;

        private object? _parentValue;
        private object? _currentSectionValue;
        private object? _firstSectionValue;
        private bool _parentResolved;
        private bool _currentSectionResolved;
        private bool _firstSectionResolved;

        public LazyPageObject(
            FlintPageContext page,
            IReadOnlyList<FlintPageContext>? siteRegularPages = null,
            int paginateSize = 0,
            string paginatePath = "page",
            IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>>? siteTaxonomies = null,
            IReadOnlyList<FlintPageContext>? siteAllPages = null)
        {
            _page = page;
            _siteAllPages = siteAllPages;
            _siteRegularPages = siteRegularPages;
            _paginateSize = paginateSize;
            _paginatePath = paginatePath;
            _siteTaxonomies = siteTaxonomies;
            // .Pages 同理：Hugo 恒为切片（普通内容页上是空切片）——传 null 会让
            // `union .Pages …` 之类的组合退化成"回落到本页 Pages"
            _pagesValue = GetSharedPageList(page.Pages ?? []);
            _termsValue = page.Terms is not null
                ? page.Terms.Select(t => (object)new LazyTaxonomyTerm(t)).ToList()
                : null;
            // C1：列表页的 pager 渲染实例携带本页分页器（page.paginator，对齐 Hugo）
            _paginatorValue = page.Paginator is not null
                ? BuildPaginatorObject(page.Paginator)
                : null;

            SetValue("title", page.Title, false);
            SetValue("content", page.Content, false);
            SetValue("output_format", page.OutputFormat, false);
            SetValue("permalink", page.Permalink, false);
            SetValue("rel_permalink", page.RelPermalink, false);
            SetValue("date", page.Date, false);
            SetValue("lastmod", page.LastMod, false);
            SetValue("tags", page.Tags, false);
            SetValue("categories", page.Categories, false);
            SetValue("word_count", page.WordCount, false);
            SetValue("reading_time", ReadingMinutes(page.ReadingTime), false);
            SetValue("description", page.Description, false);
            SetValue("summary", page.Summary, false);
            SetValue("type", page.Type, false);
            SetValue("layout", page.Layout, false);
            SetValue("draft", page.Draft, false);
            SetValue("weight", page.Weight, false);
            // .Params：Hugo 语义是「front matter 全量并入 + 自定义参数」，
            // 故 .Params.Title / .Params.Date 也可用（Ananke 用 .Params.Title 取标题，
            // 缺此兼容时 baseof 的 <title> 退化为站点名——差分验证实测发现）
            SetValue("params", BuildParamsObject(BuildParamsDict(page)), false);
            // .Resources：包装为带方法的集合（Hugo 的 .Resources.ByType/GetMatch/Match
            // 是 method 调用；裸列表无这些方法，主题会报 "function ... not found"）
            SetValue("resources", new PageResourcesObject(page.Resources), false);
            SetValue("pages", _pagesValue, false);
            // .Sections：本页直属的 section 子页集合（Hugo 语义，树阶段装配）。
            // 与 pages 一样包装成带方法族的集合——主题直接调 .ByWeight / .ByTitle
            //（techdoc 的 open-menu 用 site.home.sections.by_weight 建导航，
            //  缺此成员时 72 处 "Cannot get the member ... for a null object"）
            // .Sections：Hugo 语义恒为切片（无子 section 时是空切片，不是 nil）——
            // 主题常写 `union .RegularPages .Sections`（hugo-paper 的 list.html），
            // 传 null 会让 union 退化成"回落到本页 Pages"（在 /tags/ 上多出词条页 →
            // 多产出 /tags/page/2/）。空集合同样满足 `{{ with .Sections }}` 的假值语义
            SetValue("sections", GetSharedPageList(page.Sections ?? []), false);
            SetValue("terms", _termsValue, false);
            SetValue("section", page.Section, false);
            SetValue("table_of_contents", page.TableOfContents, false);
            SetValue("plain", page.Plain, false);
            // .Markup FORMAT（Hugo v0.146+ 的内容渲染作用域）：返回对象带
            // `.Render.Summary.Text` / `.Render.Content` 等，主题在"摘要按输出格式渲染"
            // 场景使用（FixIt 的 summary.html：`with .Markup "home"` → `with .Render`
            // → `dict "Content" .Summary.Text`）。缺此成员时报
            // "The function `page?.markup` was not found"
            SetValue("markup", new PageMarkupFunction(page), false);
            SetValue("Markup", new PageMarkupFunction(page), false);
            SetValue("raw_content", page.RawContent, false);
            // B4：Hugo .File.* 方法族（主题常用 .File.Path / .File.ContentBaseName）
            SetValue("file", BuildFileObject(page.SourcePath), false);

            // taxonomy 页数据对象（对齐 Hugo .Data）：Singular/Plural/Terms/Pages。
            // Ananke 的 terms.html 迭代 page.Data.pages 枚举词条（词条对象含
            // title/rel_permalink/pages），taxonomy.html 用 page.pages 枚举内容页——
            // 缺此对象时 terms.html 报 "Cannot get the member page.Data.pages for a null object"
            // Hugo 语义：.Data 在任何页面都存在（普通页为空 map，taxonomy/term 页含
            // Singular/Plural/Terms/Pages）。此前非分类页不注册，导致主题的
            // `.Data.Integrity` 等链式访问报 "Cannot get the member ... for a null object"
            var pageData = BuildPageDataObject(page) ?? new ScriptObject();
            AddListPageDataPages(page, pageData);
            SetValue("data", pageData, false);
            SetValue("Data", pageData, false);

            // ---- Hugo Page 派生键投影（D 组）----
            // kind 判定谓词（主题高频：{{ if .IsPage }} / {{ if .IsHome }}）
            var kind = page.Kind.ToLowerInvariant();
            var isHome = kind == "home";
            var isPage = kind == "page";
            var isSection = kind == "section";
            var isNode = isHome || isSection || kind is "taxonomy" or "term";
            SetValue("is_home", isHome, false);
            SetValue("IsHome", isHome, false);
            SetValue("is_page", isPage, false);
            SetValue("IsPage", isPage, false);
            SetValue("is_section", isSection, false);
            SetValue("IsSection", isSection, false);
            SetValue("is_node", isNode, false);
            SetValue("IsNode", isNode, false);
            SetValue("is_branch", isNode, false);
            SetValue("IsBranch", isNode, false);
            SetValue("kind", kind, false);
            SetValue("Kind", kind, false);

            // 元数据投影（缺省回退，避免主题直接取用时 null 报错）
            SetValue("link_title", page.LinkTitle ?? page.Title, false);
            SetValue("LinkTitle", page.LinkTitle ?? page.Title, false);
            SetValue("truncated", page.Truncated, false);
            SetValue("Truncated", page.Truncated, false);
            SetValue("path", page.PagePath ?? "/", false);
            // ---- 矩阵验证暴露的缺失属性（多主题共性）----
            // .Level：页面在树中的深度（home=0，/posts/=1，/posts/x/=2）
            var level = (page.Section ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).Length
                        + (isPage ? 1 : 0);
            SetValue("level", level, false);
            SetValue("Level", level, false);

            // .Language：语言对象（主题用 .Language.LanguageDirection 判断 rtl）
            var langObj = new ScriptObject
            {
                ["lang"] = page.Language ?? "",
                ["Lang"] = page.Language ?? "",
                ["language_code"] = page.Language ?? "",
                ["LanguageCode"] = page.Language ?? "",
                ["language_name"] = page.Language ?? "",
                ["LanguageName"] = page.Language ?? "",
                ["locale"] = page.Language ?? "",
                ["Locale"] = page.Language ?? "",
                // RTL 判断：主题读 language_direction；未知时给 ltr（安全默认）
                ["language_direction"] = "ltr",
                ["LanguageDirection"] = "ltr"
            };
            SetValue("language", langObj, false);
            SetValue("Language", langObj, false);
            SetValue("Path", page.PagePath ?? "/", false);
            SetValue("bundle_type", page.BundleType ?? "", false);
            SetValue("BundleType", page.BundleType ?? "", false);
            SetValue("keywords", page.Params.TryGetValue("keywords", out var kw) ? kw : new ScriptArray(), false);
            SetValue("Keywords", page.Params.TryGetValue("keywords", out var kw2) ? kw2 : new ScriptArray(), false);
            SetValue("publish_date", page.PublishDate ?? page.Date, false);
            SetValue("PublishDate", page.PublishDate ?? page.Date, false);
            SetValue("expiry_date", page.ExpiryDate, false);
            SetValue("ExpiryDate", page.ExpiryDate, false);
            SetValue("aliases", page.Aliases, false);
            SetValue("Aliases", page.Aliases, false);

            // .OutputFormats：输出格式集合 + Get(NAME) 方法（Hugo 语义；
            // 主题用 `{{ with .OutputFormats.Get "RSS" }}` 判断/取链接）
            var outputFormats = BuildOutputFormatsObject(page);
            SetValue("output_formats", outputFormats, false);
            SetValue("OutputFormats", outputFormats, false);
            // .AlternativeOutputFormats：除当前 HTML 之外的输出格式
            //（hugo-book 的 html-head 用 range .AlternativeOutputFormats 发 RSS 链接）
            var altFormats = BuildAlternativeOutputFormatsObject(page);
            SetValue("alternative_output_formats", altFormats, false);
            SetValue("AlternativeOutputFormats", altFormats, false);

            // .Plain 的派生：词列表与模糊字数
            var plainWords = page.Plain is null
                ? new ScriptArray()
                : new ScriptArray(page.Plain
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => (object)w).ToArray());
            SetValue("plain_words", plainWords, false);
            SetValue("PlainWords", plainWords, false);
            SetValue("fuzzy_word_count", page.FuzzyWordCount, false);
            SetValue("FuzzyWordCount", page.FuzzyWordCount, false);

            // .Len（Hugo Page.Len：节点页为子页数，普通页为 0）
            var len = page.Pages?.Count ?? 0;
            SetValue("len", len, false);
            SetValue("Len", len, false);

            // .Scratch / .Store：页面级可变暂存（Hugo 语义，跨块/跨 partial 状态传递）。
            // 同时作为 partial 返回值的传递通道（partialValue 机制）——Store 是
            // 真实对象容器，跨 include 保真（无需序列化往返）
            var store = new PageStoreObject();
            _store = store;
            SetValue("store", store, false);
            SetValue("Store", store, false);
            SetValue("scratch", store, false);
            SetValue("Scratch", store, false);

            // .GetPage / .Paginate：Hugo 的**页面方法**（矩阵验证中 Ananke/Stack/
            // PaperMod/LoveIt 均命中——主题用 `.GetPage "section" .Section` 取 section 页，
            // 用 `.Paginate .Pages` 做分页）。引擎此前只在 site 对象上暴露 get_page
            var sitePages = _siteRegularPages ?? [];
            SetValue("get_page", new GetPageFunction(_siteAllPages ?? sitePages), false);
            SetValue("GetPage", new GetPageFunction(_siteAllPages ?? sitePages), false);
            SetValue("paginate", new PagePaginateFunction(_page, _paginateSize, _paginatePath), false);
            SetValue("Paginate", new PagePaginateFunction(_page, _paginateSize, _paginatePath), false);

            // .GetTerms / .GetTerms "taxonomy"：Hugo 页面方法——返回**当前页所属**的
            // 该分类词条页（按站点分类表过滤 Pages 含本页的词条）。Stack/PaperMod
            // 用 `$Page.GetTerms "tags"` 取标签页列表（缺此方法报 function not found）
            var getTerms = new PageGetTermsFunction(_page, _siteTaxonomies);
            SetValue("get_terms", getTerms, false);
            SetValue("GetTerms", getTerms, false);

            // .IsAncestor PAGE / .IsDescendant PAGE（Hugo 页面方法）：
            // hugo-book 的 menu-filetree 用 `.Page.IsAncestor .CurrentPage` 决定
            // 侧边菜单是否展开；缺它时报 function not found
            var isAncestorFn = new PageRelationFunction(_page, isAncestor: true);
            SetValue("is_ancestor", isAncestorFn, false);
            SetValue("IsAncestor", isAncestorFn, false);
            var isDescendantFn = new PageRelationFunction(_page, isAncestor: false);
            SetValue("is_descendant", isDescendantFn, false);
            SetValue("IsDescendant", isDescendantFn, false);

            // .HasShortcode / .RenderString / .Param：Hugo 的页面方法族
            //（hugo-coder 42 处 `page?.has_shortcode`、archie `page.render_string`、
            //  fixit `page?.param` 实测——主题按方法调用，缺则整页渲染失败）
            var hasShortcode = new PageHasShortcodeFunction(_page);
            SetValue("has_shortcode", hasShortcode, false);
            SetValue("HasShortcode", hasShortcode, false);

            var renderString = new PageRenderStringFunction();
            SetValue("render_string", renderString, false);
            SetValue("RenderString", renderString, false);

            var pageParam = new PageParamFunction(_page);
            SetValue("param", pageParam, false);
            SetValue("Param", pageParam, false);

            var fragments = new PageFragmentsObject(page.Headings);
            fragments.Populate();
            SetValue("fragments", fragments, false);
            SetValue("Fragments", fragments, false);
            // Hugo 兼容别名（大写开头）
            SetValue("Title", page.Title, false);
            SetValue("Content", page.Content, false);
            SetValue("Permalink", page.Permalink, false);
            SetValue("RelPermalink", page.RelPermalink, false);
            SetValue("Date", page.Date, false);
            SetValue("Lastmod", page.LastMod, false);
            SetValue("Tags", page.Tags, false);
            SetValue("Categories", page.Categories, false);
            SetValue("WordCount", page.WordCount, false);
            SetValue("ReadingTime", ReadingMinutes(page.ReadingTime), false);
            SetValue("Description", page.Description, false);
            SetValue("Summary", page.Summary, false);
            SetValue("Type", page.Type, false);
            SetValue("Layout", page.Layout, false);
            SetValue("Draft", page.Draft, false);
            SetValue("Weight", page.Weight, false);
            SetValue("Params", page.Params, false);
            SetValue("Resources", page.Resources, false);
            SetValue("Pages", _pagesValue, false);
            SetValue("Terms", _termsValue, false);
            SetValue("Section", page.Section, false);
            SetValue("TableOfContents", page.TableOfContents, false);
            SetValue("Plain", page.Plain, false);
            SetValue("RawContent", page.RawContent, false);
        }

        /// <summary>
        /// 容器祖先链（最近祖先在前、home 在末位）。四种成员共用：
        /// <list type="bullet">
        /// <item><c>.Ancestors</c> = 整条链（页面集合，可 <c>.Reverse</c>）</item>
        /// <item><c>.Parent</c> = 链首（home 页无父 → null）</item>
        /// <item><c>.CurrentSection</c> = 见 <see cref="ResolveCurrentSection"/></item>
        /// <item><c>.FirstSection</c> = 见 <see cref="ResolveFirstSection"/></item>
        /// </list>
        /// **只收真实容器页**：section 与 taxonomy 列表页（RelPermalink 为本页前缀，
        /// 自身除外）。按路径段拼接会把"不产页的合成目录"（无 _index.md 的嵌套目录）
        /// 与 pager 段当成祖先，链上就出现不存在的页面——narrow 的面包屑
        /// （<c>/docs/guide/</c>、<c>/posts/page/</c> 两条死链）即此成因。
        /// <para>
        /// 探针实测（Hugo v0.166，站点 = home + docs（有 _index.md）+ docs/guide（有）
        /// + docs/noindex（无）+ /posts/ + /tags/ + /tags/x/）：
        /// </para>
        /// <list type="table">
        /// <item><term><c>/docs/guide/deep/</c></term><description>[指南区, 文档区, home]</description></item>
        /// <item><term><c>/docs/noindex/deep2/</c></term><description>[文档区, home]（中间那层不产页，不入链）</description></item>
        /// <item><term><c>/tags/x/</c></term><description>[Tags, home]</description></item>
        /// <item><term><c>/tags/</c>、<c>/posts/</c></term><description>[home]</description></item>
        /// <item><term>home</term><description>空</description></item>
        /// </list>
        /// </summary>
        private List<FlintPageContext> ContainerChain()
        {
            if (_containerChain is not null)
            {
                return _containerChain;
            }

            var chain = new List<FlintPageContext>();
            var selfRel = _page.RelPermalink ?? "/";
            var allPages = _siteAllPages ?? ScribanTemplateRenderer.CurrentSitePages;
            if (allPages is not null &&
                !string.Equals(_page.Kind, "home", StringComparison.OrdinalIgnoreCase))
            {
                chain.AddRange(allPages
                    .Where(p => (p.Kind == "section" || p.Kind == "taxonomy") &&
                                !string.IsNullOrEmpty(p.RelPermalink) &&
                                !string.Equals(p.RelPermalink, selfRel, StringComparison.OrdinalIgnoreCase) &&
                                selfRel.StartsWith(p.RelPermalink, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.RelPermalink!.Length));
                if (allPages.FirstOrDefault(
                        p => string.Equals(p.Kind, "home", StringComparison.OrdinalIgnoreCase))
                    is { } home)
                {
                    chain.Add(home);
                }
            }

            return _containerChain = chain;
        }

        /// <summary>
        /// .CurrentSection：本页所属的 section 页——**自身即容器页时是自己**
        /// （section/taxonomy/term/home 四类都如此，探针实测：<c>/docs/</c> → 自身、
        /// <c>/tags/x/</c> → 自身、home → 自身）。
        /// 内容页取链上**最近的 section**；根级页（如 <c>/p/</c>）与"仅有无 _index.md
        /// 的嵌套目录"下的页（如 <c>/docs/noindex/deep2/</c>）落到 **home**
        /// （探针实测：两者的 .CurrentSection 都是 home / 顶层 section，不是那层目录）
        /// </summary>
        private object? ResolveCurrentSection()
        {
            var kind = _page.Kind ?? "page";
            if (kind is "home" or "section" or "taxonomy" or "term")
            {
                return this;
            }

            var chain = ContainerChain();
            var nearestSection = chain.FirstOrDefault(
                p => string.Equals(p.Kind, "section", StringComparison.OrdinalIgnoreCase));
            if (nearestSection is not null)
            {
                return CreatePageObject(nearestSection);
            }

            var home = chain.LastOrDefault(
                p => string.Equals(p.Kind, "home", StringComparison.OrdinalIgnoreCase));
            return home is not null ? CreatePageObject(home) : this;
        }

        /// <summary>
        /// .NextInSection/.PrevInSection 的取值：在所属 section 的子页列表
        /// （SiteBuilder 按 Hugo 默认序排列：权重升、日期降）上取相邻页——
        /// 列表降序 ⇒ <paramref name="newer"/>（NextInSection）取 idx-1、
        /// PrevInSection 取 idx+1；边界（最新/最旧）为 nil（探针实测）
        /// </summary>
        private object? ResolveInSectionNeighbour(bool newer)
        {
            var kind = _page.Kind ?? "page";
            Flint.Core.Abstractions.PageContext? sectionContext;
            if (kind is "home" or "section" or "taxonomy" or "term")
            {
                sectionContext = _page;
            }
            else
            {
                var chain = ContainerChain();
                sectionContext = chain.FirstOrDefault(
                    p => string.Equals(p.Kind, "section", StringComparison.OrdinalIgnoreCase))
                    ?? chain.LastOrDefault(
                    p => string.Equals(p.Kind, "home", StringComparison.OrdinalIgnoreCase));
            }

            var pages = sectionContext?.Pages;
            if (pages is null || pages.Count == 0)
            {
                return null;
            }

            var selfIndex = -1;
            for (var i = 0; i < pages.Count; i++)
            {
                if (ReferenceEquals(pages[i], _page))
                {
                    selfIndex = i;
                    break;
                }
            }
            if (selfIndex < 0)
            {
                return null;
            }

            var neighbourIndex = newer ? selfIndex - 1 : selfIndex + 1;
            return neighbourIndex >= 0 && neighbourIndex < pages.Count
                ? CreatePageObject(pages[neighbourIndex])
                : null;
        }

        /// <summary>
        /// .FirstSection：本页所在的**顶层** section——探针实测（v0.166）：
        /// home → 自身；<c>/docs/</c>（一级 section）→ 自身；<c>/docs/guide/</c> →
        /// <c>/docs/</c>（最外层的 section，不是自己）；内容页 → 最外层 section；
        /// term 页 <c>/tags/x/</c> → <c>/tags/</c>（分类列表页，故 taxonomy 也计入）；
        /// 根级页 / 无 section 可归的页 → home
        /// </summary>
        private object? ResolveFirstSection()
        {
            var kind = _page.Kind ?? "page";
            if (kind is "home" or "taxonomy")
            {
                return this;
            }

            var chain = ContainerChain();
            // 链是"最近在前"，故**末位**的 section/taxonomy 即最外层容器
            var outermost = chain.LastOrDefault(
                p => p.Kind is "section" or "taxonomy");
            if (outermost is not null)
            {
                return CreatePageObject(outermost);
            }

            if (string.Equals(kind, "term", StringComparison.OrdinalIgnoreCase))
            {
                return this;
            }

            if (string.Equals(kind, "section", StringComparison.OrdinalIgnoreCase))
            {
                return this;
            }

            var home = chain.LastOrDefault(
                p => string.Equals(p.Kind, "home", StringComparison.OrdinalIgnoreCase));
            return home is not null ? CreatePageObject(home) : this;
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            // .Paginator：**读取即视为"本页被模板分页"**（Hugo 语义：分页在首次访问
            // `.Paginate` 或 `.Paginator` 时惰性建立，随后才产出 /page/N/）。
            // Ananke 用 `.Paginator.Pages` 而非 `.Paginate`，只在 .Paginate 上打标记
            // 会漏掉这类主题（实测：Hugo 产出 /posts/page/2/ 而 Flint 不产）。
            // 故从成员字典里摘出来，统一走这里以便记录访问
            if (member is "paginator" or "Paginator")
            {
                ScribanTemplateRenderer.NotePaginateInvoked(_page.RelPermalink);
                // 模板自己调过 `.Paginate` 时，Hugo 的 `.Paginator` 返回**那一次创建**的
                // 分页器（`.Paginate` 会改写该页的 paginator）；只有模板没调过才用
                // 构建期预绑定的隐式分页器。两者集合可能不同（stack 的 home：模板传空集
                // → 1 页；隐式 = 站点常规页 → 3 页），混用会渲染出指向未产出页的链接
                var registered = ScribanTemplateRenderer.GetPaginatePager(_page.RelPermalink);
                value = registered is not null ? BuildPaginatorObject(registered) : _paginatorValue;
                return true;
            }

            // .Ancestors：访问时才解析（页面对象可能先由页面集合迭代创建，那时没有站点页集）
            if (member is "ancestors" or "Ancestors")
            {
                value = _ancestorsValue ??= GetSharedPageList(ContainerChain());
                return true;
            }

            // .Parent / .CurrentSection / .FirstSection：同一棵树上的三种投影（均为**真实页面对象**，
            // 主题会继续取 .Title/.RegularPages/.GetPage 等）。判据见 BuildContainerChain 的说明
            if (member is "parent" or "Parent")
            {
                if (!_parentResolved)
                {
                    _parentValue = ContainerChain() is [var nearest, ..] ? CreatePageObject(nearest) : null;
                    _parentResolved = true;
                }

                value = _parentValue;
                return true;
            }
            if (member is "current_section" or "CurrentSection")
            {
                if (!_currentSectionResolved)
                {
                    _currentSectionValue = ResolveCurrentSection();
                    _currentSectionResolved = true;
                }

                value = _currentSectionValue;
                return true;
            }
            if (member is "first_section" or "FirstSection")
            {
                if (!_firstSectionResolved)
                {
                    _firstSectionValue = ResolveFirstSection();
                    _firstSectionResolved = true;
                }

                value = _firstSectionValue;
                return true;
            }

            // .Site：Hugo 的页面成员（`.Site.Params…`）；迁移产物里 `$page.Site.X` 落到
            // `$page.site.x`（stack 的 `$Page.Site.Params.dateFormat.published` 实测）
            if (member is "site" or "Site")
            {
                value = ScribanTemplateRenderer.CurrentSiteObject();
                return true;
            }

            // prev/next 懒构建：CWT 复用（相邻页面对象全构建内共享），只读不回写
            if (member is "prev_page" or "PrevPage")
            {
                value = _page.PrevPage is not null ? CreatePageObject(_page.PrevPage) : null;
                return true;
            }
            if (member is "next_page" or "NextPage")
            {
                value = _page.NextPage is not null ? CreatePageObject(_page.NextPage) : null;
                return true;
            }

            // .NextInSection / .PrevInSection：Hugo 探针（v0.166，section 内 a<b<c<d）——
            // NextInSection = **更新的页**、PrevInSection = **更旧的页**，边界为 nil。
            // blowfish 的 article-pagination 卡片（含日期）靠这两个成员渲染
            if (member is "next_in_section" or "NextInSection")
            {
                value = ResolveInSectionNeighbour(newer: true);
                return true;
            }
            if (member is "prev_in_section" or "PrevInSection")
            {
                value = ResolveInSectionNeighbour(newer: false);
                return true;
            }

            // .RegularPages：Hugo 在任何页面都提供（single 页为站点级常规页集合，
            // 主题用 `{{ .RegularPages.Related . }}` 做相关推荐——Ananke 实证）
            if (member is "regular_pages" or "RegularPages")
            {
                // 分类页（taxonomy/term）：Hugo v0.166 实测 `.RegularPages` = **空**——
                // 词条页不算"常规子页"（hugo-paper 的 list.html 写
                // `$pages := union .RegularPages .Sections`，在 /tags/ 上应为空集合 →
                // `.Paginate []` = 1 页；此前返回词条页集合 → 多产出 /tags/page/2/）
                if (_page.Kind is "taxonomy" or "term")
                {
                    value = GetSharedPageList([]);
                    return true;
                }
                if (_page.Pages is not null)
                {
                    // 节点页：自身子页集合（Hugo 语义优先）
                    value = GetSharedPageList(_page.Pages);
                    return true;
                }
                if (_siteRegularPages is not null)
                {
                    value = GetSharedPageList(_siteRegularPages);
                    return true;
                }
                value = new LazyPageList(Array.Empty<FlintPageContext>());
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }

        /// <summary>
        /// 包装的页面上下文（C4 视图接收者）：render "view" &lt;page&gt; 需要从
        /// 模板传入的页面对象反查真实 PageContext 作为渲染上下文
        /// </summary>
        internal FlintPageContext PageContext => _page;
    }

    /// <summary>
    /// 阅读时间投影：Hugo 的 <c>.ReadingTime</c> 是**分钟数整数**（向上取整），
    /// 而内部类型是 <see cref="TimeSpan"/>——主题会做算术与比较
    /// （`reading_time != 0`、`add reading_time 1`），直接暴露 TimeSpan 会报
    /// "Unable to convert type `TimeSpan` to int"（Blowfish 实测 1571 处）
    /// </summary>
    private static int ReadingMinutes(TimeSpan readingTime) =>
        (int)Math.Ceiling(readingTime.TotalMinutes);

    /// <summary>
    /// 绝对 URL（或已是相对路径）→ 相对路径（对齐 Hugo <c>.RelPermalink</c>）。
    /// 用 <see cref="Uri.PathAndQuery"/> 取路径部分，不依赖站点 baseURL 拼接形态；
    /// 解析失败时原样返回（宽容，不因脏数据中断渲染）
    /// </summary>
    private static string ToRelPermalink(string? permalink)
    {
        if (string.IsNullOrEmpty(permalink))
        {
            return "/";
        }

        if (permalink.StartsWith('/'))
        {
            return permalink;
        }

        return Uri.TryCreate(permalink, UriKind.Absolute, out var uri)
            ? uri.PathAndQuery
            : permalink;
    }

    /// <summary>
    /// .Paginate（Hugo 页面方法）：对给定集合建分页器。
    /// 主题用 `{{ $pag := .Paginate .Pages }}` 显式分页（Stack/PaperMod/LoveIt 实测）；
    /// 返回首个 pager（Hugo 语义：.Paginate 返回当前页的 pager）
    /// </summary>
    internal sealed class PagePaginateFunction(
        FlintPageContext page, int pageSize, string paginatePath)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            // 记录"本列表页被模板分页"——站点渲染据此决定是否产 /page/N/
            //（Hugo 语义：分页页由模板调用驱动，不是站点级预生成）
            ScribanTemplateRenderer.NotePaginateInvoked(page.RelPermalink);

            // **显式集合**：Hugo 的 `.Paginate $pages` 按传入集合分页（页数、每页内容
            // 都以它为准）。旧实现丢弃实参、恒按当前页 Pages → 主题传
            // site.RegularPages 时页数与列表内容都不是 Hugo 的样子
            //（papermod/m10c 的 home 用 `.Paginate $pages`，实测页数不一致）
            //
            // **空集合也算显式集合**：blog-awesome 的列表页调用
            // `.Paginate (where .Pages "Section" "blog")`——过滤结果为空，Hugo 得到
            // "空分页器"（1 页、无 /page/2/），而"回落本页 Pages"会按 3 篇文章分页
            // → 多出 /page/2/（门禁④报不对称）
            IReadOnlyList<FlintPageContext> explicitItems = [];
            var hasExplicitCollection =
                arguments.Count > 0 && TryResolvePageCollection(arguments[0], out explicitItems);
            // **分组集合**（`.Paginate (.Pages.GroupByDate "2006")`）：Hugo 探针（v0.166，
            // 5 篇跨 2 年、pagerSize=2）——`TotalNumberOfElements` = 5（切的是**底层页面**）、
            // `TotalPages` = 3、第 1 页 `PageGroups` = [2025:2]、第 2 页 = [2025:1][2024:1]。
            // 故把分组摊平为页面参与分页，同时记住分组边界供 `page_groups` 重新切分
            var groupBoundaries = ResolveGroupBoundaries(arguments.Count > 0 ? arguments[0] : null);
            if (groupBoundaries is not null && groupBoundaries.Count > 0)
            {
                hasExplicitCollection = true;
                explicitItems = FlattenGroupedPages(arguments[0]);
            }

            var items = hasExplicitCollection ? explicitItems : page.Pages ?? [];

            // **显式页大小**：Hugo 的 `.Paginate $pages N` 第二参覆盖站点 pagerSize
            //（v0.166 实测：站点 pagerSize=2、3 篇文章时 `.Paginate $ps 6` → TotalPages=1，
            //  不带第二参 → 2）。loveit 的 home 就是 `$.Paginate $pages $posts.paginate`
            //（主题配置 params.home.posts.paginate = 6）——旧实现忽略第二参，让
            // 3 篇文章按 2 分页 → 多出 /page/2/（Hugo 无），门禁④报不对称
            var size = pageSize > 0 ? pageSize : 10;
            if (arguments.Count > 1 && TryExplicitSize(arguments[1]) is > 0 and var explicitSize)
            {
                size = explicitSize;
            }

            // 集合与**实际生效的页大小**一并登记：站点侧据此生成 /page/N/
            //（只记集合会让页数按站点配置算，与模板用的尺寸不一致；
            //  显式空集合也要登记，否则站点侧回落本页 Pages 又多出页）
            if (hasExplicitCollection || items.Count > 0)
            {
                ScribanTemplateRenderer.NotePaginateCollection(page.RelPermalink, items, size);
            }

            // **页码与基准 URL 取自当前页已绑定的 pager**：模板在**第 N 页**上再次调用
            // `.Paginate` 时，Hugo 返回的是"当前页的 pager"（`/posts/page/2/` 上
            // `.Paginator.Next` 指向第 3 页；末页为 nil）。
            // 此前恒按"第 1 页 + 本页 RelPermalink"重建 → 在 `/posts/page/2/` 上产出
            // `/posts/page/2/page/2/`（末页还多了个不存在的 Next 链接），
            // 实测 even / hugo-paper 的产物里出现 `/page/2/page/2/`
            var bound = page.Paginator;
            var baseRel = bound?.BaseRelPermalink
                ?? (string.IsNullOrEmpty(page.RelPermalink) ? "/" : page.RelPermalink);
            var currentPageNumber = bound?.PageNumber ?? 1;

            var pager = PaginatorView.Create(items, currentPageNumber, size, baseRel, paginatePath);
            if (groupBoundaries is not null && groupBoundaries.Count > 0)
            {
                pager = pager.WithGroupBoundaries(groupBoundaries);
            }

            // 登记：后续对 `page.paginator` 的读取要返回这一个（Hugo 语义）
            ScribanTemplateRenderer.NotePaginatePager(page.RelPermalink, pager);
            return BuildPaginatorObject(pager);
        }

        /// <summary>`.Paginate` 第二参（页大小）：接受数值与数字串，其它形态返回 null</summary>
        private static int? TryExplicitSize(object? argument) => argument switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            decimal m => (int)m,
            short s => s,
            byte b => b,
            string str when int.TryParse(str, out var parsed) => parsed,
            _ => null
        };

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 0;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        /// <summary>
        /// 把实参还原成页面集合（页面序列 / 页面对象列表）。**空序列算合法集合**
        /// （Hugo：`.Paginate <空集合>` = 空分页器，只 1 页）；序列里混入非页面元素
        /// 时返回 false（调用方回落到当前页 <c>Pages</c>）——普通字符串列表不该被
        /// 当成页面集合分页
        /// </summary>
        private static bool TryResolvePageCollection(
            object? argument, out IReadOnlyList<FlintPageContext> items)
        {
            switch (argument)
            {
                case FlintPageContext single:
                    items = [single];
                    return true;
                case System.Collections.IEnumerable seq and not string:
                    var pages = new List<FlintPageContext>();
                    foreach (var item in seq)
                    {
                        if (item is LazyPageObject lp)
                        {
                            pages.Add(lp.PageContext);
                        }
                        else
                        {
                            items = [];
                            return false;
                        }
                    }
                    items = pages;
                    return true;
                default:
                    items = [];
                    return false;
            }
        }

        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(object), "pages");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "paginator");
    }

    /// <summary>
    /// .HasShortcode（Hugo 页面方法）：内容是否含指定名字的短代码。
    /// 主题用它为含特定短代码的页面附加资源（如 mermaid/katex 的脚本）——
    /// 判定宽松（内容里出现 <c>{{&lt; name</c> 或 <c>{{% name</c> 即算命中），
    /// 与 Hugo 的"短代码名为路径最后一段"语义一致
    /// </summary>
    internal sealed class PageHasShortcodeFunction(FlintPageContext page)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return false;
            }
            var name = arguments[0]?.ToString() ?? "";
            if (name.Length == 0)
            {
                return false;
            }
            var raw = page.RawContent ?? "";
            if (raw.Length == 0)
            {
                return false;
            }
            // 短代码名可能是路径（"foo/bar"），Hugo 按末段匹配文件名
            var lastSeg = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
            foreach (var marker in new[] { "{{< ", "{{% ", "{{<" , "{{%" })
            {
                if (raw.Contains(marker + lastSeg, StringComparison.Ordinal) ||
                    raw.Contains(marker + name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 0;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(bool);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), "name");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(bool), "result");
    }

    /// <summary>
    /// .RenderString（Hugo 页面方法）：把字符串按 Markdown 渲染为 HTML。
    /// Hugo 的签名是 <c>RenderString STRING [OPTS]</c>（可选 display/type 选项）。
    /// Flint 侧用内容管线的 Markdown 渲染器（与正文渲染一致）
    /// </summary>
    internal sealed class PageRenderStringFunction()
        : Scriban.Runtime.IScriptCustomFunction
    {
        private static readonly Content.MarkdownParser Parser = new();

        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return "";
            }
            var text = arguments[0]?.ToString() ?? "";
            if (text.Length == 0)
            {
                return "";
            }
            try
            {
                return Parser.ToHtml(text);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 渲染失败返回原文（Hugo 对渲染错误同样不中断页面渲染）
                return text;
            }
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 2;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(string);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(index == 0 ? typeof(string) : typeof(object), index == 0 ? "text" : "options");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(string), "html");
    }

    /// <summary>
    /// .Fragments（Hugo v0.111+ 页面方法族）：文档标题的目录视图。
    /// 暴露 <c>ToHTML start end ordered</c> / <c>Identifiers</c> / <c>Headings</c>。
    /// FixIt 用 <c>.Fragments.ToHTML $start $end $ordered</c> 生成文章目录
    /// （实测 "The function `page?.fragments?.to_h_t_m_l` was not found" 使整页渲染失败）。
    /// 成员**即时注册**而非懒查找 override：ScriptObject 的成员查找有多条路径，
    /// 只 override TryGetValue 会让函数调用路径查不到成员而报 function not found
    /// </summary>
    internal sealed class PageFragmentsObject(IReadOnlyList<MarkdownHeading> headings)
        : ScriptObject, IFlintNonDataObject
    {
        public IReadOnlyList<MarkdownHeading> HeadingsSource { get; } = headings;

        /// <summary>注册全部成员（构造后由调用方显式调用，避免构造期虚调用）</summary>
        public void Populate()
        {
            var toHtml = new FragmentsToHtmlFunction(HeadingsSource);
            SetValue("to_html", toHtml, false);
            SetValue("toHTML", toHtml, false);
            SetValue("ToHTML", toHtml, false);
            // 转换器的 snake 归一化把 ToHTML 写成 to_h_t_m_l（每个大写都当词边界，
            // 见 fixit 的 `page?.fragments?.to_h_t_m_l`）——缺失时整页报 function not found
            SetValue("to_h_t_m_l", toHtml, false);

            var ids = new ScriptArray(HeadingsSource.Select(h => (object)h.Id));
            SetValue("identifiers", ids, false);
            SetValue("Identifiers", ids, false);

            var items = new ScriptArray(HeadingsSource.Select(h => (object)new ScriptObject
            {
                ["id"] = h.Id,
                ["Id"] = h.Id,
                ["level"] = h.Level,
                ["Level"] = h.Level,
                ["title"] = h.Text,
                ["Title"] = h.Text
            }));
            SetValue("headings", items, false);
            SetValue("Headings", items, false);
        }
    }

    /// <summary>
    /// .Fragments.ToHTML start end ordered：按层级区间渲染目录 HTML
    /// （Hugo 实测：区间外标题被裁掉、缺失层级补空 li 包裹；见 TocRenderer）
    /// </summary>
    internal sealed class FragmentsToHtmlFunction(IReadOnlyList<MarkdownHeading> headings)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            var start = arguments.Count > 0 ? BuiltinTemplateFunctions.ToInt(arguments[0]) : 2;
            var end = arguments.Count > 1 ? BuiltinTemplateFunctions.ToInt(arguments[1]) : 3;
            var ordered = arguments.Count > 2 && BuiltinTemplateFunctions.IsTruthy(arguments[2]);
            return Flint.Core.Content.TocRenderer.Render(headings, start, end, ordered);
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 0;
        public int ParameterCount => 3;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(string);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            index == 2
                ? new Scriban.Runtime.ScriptParameterInfo(typeof(bool), "ordered")
                : new Scriban.Runtime.ScriptParameterInfo(typeof(int), index == 0 ? "start" : "end");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(string), "html");
    }

    /// <summary>
    /// .Param（Hugo 页面方法）：按点路径取参数（页面 params → 站点 params 回落）。
    /// 与全局 paramLookup 同语义，登记为页面方法使 <c>page.Param "x"</c> 可直接调用
    /// </summary>
    internal sealed class PageParamFunction(FlintPageContext page)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return null;
            }
            var path = arguments[0]?.ToString() ?? "";
            if (path.Length == 0 || page.Params is null)
            {
                return null;
            }
            // 页面参数点路径查找（Hugo .Param 语义；站点回落由模板侧的 paramLookup 覆盖）
            return BuiltinTemplateFunctions.GetMember(page.Params, path);
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), "path");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "value");
    }

    /// <summary>
    /// .GetTerms（Hugo 页面方法）：返回当前页在指定分类下的词条页列表。
    /// 从站点分类表取出该分类全部词条，过滤出 Pages 含当前页者
    /// （Hugo 语义：页面的 .GetTerms "tags" 给该页用到的标签页）
    /// </summary>
    internal sealed class PageGetTermsFunction(
        FlintPageContext page,
        IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>>? siteTaxonomies)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            var result = new List<object>();
            if (siteTaxonomies is null || arguments.Count == 0)
            {
                return result;
            }

            var name = arguments[0]?.ToString() ?? "";
            if (name.Length == 0 || !siteTaxonomies.TryGetValue(name, out var terms))
            {
                return result;
            }

            foreach (var term in terms)
            {
                if (term.Pages.Any(p => ReferenceEquals(p, page) ||
                        string.Equals(p.SourcePath, page.SourcePath, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(new LazyTermPage(term));
                }
            }
            return result;
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 0;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), "taxonomy");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "terms");
    }

    /// <summary>
    /// .Site.GetPage / .Page.GetPage：按路径或 (kind, 名) 查页。
    /// 未命中返回 null（Hugo 语义；主题通常用 with 包裹）
    /// </summary>
    /// <summary>
    /// <c>.IsAncestor PAGE</c> / <c>.IsDescendant PAGE</c>（Hugo 页面方法）：
    /// 按 URL 段判定祖先/后代关系（home 是所有非 home 页面的祖先）
    /// </summary>
    internal sealed class PageRelationFunction(FlintPageContext page, bool isAncestor)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            var other = arguments.Count > 0 ? ToPageContext(arguments[0]) : null;
            if (other is null)
            {
                return false;
            }

            var self = page.RelPermalink.Trim('/');
            var target = other.RelPermalink.Trim('/');
            var (ancestor, descendant) = isAncestor ? (self, target) : (target, self);
            // home（路径为空）是所有非 home 页面的祖先
            if (ancestor.Length == 0)
            {
                return descendant.Length > 0;
            }
            return descendant.Length > ancestor.Length &&
                   descendant.StartsWith(ancestor + "/", StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind => Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(bool);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(object), "page");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(bool), "result");

        private static FlintPageContext? ToPageContext(object? value) => value switch
        {
            FlintPageContext p => p,
            LazyPageObject lp => lp.PageContext,
            _ => null
        };
    }

    internal sealed class GetPageFunction(
        IReadOnlyList<FlintPageContext> pages,
        string? languagePrefix = null)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return null;
            }

            var a0 = arguments[0]?.ToString() ?? "";
            var a1 = arguments.Count > 1 ? arguments[1]?.ToString() : null;
            // 检索集合优先用"当前构建的全量页面"（含 section/term）：页面对象的
            // 构造快照可能是常规页集合（列表渲染先行创建），只认快照会漏 section
            var searchPages = ScribanTemplateRenderer.CurrentSitePages ?? pages;

            FlintPageContext? found = null;
            if (a1 is not null)
            {
                // (kind, 名) 形态：section 按 Section 段匹配，page 按标题/slug 匹配
                found = a0.ToLowerInvariant() switch
                {
                    "section" or "sections" => searchPages.FirstOrDefault(p =>
                        p.Kind.Equals("section", StringComparison.OrdinalIgnoreCase) &&
                        (p.Section.Equals(a1, StringComparison.OrdinalIgnoreCase) ||
                         p.RelPermalink.Trim('/').Equals(a1, StringComparison.OrdinalIgnoreCase))),
                    "home" => searchPages.FirstOrDefault(p => p.Kind.Equals("home", StringComparison.OrdinalIgnoreCase)),
                    "page" => searchPages.FirstOrDefault(p => p.Title.Equals(a1, StringComparison.OrdinalIgnoreCase)),
                    _ => null
                };
            }
            else
            {
                // 路径形态：归一后比对 RelPermalink；多语言站点再比一次"剥掉语言前缀"的形态——
                // Hugo 的 .Site.GetPage 按**当前语言的内容根**解析路径，主题写
                // `"/about"` 而页面 RelPermalink 是 `"/en/about/"`，只比全路径必然落空
                //（monochrome 的 states.html：`.Site.GetPage .Params.balloon_resources`
                // （值 "/about"）返回 null → "$res.resources for a null object"）
                var path = a0.Trim('/');
                found = searchPages.FirstOrDefault(p =>
                    p.RelPermalink.Trim('/').Equals(path, StringComparison.OrdinalIgnoreCase) ||
                    p.PagePath?.Trim('/').Equals(path, StringComparison.OrdinalIgnoreCase) == true ||
                    MatchesIgnoringLanguagePrefix(p, path) ||
                    (path.Length == 0 && p.Kind.Equals("home", StringComparison.OrdinalIgnoreCase)));
            }

            return found is null ? null : CreatePageObject(found);
        }

        /// <summary>
        /// 语言前缀无关的路径匹配：站点语言为 <paramref name="languagePrefix"/>（如 "en"）时，
        /// 页面 RelPermalink <c>"/en/about/"</c> 应能命中查询路径 <c>"about"</c>
        /// </summary>
        private bool MatchesIgnoringLanguagePrefix(FlintPageContext page, string path)
        {
            if (languagePrefix is not { Length: > 0 } lang || path.Length == 0)
            {
                return false;
            }
            var rel = page.RelPermalink.Trim('/');
            return rel.Length > lang.Length + 1 &&
                   rel.StartsWith(lang, StringComparison.OrdinalIgnoreCase) &&
                   rel[lang.Length] == '/' &&
                   rel[(lang.Length + 1)..].Equals(path, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 2;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), index == 0 ? "pathOrKind" : "name");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "page");
    }

    /// <summary>
    /// .OutputFormats 对象：格式列表 + Get(NAME) 方法。
    /// Hugo 的 `.OutputFormats.Get "RSS"` 返回格式对象（含 RelPermalink），
    /// 未命中返回 null（主题用 with 包裹）
    /// </summary>
    private static ScriptObject BuildOutputFormatsObject(FlintPageContext page)
    {
        // **kind 默认输出格式**（Hugo 探针）：home/section/taxonomy/term 未在
        // front matter 声明 `outputs` 时默认 **HTML + RSS**，普通页仅 HTML。
        // 此前列表页恒只有 html → `with .OutputFormats.Get "rss"` 取到 null →
        // fixit/section.html 的 RSS 订阅链接整段不渲染（实测）
        IReadOnlyList<string> formats = page.Outputs.Count > 0
            ? page.Outputs
            : page.Kind.ToLowerInvariant() switch
            {
                "home" or "section" or "taxonomy" or "term" => ["html", "rss"],
                _ => ["html"]
            };
        var arr = new ScriptArray();
        foreach (var name in formats)
        {
            arr.Add(BuildFormatObject(page, name));
        }

        var o = new ScriptObject();
        foreach (var item in arr)
        {
            o[(string)((ScriptObject)item!)["name"]!] = item;
        }

        o["get"] = new OutputFormatsGetFunction(arr);
        o["Get"] = o["get"];
        o["count"] = arr.Count;
        o["Count"] = arr.Count;

        // 使对象可迭代（for fmt in page.output_formats）
        foreach (var i in Enumerable.Range(0, arr.Count))
        {
            o[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = arr[i];
        }

        return o;
    }

    private static ScriptObject BuildFormatObject(FlintPageContext page, string name)
    {
        var suffix = name.ToLowerInvariant() switch
        {
            "rss" => "xml",
            "json" => "json",
            _ => "html"
        };
        // **分页页归一**：分页页（/posts/page/2/）的 RSS/JSON 地址指向**列表根**的
        // feed（/posts/index.xml），与 Hugo 一致——分页页本身不产出独立 RSS
        var baseRel = System.Text.RegularExpressions.Regex.Replace(
            page.RelPermalink ?? "/", @"/page/\d+/$", "/");
        var rel = name.Equals("html", StringComparison.OrdinalIgnoreCase)
            ? page.RelPermalink
            : baseRel.TrimEnd('/') + "/index." + suffix;
        return new ScriptObject
        {
            ["name"] = name, ["Name"] = name,
            // media_type 是**嵌套对象**（Hugo 的 OutputFormat.MediaType.Type 链——
            // console 的 baseof 取 `.MediaType.Type` 拼链接；此前给字符串 → `.type`
            // 取空 → 输出 type=""）
            ["media_type"] = new ScriptObject
            {
                ["type"] = name.ToLowerInvariant() switch
                {
                    "rss" => "application/rss+xml",
                    "json" => "application/json",
                    _ => "text/html"
                },
                ["Type"] = name.ToLowerInvariant() switch
                {
                    "rss" => "application/rss+xml",
                    "json" => "application/json",
                    _ => "text/html"
                }
            },
            ["rel_permalink"] = rel, ["RelPermalink"] = rel,
            // **permalink 对齐 Hugo**：RSS/JSON 等非 HTML 格式的 permalink 是
            // 自身的绝对地址（/posts/index.xml），不是 HTML 页地址
            // （fixit 的 RSS 订阅链接此前指向列表页本身，实测）
            ["permalink"] = page.Permalink.TrimEnd('/') + (rel == page.RelPermalink ? "" : "/index." + suffix),
            ["Permalink"] = page.Permalink,
            ["rel"] = "alternate", ["Rel"] = "alternate"
        };
    }

    /// <summary>
    /// .AlternativeOutputFormats：页面除当前 HTML 渲染之外的其他输出格式
    /// （Hugo 语义：`range .AlternativeOutputFormats` 用于在 head 里发
    /// RSS/JSON 自动发现链接——hugo-book 的 html-head.html 实测）
    /// </summary>
    private static ScriptArray BuildAlternativeOutputFormatsObject(FlintPageContext page)
    {
        IReadOnlyList<string> formats = page.Outputs.Count > 0
            ? page.Outputs
            : page.Kind.ToLowerInvariant() switch
            {
                "home" or "section" or "taxonomy" or "term" => ["html", "rss"],
                _ => ["html"]
            };
        var arr = new ScriptArray();
        foreach (var name in formats)
        {
            if (name.Equals("html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            arr.Add(BuildFormatObject(page, name));
        }

        return arr;
    }

    /// <summary>OutputFormats.Get(NAME)：按名取格式（未命中返回 null）</summary>
    private sealed class OutputFormatsGetFunction(ScriptArray formats)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            var want = arguments.Count > 0 ? arguments[0]?.ToString() ?? "" : "";
            foreach (var item in formats)
            {
                if (item is ScriptObject o &&
                    string.Equals(o["name"]?.ToString(), want, StringComparison.OrdinalIgnoreCase))
                {
                    return o;
                }
            }
            return null;
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), "name");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "format");
    }

    /// <summary>
    /// 构造 .Params 视图（Hugo 语义）：front matter 顶层字段并入自定义参数，
    /// 页面自身字段优先于同名自定义参数。键同时提供小写与首字母大写形态
    /// （主题两种写法都常见：<c>.Params.Title</c> / <c>.Params.title</c>）
    /// </summary>
    private static Dictionary<string, object> BuildParamsDict(FlintPageContext page)
    {
        var o = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // 1) 自定义参数（front matter 的 params 段 + 未被识别的顶层键）
        foreach (var kv in page.Params)
        {
            o[kv.Key] = kv.Value;
        }

        // 2) front matter 顶层字段（页面字段优先）
        void SetBoth(string name, object? value)
        {
            if (value is null)
            {
                return;
            }
            o[name] = value;
            o[name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name] = value;
        }

        SetBoth("title", page.Title);
        SetBoth("description", page.Description);
        // summary 只投影**显式 front matter**值（探针 v0.166：自动摘要不进 .Params，
        // narrow 首页 meta description、clarity 摘要钩子依赖该判定）
        SetBoth("summary", page.ExplicitSummary);
        SetBoth("date", page.Date);
        SetBoth("lastmod", page.LastMod);
        SetBoth("publishdate", page.PublishDate);
        SetBoth("expirydate", page.ExpiryDate);
        SetBoth("type", page.Type);
        SetBoth("layout", page.Layout);
        SetBoth("weight", page.Weight);
        SetBoth("draft", page.Draft);
        SetBoth("section", page.Section);
        SetBoth("kind", page.Kind);
        if (page.Tags.Count > 0)
        {
            SetBoth("tags", page.Tags);
        }
        if (page.Categories.Count > 0)
        {
            SetBoth("categories", page.Categories);
        }
        if (page.Aliases.Count > 0)
        {
            SetBoth("aliases", page.Aliases);
        }

        return o;
    }

    /// <summary>
    /// 列表页（home/section）的 <c>.Data.Pages</c>。
    /// </summary>
    /// <remarks>
    /// Hugo v0.166 实测：section 页的 <c>.Data.Pages</c> 与 <c>.Pages</c> 同源且非空
    /// （主题常写 <c>(where .Data.Pages "Type" "in" …).GroupByDate</c>，yinyang 的
    /// <c>_default/list.html</c> 用它渲染 section/多语言首页）。Flint 此前只给
    /// taxonomy/term 页建 <c>.Data</c>，其余页面是空 map → <c>.Data.Pages</c> 取空，
    /// 过滤结果退化成普通列表，随后的 <c>.GroupByDate</c> 报 "function not found"。
    /// 只补 <c>pages</c> 键：<c>terms</c>/<c>singular</c> 等仍是分类页专属，
    /// 免得把空 <c>Terms</c> 变成真值而翻转主题的 <c>{{ if .Data.Terms }}</c> 分支。
    /// 普通内容页（kind=page）不补——Hugo 那里没有子页集合
    /// </remarks>
    private static void AddListPageDataPages(FlintPageContext page, ScriptObject pageData)
    {
        if (pageData.ContainsKey("pages"))
        {
            return;
        }

        if (!page.Kind.Equals("home", StringComparison.OrdinalIgnoreCase)
            && !page.Kind.Equals("section", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var pagesValue = GetSharedPageList(page.Pages ?? []);
        pageData["pages"] = pagesValue;
        pageData["Pages"] = pagesValue;
    }

    /// <summary>
    /// 分类页数据对象（对齐 Hugo <c>.Data</c>）：
    /// <c>singular</c>/<c>plural</c>/<c>terms</c>（含 Alphabetical/ByCount）/<c>pages</c>。
    /// kind=taxonomy（terms.html）的 <c>pages</c> 为词条对象列表；
    /// kind=term（taxonomy.html）的 <c>pages</c> 为内容页列表。
    /// 非分类页返回 null（<c>.Data</c> 本身由调用方补空 map，
    /// <c>pages</c> 见 <see cref="AddListPageDataPages"/>）
    /// </summary>
    private static ScriptObject? BuildPageDataObject(FlintPageContext page)
    {
        var isTaxonomyList = page.Kind.Equals("taxonomy", StringComparison.OrdinalIgnoreCase);
        var isTerm = page.Kind.Equals("term", StringComparison.OrdinalIgnoreCase);
        if (!isTaxonomyList && !isTerm)
        {
            return null;
        }

        var data = new ScriptObject
        {
            ["singular"] = page.TaxonomySingular ?? page.TaxonomyName,
            ["plural"] = page.TaxonomyPlural ?? page.TaxonomyName,
            ["Singular"] = page.TaxonomySingular ?? page.TaxonomyName,
            ["Plural"] = page.TaxonomyPlural ?? page.TaxonomyName
        };

        // Terms：词条名 → 词条对象，另提供 Alphabetical / ByCount 排序视图
        var terms = page.Terms ?? [];
        var termsMap = new LazyTermsMap(terms);
        data["terms"] = termsMap;
        data["Terms"] = termsMap;

        // Pages：taxonomy 列表页给词条对象（Ananke terms.html 语义），
        // term 页给内容页集合（Ananke taxonomy.html 用 page.pages，此处同源）
        var pagesValue = isTaxonomyList
            ? terms.Select(t => (object)new LazyTermPage(t)).ToList()
            : (object?)GetSharedPageList(page.Pages ?? []);
        data["pages"] = pagesValue;
        data["Pages"] = pagesValue;

        return data;
    }

    /// <summary>
    /// 词条对象（Hugo 词条页视角）：title/rel_permalink/permalink/pages/count。
    /// terms.html 迭代 <c>Data.Pages</c> 时按此形状访问
    /// </summary>
    private sealed class LazyTermPage : ScriptObject
    {
        private readonly TaxonomyTerm _term;
        private LazyPageList? _lazyPages;

        public LazyTermPage(TaxonomyTerm term)
        {
            _term = term;
            // RelPermalink 为相对路径（对齐 Hugo .RelPermalink 语义）——主题把它
            // 放进 href 或与锚点拼接时，绝对 URL 会产出错误链接
            var rel = ToRelPermalink(term.Permalink);
            SetValue("title", term.Name, false);
            SetValue("name", term.Name, false);
            SetValue("rel_permalink", rel, false);
            SetValue("permalink", term.Permalink, false);
            SetValue("count", term.Count, false);
            SetValue("slug", term.Slug, false);

            SetValue("Title", term.Name, false);
            SetValue("Name", term.Name, false);
            SetValue("RelPermalink", rel, false);
            SetValue("Permalink", term.Permalink, false);
            SetValue("Count", term.Count, false);
            SetValue("Slug", term.Slug, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (member.Equals("pages", StringComparison.OrdinalIgnoreCase))
            {
                _lazyPages ??= GetSharedPageList(_term.Pages);
                value = _lazyPages;
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 词条映射（Hugo <c>.Data.Terms</c>）：按词条名取值，另有
    /// <c>Alphabetical</c>（按名序）与 <c>ByCount</c>（按数量降序）两个排序视图，
    /// 对齐 Hugo 文档的 taxonomy 用法
    /// </summary>
    private sealed class LazyTermsMap : ScriptObject, IFlintNonDataObject
    {
        private readonly IReadOnlyList<TaxonomyTerm> _terms;
        private List<object>? _alphabetical;
        private List<object>? _byCount;

        public LazyTermsMap(IReadOnlyList<TaxonomyTerm> terms)
        {
            _terms = terms;
            SetValue("count", terms.Count, false);
            SetValue("Count", terms.Count, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            // **去下划线归一**：迁移器把 `.ByCount` 归一成 `by_count`（带下划线），
            // 而 Scriban 标准成员是折叠形 `bycount`——两种拼写都要命中
            switch (member.ToLowerInvariant().Replace("_", ""))
            {
                case "alphabetical":
                    _alphabetical ??= _terms
                        .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(t => (object)new LazyTermPage(t))
                        .ToList();
                    value = _alphabetical;
                    return true;
                case "bycount":
                    _byCount ??= _terms
                        .OrderByDescending(t => t.Count)
                        .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(t => (object)new LazyTermPage(t))
                        .ToList();
                    value = _byCount;
                    return true;
            }

            // 词条名直接取值（.Data.Terms.foo）
            var match = _terms.FirstOrDefault(t =>
                t.Name.Equals(member, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                value = new LazyTermPage(match);
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    private static ScriptObject BuildSiteObject(FlintSiteContext site)
    {
        // 惰加载包装器跨渲染共享（见 SharedPageLists）；GetConvertedPages 内部有锁保证并发安全
        var lazyPages = GetSharedPageList(site.Pages);
        var lazyRegularPages = GetSharedPageList(site.RegularPages);
        var lazyTaxonomies = new LazyTaxonomies(site.Taxonomies);

        // 站点级暂存：同一 SiteContext 的所有页面共享（site 对象本身跨渲染复用缓存，
        // 故该 Store 在整次构建内被所有页面看到——与 Hugo .Site.Store 语义一致）
        var siteStore = new PageStoreObject();

        // .Site.MainSections / .Site.Params.mainSections：配置值优先，否则按 Hugo
        // 默认规则计算（见 DefaultMainSections）
        object siteMainSections = site.Params.TryGetValue("mainSections", out var msParam)
            ? msParam
            : DefaultMainSections(site);

        // 依赖跟踪站点对象：site.* 成员访问被记录为 data:site.* 依赖键（T4.1）
        // site.home：home 页的页面对象（Hugo 语义；主题用 .Site.Home.RelPermalink 等）。
        // 惰构建 + 共享缓存——无 home 页时返回 null（主题通常配合 ?. 或 with 使用）
        var homePage = site.Pages.FirstOrDefault(p =>
            string.Equals(p.Kind, "home", StringComparison.OrdinalIgnoreCase));
        var lazyHome = homePage is null ? null : (object)CreatePageObject(homePage);

        return new DependencyTrackingScriptObject
        {
            ["title"] = site.Title,
            ["base_url"] = site.BaseURL,
            ["home"] = lazyHome,
            ["Home"] = lazyHome,
            // .Site.GetPage（Hugo 路径/kind 查询）：主题用它取 section 页做导航
            // （Ananke/Stack 实测）。签名兼容两种形态：
            //   GetPage "/posts"            按路径
            //   GetPage "section" "posts"   按 kind + 名
            ["get_page"] = new GetPageFunction(site.Pages, site.Language),
            ["GetPage"] = new GetPageFunction(site.Pages, site.Language),
            // language 是**语言对象**（Hugo 的 .Site.Language.Locale/Lang 链——
            // narrow 的 baseof 取 `site.Language.Locale`；此前给纯字符串 → .locale 空
            // → <html lang="">）
            ["language"] = new ScriptObject
            {
                ["locale"] = site.Language ?? "",
                ["Locale"] = site.Language ?? "",
                ["lang"] = site.Language ?? "",
                ["Lang"] = site.Language ?? "",
                ["language_code"] = site.Language ?? "",
                ["LanguageCode"] = site.Language ?? "",
                ["language_name"] = site.Language ?? "",
                ["language_direction"] = "ltr"
            },
            ["pages"] = lazyPages,
            ["regular_pages"] = lazyRegularPages,
            ["taxonomies"] = lazyTaxonomies,
            ["menus"] = CreateMenusObject(site.Menus),
            ["paginator"] = BuildPaginatorObject(site),
            ["config"] = site.Config,
            ["data"] = site.Data,
            ["params"] = BuildParamsObjectWithMainSections(site, siteMainSections),
            ["build_date"] = site.BuildDate,
            ["last_change"] = site.LastChange,
            ["is_multilingual"] = site.IsMultiLingual,
            ["languages"] = site.Languages,

            // 站点级暂存（Hugo 0.113+ 的 .Site.Store / .Site.Scratch）：
            // 与页面级 Store 同型（Set/Get/Add/SetInMap/DeleteInMap/GetSortedMapValues）。
            // 此前只有页面有 Store，主题用 `site.Store.SetInMap "pagination" ...` 时
            // 报 "Cannot get the member site.store.setinmap for a null object"
            //（hugo-book / relearn / jane 等命中）
            ["store"] = siteStore,
            ["Store"] = siteStore,
            ["scratch"] = siteStore,
            ["Scratch"] = siteStore,

            // .Site.MainSections（Hugo 的站点方法）：配置值优先（[params] mainSections），
            // 否则按 Hugo 语义取常规页出现的 section 名。缺它时
            // `where … "Section" "in" site.main_sections` 过滤条件落空 →
            // 主题拿到空列表（FixIt 的 init/global.html：site.store 里的
            // mainSectionPages 为空 → footer 的 $pages.Prev 报错）
            ["main_sections"] = siteMainSections,
            ["MainSections"] = siteMainSections,
            ["mainsections"] = siteMainSections,

            // Hugo 兼容别名
            ["Title"] = site.Title,
            ["BaseURL"] = site.BaseURL,
            ["Language"] = site.Language,
            ["Pages"] = lazyPages,
            ["RegularPages"] = lazyRegularPages,
            ["Taxonomies"] = lazyTaxonomies,
            ["Menus"] = CreateMenusObject(site.Menus),
            ["Config"] = site.Config,
            ["Data"] = site.Data,
            ["Params"] = BuildParamsObject(site.Params),
            ["BuildDate"] = site.BuildDate,
            ["LastChange"] = site.LastChange,
            ["IsMultiLingual"] = site.IsMultiLingual,
            ["Languages"] = site.Languages,
        };
    }

    /// <summary>
    /// 参数对象（带 snake_case 别名）。
    /// </summary>
    /// <remarks>
    /// 配置里的参数键多为 camelCase（<c>[params] mainSections</c> / <c>headTitle</c>），
    /// 而 Hugo 主题里常按 snake_case 访问（<c>site.Params.main_sections</c>），
    /// 迁移工具也会把成员名归一成 snake_case。Scriban 的成员查找不区分大小写
    /// 但**不**忽略下划线（"main_sections" ≠ "mainSections"）→ 取空值。
    /// yinyang 实测：<c>site.params.main_sections</c> 为空使
    /// <c>where .Data.Pages "Type" "in" …</c> 的过滤条件失效
    /// （Hugo 侧同一表达式返回空集，Flint 侧却把全部词条页当命中）。
    /// 为含大写的键补 snake_case 别名；同名键已存在时不覆盖。
    /// </remarks>
    /// <summary>
    /// 参数对象 + <c>mainSections</c>：Hugo 在未显式配置时会把"常规页出现的 section"
    /// 算好写进 <c>site.Params.mainSections</c>，主题（papermod 的 home、FixIt 的
    /// 导航）据它过滤文章列表
    /// </summary>
    private static ScriptObject BuildParamsObjectWithMainSections(
        FlintSiteContext site, object mainSections)
    {
        var obj = BuildParamsObject(site.Params);
        if (!obj.ContainsKey("mainSections"))
        {
            obj["mainSections"] = mainSections;
        }
        if (!obj.ContainsKey("main_sections"))
        {
            obj["main_sections"] = mainSections;
        }
        return obj;
    }

    /// <summary>
    /// Hugo 的 <c>mainSections</c> 默认值（v0.166 探针实测）：**常规页最多的那个段**，
    /// 单元素列表；并列时取字典序最小的段名；一个段都没有（全是根级页）时为空列表。
    ///   posts 2 / docs 1 → posts      posts 2 / docs 2 → docs（并列取小）
    ///   posts 3 / docs 2 → posts      aaa 1 / zzz 1   → aaa
    ///   只有 /about/      → 空
    /// 配置里显式写了 <c>[params] mainSections</c> 则以配置为准（调用点处理）。
    /// </summary>
    private static List<string> DefaultMainSections(FlintSiteContext site)
    {
        var bySection = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in site.RegularPages)
        {
            if (!string.IsNullOrEmpty(page.Section))
            {
                bySection[page.Section] = bySection.TryGetValue(page.Section, out var n) ? n + 1 : 1;
            }
        }

        if (bySection.Count == 0)
        {
            return [];
        }

        var best = bySection
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .First()
            .Key;
        return [best];
    }

    private static ScriptObject BuildParamsObject(IReadOnlyDictionary<string, object>? source)
    {
        var obj = new ScriptObject();
        if (source is null)
        {
            return obj;
        }

        foreach (var kv in source)
        {
            obj[kv.Key] = WrapParamValue(kv.Value);
        }

        foreach (var kv in source)
        {
            var snake = ToSnakeCaseKey(kv.Key);
            if (!string.Equals(snake, kv.Key, StringComparison.Ordinal) && !obj.ContainsKey(snake))
            {
                obj[snake] = WrapParamValue(kv.Value);
            }
        }

        return obj;
    }

    /// <summary>
    /// 参数值**递归包装**：嵌套字典同样转 ScriptObject 并补 snake_case 别名。
    /// 迁移器把 `.Site.Params.dateFormat.published` 归一成 `site.params.date_format.published`，
    /// 只做顶层别名时**嵌套层取不到值**——stack 的
    /// <c>{{ .Date | time.Format .Site.Params.dateFormat.published }}</c> 因此回落默认格式
    ///（文本渲染成 `2026-01-15` 而非 `Thursday, January 15, 2026`，实测）。
    /// 列表逐元素递归（`[[params.menu]]` 这类数组里的字典同样要吃别名）
    /// </summary>
    private static object? WrapParamValue(object? value) => value switch
    {
        IReadOnlyDictionary<string, object> dict => BuildParamsObject(dict),
        IList<object> list => new ScriptArray(list.Select(WrapParamValue)),
        _ => value
    };

    /// <summary>camelCase/PascalCase 键 → snake_case（mainSections → main_sections）</summary>
    private static string ToSnakeCaseKey(string key)
    {
        var sb = new System.Text.StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var ch = key[i];
            if (char.IsUpper(ch) && i > 0 && key[i - 1] != '_')
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 懒加载页面列表包装器
    /// 只有在实际访问时才转换页面对象，避免 O(n²) 问题
    /// </summary>
    private static LazyPageList GetSharedPageList(IReadOnlyList<FlintPageContext> pages)
    {
        return SharedPageLists.GetValue(pages, static p => new LazyPageList(p));
    }

    /// <summary>
    /// **页面序列对象**：集合方法（<c>.ByDate</c>/<c>.Reverse</c>/<c>.Limit</c> 等）
    /// 结果的返回形态，与 <c>.Pages</c>/<c>site.regular_pages</c> 同型，
    /// 故结果上仍可继续调用方法族（Hugo 语义：页面集合的派生仍是页面集合）。
    /// </summary>
    /// <remarks>
    /// 早期这些方法返回裸 <c>ScriptArray</c>，丢掉了方法族——链式用法
    /// <c>.ByLastmod.Reverse</c> 静默得到 null（FixIt RSS 实测：
    /// <c>(index $pages.ByLastmod.Reverse 0).LastMod</c> 报 "for a null object"；
    /// 根因是 Scriban 在成员链中会**自动调用**零必填参数的函数成员，
    /// 返回的裸数组上没有 <c>reverse</c> 成员）。
    /// </remarks>
    internal static ScriptObject SharedPageSequence(IReadOnlyList<FlintPageContext> pages) =>
        GetSharedPageList(pages);

    /// <summary>
    /// Hugo Pages 方法族的**三种拼写**：PascalCase（Hugo 文档与模板的原始写法
    /// <c>.ByDate</c>）、折叠形（转换器一条归一化路径产出 <c>bydate</c>）、
    /// 下划线形（另一条路径产出 <c>by_date</c>）。
    /// </summary>
    /// <remarks>
    /// 两种差异都要覆盖：
    /// 1. **不忽略下划线**——`by_date` 与 `bydate` 是两个不同的键；
    /// 2. **区分大小写**——Scriban 的 ScriptObject 成员字典按 Ordinal 比较，
    ///    `SetValue("bydate")` 后访问 `.ByDate` 取到 null（实测：`site.pages.ByDate`
    ///    返回空而 `site.pages.bydate` 正常）。此前只注册折叠形，主题写 Hugo 原始
    ///    PascalCase 时**静默拿到空集合**（FixIt 的 footer.html `$pages.Prev`
    ///    报 "The function `$pages.Prev` was not found"）。
    /// 既有先例：LazyPageList 早就为计数注册了 count/Count/length/Length 等变体，
    /// 这里把同一做法推广到整个方法族
    /// </remarks>
    private static readonly (string Pascal, string Collapsed, string Snake)[] PageMethodSpellings =
    [
        ("ByDate", "bydate", "by_date"),
        ("ByPublishDate", "bypublishdate", "by_publish_date"),
        ("ByExpiryDate", "byexpirydate", "by_expiry_date"),
        ("ByLastmod", "bylastmod", "by_lastmod"),
        ("ByTitle", "bytitle", "by_title"),
        ("ByLinkTitle", "bylinktitle", "by_link_title"),
        ("ByWeight", "byweight", "by_weight"),
        ("ByLength", "bylength", "by_length"),
        ("ByParam", "byparam", "by_param"),
        ("GroupBy", "groupby", "group_by"),
        ("GroupByDate", "groupbydate", "group_by_date"),
        ("GroupByPublishDate", "groupbypublishdate", "group_by_publish_date"),
        ("IndexOf", "indexof", "index_of"),
        ("Prev", "prev", "prev_in_section"),
        ("Next", "next", "next_in_section"),
        ("Related", "related", "related"),
        ("Reverse", "reverse", "reverse"),
        ("Limit", "limit", "limit")
    ];

    private sealed class LazyPageList : ScriptObject, IEnumerable<ScriptObject>, IList<ScriptObject>
    {
        private readonly IReadOnlyList<FlintPageContext> _pages;
        private List<ScriptObject>? _convertedPages;

        public LazyPageList(IReadOnlyList<FlintPageContext> pages)
        {
            _pages = pages;
            // 集合方法（Hugo Pages 方法族）：主题在集合上调用，如
            // {{ range .Pages.ByDate }} / {{ .Site.RegularPages.Related . }} / {{ first 5 .Pages }}
            // 集合方法（Hugo Pages 方法族）：主题在集合上调用，如
            // {{ range .Pages.ByDate }} / {{ .Site.RegularPages.Related . }}。
            // Scriban 成员查找不区分大小写，单一注册即可覆盖 ByDate/by_date 两种写法
            SetValue("bydate", new PagesByDateFunction(pages), false);
            SetValue("bytitle", new PagesByTitleFunction(pages), false);
            SetValue("byweight", new PagesByWeightFunction(pages), false);
            SetValue("bylength", new PagesByLengthFunction(pages), false);
            SetValue("bylastmod", new PagesByLastmodFunction(pages), false);
            SetValue("bypublishdate", new PagesByDateFunction(pages), false);
            SetValue("byexpirydate", new PagesByDateFunction(pages), false);
            SetValue("byparam", new PagesByParamFunction(pages), false);
            SetValue("bylinktitle", new PagesByTitleFunction(pages), false);
            SetValue("related", new PagesRelatedFunction(pages), false);
            SetValue("reverse", new PagesReverseFunction(pages), false);
            SetValue("limit", new PagesLimitFunction(pages), false);
            SetValue("groupby", new PagesGroupByFunction(pages), false);
            SetValue("groupbydate", new PagesGroupByDateFunction(pages), false);
            // Hugo 方法名的下划线拼写别名：转换器对链式方法名有两条归一化路径，
            // 一条产出折叠形（groupbydate）、一条产出下划线形（group_by_publish_date，
            // monochrome 的 list.html 实测 "The function `page?.pages?.group_by_publish_date`
            // was not found" 20 处）。Scriban 成员查找不区分大小写但**不**忽略下划线，
            // 故两种拼写都要注册。
            // 这里**系统化**补齐：遍历已注册成员，为折叠形补 snake_case 形态
            //（bypublishdate → by_publish_date）——手写别名必漏，而漏掉的形态不报错、
            //  只是静默返回空（monochrome 的 _partials/list.html
            //  `$pages.by_publish_date?.reverse` 实测：整段列表被静默清空）
            foreach (var key in Keys.ToList())
            {
                if (key.Length == 0)
                {
                    continue;
                }
                var snake = ToSnakeCaseKey(key);
                if (!string.Equals(snake, key, StringComparison.Ordinal) && !ContainsKey(snake))
                {
                    SetValue(snake, this[key], false);
                }
            }
            SetValue("groupbypublishdate", new PagesGroupByDateFunction(pages), false);
            SetValue("group_by_publish_date", new PagesGroupByDateFunction(pages), false);
            SetValue("indexof", new PagesIndexOfFunction(pages), false);
            SetValue("next", new PagesNextPrevFunction(pages, forward: true), false);
            SetValue("prev", new PagesNextPrevFunction(pages, forward: false), false);
            // 设置 count/length 属性，这些是常用的且不需要转换所有页面
            SetValue("count", pages.Count, false);
            SetValue("length", pages.Count, false);
            SetValue("size", pages.Count, false);
            SetValue("len", pages.Count, false);
            SetValue("Count", pages.Count, false);
            SetValue("Length", pages.Count, false);
            SetValue("Len", pages.Count, false);

            // **拼写别名放在构造函数末尾**：必须在本类全部注册之后运行，
            // 否则后段注册的成员拿不到别名（`prev`/`next`/`indexof` 在 groupby 之后注册，
            // FixIt 的 `$pages.Prev page` 因此报 "The function `$pages.Prev` was not found"）。
            // 另注：`prev`/`next` 的 snake 形态（prev_in_section/next_in_section）在 Hugo 里
            // 是**页面方法**（无参，返回相邻页），与集合方法同形不同义——故这两条只注册
            // PascalCase 别名，不注册 snake 形态，避免把页面方法名指向集合函数
            foreach (var (pascal, collapsed, snake) in PageMethodSpellings)
            {
                if (!ContainsKey(collapsed))
                {
                    continue;
                }
                var spellingValue = this[collapsed];
                if (!ContainsKey(pascal))
                {
                    SetValue(pascal, spellingValue, false);
                }
                if (!string.Equals(snake, collapsed, StringComparison.Ordinal) &&
                    !snake.Contains("_in_section", StringComparison.Ordinal) &&
                    !ContainsKey(snake))
                {
                    SetValue(snake, spellingValue, false);
                }
            }
        }

        // IList<ScriptObject> 实现 - 用于 Scriban 的 len 过滤器
        int ICollection<ScriptObject>.Count => _pages.Count;
        bool ICollection<ScriptObject>.IsReadOnly => true;

        public ScriptObject this[int index]
        {
            get => GetConvertedPages()[index];
            set => throw new NotSupportedException();
        }

        public int IndexOf(ScriptObject item) => GetConvertedPages().IndexOf(item);
        public bool Contains(ScriptObject item) => GetConvertedPages().Contains(item);
        public void CopyTo(ScriptObject[] array, int arrayIndex) => GetConvertedPages().CopyTo(array, arrayIndex);
        void ICollection<ScriptObject>.Add(ScriptObject item) => throw new NotSupportedException();
        void ICollection<ScriptObject>.Clear() => throw new NotSupportedException();
        bool ICollection<ScriptObject>.Remove(ScriptObject item) => throw new NotSupportedException();
        public void Insert(int index, ScriptObject item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();

        private readonly object _conversionLock = new();

        private List<ScriptObject> GetConvertedPages()
        {
            // 跨渲染共享后的并发保护：并行页面渲染同时枚举同一实例
            if (_convertedPages is not null)
            {
                return _convertedPages;
            }

            lock (_conversionLock)
            {
                return _convertedPages ??= _pages.Select(p => CreatePageObject(p)).ToList();
            }
        }

        public new IEnumerator<ScriptObject> GetEnumerator()
        {
            return GetConvertedPages().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetConvertedPages().GetEnumerator();
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            // 处理索引访问
            if (int.TryParse(member, out var index) && index >= 0 && index < _pages.Count)
            {
                value = GetConvertedPages()[index];
                return true;
            }

            // 处理 first/last 等常用属性
            switch (member.ToLowerInvariant())
            {
                case "first":
                    value = _pages.Count > 0 ? CreatePageObject(_pages[0]) : null;
                    return true;
                case "last":
                    value = _pages.Count > 0 ? CreatePageObject(_pages[^1]) : null;
                    return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 懒加载分类集合包装器
    /// </summary>
    private sealed class LazyTaxonomies : ScriptObject, IFlintNonDataObject
    {
        private readonly FlintTaxonomyCollection _taxonomies;
        private readonly Dictionary<string, object> _convertedTaxonomies = new();

        public LazyTaxonomies(FlintTaxonomyCollection taxonomies)
        {
            _taxonomies = taxonomies;
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (_convertedTaxonomies.TryGetValue(member, out var cached))
            {
                value = cached;
                return true;
            }

            if (_taxonomies.Taxonomies.TryGetValue(member, out var terms))
            {
                // 使用懒加载的术语列表
                var lazyTerms = terms.Select(t => new LazyTaxonomyTerm(t)).ToList();
                _convertedTaxonomies[member] = lazyTerms;
                value = lazyTerms;
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 懒加载分类术语包装器
    /// </summary>
    private sealed class LazyTaxonomyTerm : ScriptObject, IFlintNonDataObject
    {
        private readonly TaxonomyTerm _term;
        private LazyPageList? _lazyPages;

        public LazyTaxonomyTerm(TaxonomyTerm term)
        {
            _term = term;
            // 设置基本属性（不需要转换页面）
            SetValue("name", term.Name, false);
            SetValue("slug", term.Slug, false);
            SetValue("count", term.Count, false);
            SetValue("permalink", term.Permalink, false);

            // Hugo 兼容
            SetValue("Name", term.Name, false);
            SetValue("Slug", term.Slug, false);
            SetValue("Count", term.Count, false);
            SetValue("Permalink", term.Permalink, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            // 只有访问 pages/Pages 时才懒加载
            if (member.Equals("pages", StringComparison.OrdinalIgnoreCase))
            {
                _lazyPages ??= new LazyPageList(_term.Pages);
                value = _lazyPages;
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }


    // CreateTaxonomiesObject 已被 LazyTaxonomies 替代，不再需要

    /// <summary>
    /// Hugo .File 对象（B4）：从页面源路径派生 path/dirname/basename/content_base_name/
    /// filename/extension/unique_id/translation_base_name。无源路径（合成页）返回空对象
    /// </summary>
    private static ScriptObject BuildFileObject(string? sourcePath)
    {
        var file = new ScriptObject();
        if (string.IsNullOrEmpty(sourcePath))
        {
            file["path"] = "";
            file["Path"] = "";
            return file;
        }

        var normalized = sourcePath.Replace('\\', '/');
        // Hugo .File.Path 相对 content/ 目录（无前导斜杠）
        var contentIdx = normalized.LastIndexOf("/content/", StringComparison.OrdinalIgnoreCase);
        var relPath = contentIdx >= 0
            ? normalized[(contentIdx + "/content/".Length)..]
            : normalized.TrimStart('/');
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        // index/_index 的 ContentBaseName 取父目录名（Hugo 语义）
        var contentBaseName = baseName is "index" or "_index"
            ? Path.GetFileName(Path.GetDirectoryName(sourcePath) ?? "") ?? ""
            : baseName;

        var values = new (string Key, object Value)[]
        {
            ("path", relPath),
            ("dirname", relPath.Contains('/') ? relPath[..relPath.LastIndexOf('/')] : ""),
            ("basename", baseName),
            ("content_base_name", contentBaseName),
            ("filename", Path.GetFileName(sourcePath)),
            ("extension", Path.GetExtension(sourcePath).TrimStart('.')),
            ("unique_id", relPath)
        };
        foreach (var (key, value) in values)
        {
            file[key] = value;
            file[char.ToUpperInvariant(key[0]) + key[1..]] = value; // Hugo 大写别名
        }
        return file;
    }

    private static ScriptObject CreateMenusObject(FlintMenuCollection menus)
    {
        var obj = new ScriptObject();
        foreach (var (name, items) in menus.Menus)
        {
            obj[name] = new MenuItemsList(items.Select(CreateMenuItemObject).ToList());
        }
        return obj;
    }

    /// <summary>
    /// 菜单项列表：可迭代（保持配置序）+ <c>byweight</c>/<c>by_weight</c> 成员。
    /// Hugo 菜单方法族 <c>.Site.Menus.main.ByWeight</c> ——techdoc 的 global-menu
    /// 实测：菜单列表是普通 List 时 <c>.by_weight</c> 取空 → 整个站点菜单不渲染
    /// </summary>
    private sealed class MenuItemsList : ScriptObject, IEnumerable<ScriptObject>
    {
        private readonly ScriptArray _byWeight;
        private readonly List<ScriptObject> _items;

        public MenuItemsList(List<ScriptObject> items)
        {
            _items = items;
            _byWeight = new ScriptArray(
                items.OrderBy(i => i["weight"] is int w ? w : 0).ToList());
            // 索引成员注册条目：ScriptObject 空成员时 Flint 真值判定为假，
            // `{{ if site.menus.main }}` 守卫会整块跳过（techdoc 菜单实测）
            for (var i = 0; i < items.Count; i++)
            {
                this[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = items[i];
            }
        }

        public new IEnumerator<ScriptObject> GetEnumerator() => _items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (member is "byweight" or "by_weight" or "ByWeight")
            {
                value = _byWeight;
                return true;
            }
            if (member is "count" or "length" or "Count" or "Length")
            {
                value = _items.Count;
                return true;
            }
            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 站点级分页对象（无逐页绑定时回落，保持既有行为）：
    /// pages = 首版首页切片，total_pages/page_number/has_prev/has_next
    /// </summary>
    /// <summary>
    /// 站点级分页器（<c>site.paginator</c> 与内置分页模板的全局 <c>paginator</c>）。
    /// **与页面级同型**：内置 pagination 模板会读 <c>paginator.pagers[i]</c>，
    /// 少注册 members 会让它报 "Object `paginator.pagers` is null"
    ///（ananke 的 home/list 经内置分页模板实测）
    /// </summary>
    /// <summary>
    /// 站点级分页器（<c>site.paginator</c> 与内置分页模板使用的全局 <c>paginator</c>）。
    /// **与页面级同型**：内置 pagination 模板会读 <c>paginator.pagers[i]</c>，
    /// 少注册成员会报 "Object `paginator.pagers` is null"（ananke 的 home/list 实测）
    /// </summary>
    private static ScriptObject BuildPaginatorObject(FlintSiteContext site)
    {
        return BuildPaginatorObjectCore(new PaginatorView
        {
            AllItems = site.PaginatorPages,
            PageNumber = Math.Max(1, site.PaginatorPageNumber),
            PageSize = Math.Max(1, site.Config.Paginate),
            BaseRelPermalink = "/",
            PaginatePath = site.Config.PaginatePath,
        });
    }
    // 逐页绑定的分页对象复用缓存：同一 PaginatorView（一个列表页的所有 pager 共享）
    // 跨渲染/跨 site.paginator 与 page.paginator 只构建一次；CWT 键为视图引用，
    // 随构建周期回收（与 SharedPageObjects 同一模型）
    private static readonly ConditionalWeakTable<PaginatorView, ScriptObject> SharedPaginatorObjects = new();

    /// <summary>分组分页的 <c>page_groups</c>：每组 { key, pages }（页面集合同型，可继续用方法族）</summary>
    private static ScriptArray BuildPageGroups(PaginatorView view)
    {
        var arr = new ScriptArray();
        foreach (var group in view.PageGroups)
        {
            arr.Add(new ScriptObject
            {
                ["key"] = group.Key,
                ["Key"] = group.Key,
                ["pages"] = GetSharedPageList(group.Pages),
                ["Pages"] = GetSharedPageList(group.Pages)
            });
        }

        return arr;
    }

    private static ScriptObject BuildPaginatorObject(PaginatorView view)
    {
        return SharedPaginatorObjects.GetValue(view, static v => BuildPaginatorObjectCore(v));
    }

    /// <summary>
    /// 分页器对象（C1，对齐 Hugo Pager 字段子集）：
    /// pages/page_number/total_pages/pager_size/number_of_elements/total_number_of_elements/
    /// has_prev/has_next/is_first/is_last/url，以及 first/last/prev/next/pagers（均 pager 对象）。
    /// pagers 为惰性列表——模板未访问时不物化（大站点避免 O(N) 分页对象构造）
    /// </summary>
    private static ScriptObject BuildPaginatorObjectCore(PaginatorView view)
    {
        var so = new ScriptObject();
        var pages = GetSharedPageList(view.Pages);
        so["pages"] = pages;
        so["page_number"] = view.PageNumber;
        so["total_pages"] = view.TotalPages;
        so["pager_size"] = view.PageSize;
        so["number_of_elements"] = view.NumberOfElements;
        so["total_number_of_elements"] = view.TotalItems;
        so["has_prev"] = view.HasPrev;
        so["has_next"] = view.HasNext;
        so["is_first"] = view.IsFirst;
        so["is_last"] = view.IsLast;
        so["url"] = view.URL;
        so["first"] = new PagerObject(view.First);
        so["last"] = new PagerObject(view.Last);
        so["prev"] = view.Prev is not null ? new PagerObject(view.Prev) : null;
        so["next"] = view.Next is not null ? new PagerObject(view.Next) : null;
        so["pagers"] = new LazyPagers(view.Pagers);
        // **PageGroups**（Hugo 的 `.Paginate (.Pages.GroupByDate "2006")` 形态）：
        // 每个组是 { key, pages } 对象；非分组分页为空列表（探针：`len .PageGroups` = 0）
        so["page_groups"] = BuildPageGroups(view);

        // Hugo 兼容别名（PascalCase）：Hugo 的 Pager 字段是 Pascal（.TotalPages/
        // .HasPrev/.Prev.URL），而 Scriban 成员查找大小写敏感——只注册 snake
        // 会使模板的 `$pag.HasPrev`/`$pag.Prev.URL` 取到 null（实测 mini 主题）
        foreach (var (snake, pascal) in new (string, string)[]
                 {
                     ("pages", "Pages"), ("page_number", "PageNumber"),
                     ("total_pages", "TotalPages"), ("pager_size", "PagerSize"),
                     ("number_of_elements", "NumberOfElements"),
                     ("total_number_of_elements", "TotalNumberOfElements"),
                     ("has_prev", "HasPrev"), ("has_next", "HasNext"),
                     ("is_first", "IsFirst"), ("is_last", "IsLast"),
                     ("url", "URL"), ("first", "First"), ("last", "Last"),
                     ("prev", "Prev"), ("next", "Next"), ("pagers", "Pagers"),
                     ("page_groups", "PageGroups")
                 })
        {
            if (so.ContainsKey(snake) && !so.ContainsKey(pascal))
            {
                so[pascal] = so[snake];
            }
        }

        // Hugo 兼容大写别名
        so["Pages"] = pages;
        so["PageNumber"] = view.PageNumber;
        so["TotalPages"] = view.TotalPages;
        so["PagerSize"] = view.PageSize;
        so["NumberOfElements"] = view.NumberOfElements;
        so["TotalNumberOfElements"] = view.TotalItems;
        so["HasPrev"] = view.HasPrev;
        so["HasNext"] = view.HasNext;
        so["URL"] = view.URL;
        return so;
    }

    /// <summary>
    /// 单个 pager 对象：标量元数据即时绑定，pages 切片惰性求值
    /// （视图共享，切片本身在 PaginatorView 内已缓存）
    /// </summary>
    private sealed class PagerObject : ScriptObject, IFlintNonDataObject
    {
        private readonly PaginatorView _pager;
        private LazyPageList? _pages;

        public PagerObject(PaginatorView pager)
        {
            _pager = pager;
            var url = pager.URL;
            SetValue("url", url, false);
            SetValue("page_number", pager.PageNumber, false);
            SetValue("total_pages", pager.TotalPages, false);
            SetValue("pager_size", pager.PageSize, false);
            SetValue("has_prev", pager.HasPrev, false);
            SetValue("has_next", pager.HasNext, false);
            SetValue("is_first", pager.IsFirst, false);
            SetValue("is_last", pager.IsLast, false);

            SetValue("URL", url, false);
            SetValue("PageNumber", pager.PageNumber, false);
            SetValue("TotalPages", pager.TotalPages, false);
            SetValue("PagerSize", pager.PageSize, false);
            SetValue("HasPrev", pager.HasPrev, false);
            SetValue("HasNext", pager.HasNext, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (member is "pages" or "Pages")
            {
                _pages ??= GetSharedPageList(_pager.Pages);
                value = _pages;
                return true;
            }
            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 惰性 pager 列表（脚本对象包装）：模板访问 pagers 时才把视图列表转成对象列表，
    /// 未访问零开销。IList&lt;ScriptObject&gt; 实现供 Scriban 的索引/len/size 消费
    /// （与 LazyPageList 同一模式）
    /// </summary>
    private sealed class LazyPagers : ScriptObject, IEnumerable<ScriptObject>, IList<ScriptObject>
    {
        private readonly IReadOnlyList<PaginatorView> _pagers;
        private List<ScriptObject>? _items;
        private readonly object _lock = new();

        public LazyPagers(IReadOnlyList<PaginatorView> pagers)
        {
            _pagers = pagers;
            SetValue("count", pagers.Count, false);
            SetValue("length", pagers.Count, false);
            SetValue("size", pagers.Count, false);
            SetValue("Count", pagers.Count, false);
            SetValue("Length", pagers.Count, false);
        }

        int ICollection<ScriptObject>.Count => _pagers.Count;
        bool ICollection<ScriptObject>.IsReadOnly => true;

        public ScriptObject this[int index]
        {
            get => GetItems()[index];
            set => throw new NotSupportedException();
        }

        public int IndexOf(ScriptObject item) => GetItems().IndexOf(item);
        public bool Contains(ScriptObject item) => GetItems().Contains(item);
        public void CopyTo(ScriptObject[] array, int arrayIndex) => GetItems().CopyTo(array, arrayIndex);
        void ICollection<ScriptObject>.Add(ScriptObject item) => throw new NotSupportedException();
        void ICollection<ScriptObject>.Clear() => throw new NotSupportedException();
        bool ICollection<ScriptObject>.Remove(ScriptObject item) => throw new NotSupportedException();
        public void Insert(int index, ScriptObject item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();

        private List<ScriptObject> GetItems()
        {
            if (_items is not null)
            {
                return _items;
            }
            lock (_lock)
            {
                return _items ??= _pagers.Select(p => (ScriptObject)new PagerObject(p)).ToList();
            }
        }

        public new IEnumerator<ScriptObject> GetEnumerator() => GetItems().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetItems().GetEnumerator();

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (int.TryParse(member, out var index) && index >= 0 && index < _pagers.Count)
            {
                value = GetItems()[index];
                return true;
            }
            switch (member.ToLowerInvariant())
            {
                case "first":
                    value = _pagers.Count > 0 ? GetItems()[0] : null;
                    return true;
                case "last":
                    value = _pagers.Count > 0 ? GetItems()[^1] : null;
                    return true;
            }
            return base.TryGetValue(context, span, member, out value);
        }
    }

    private static ScriptObject CreateMenuItemObject(FlintMenuItem item)
    {
        return new ScriptObject
        {
            ["name"] = item.Name,
            ["url"] = item.URL,
            ["weight"] = item.Weight,
            ["identifier"] = item.Identifier,
            ["parent"] = item.Parent,
            ["pre"] = item.Pre,
            ["post"] = item.Post,
            ["children"] = item.Children.Select(CreateMenuItemObject).ToList(),
            ["has_children"] = item.HasChildren,
            ["is_active"] = item.IsActive,

            // Hugo 兼容
            ["Name"] = item.Name,
            ["URL"] = item.URL,
            ["Weight"] = item.Weight,
            ["Identifier"] = item.Identifier,
            ["Parent"] = item.Parent,
            ["Pre"] = item.Pre,
            ["Post"] = item.Post,
            ["Children"] = item.Children.Select(CreateMenuItemObject).ToList(),
            ["HasChildren"] = item.HasChildren,
            ["IsActive"] = item.IsActive,
        };
    }

    /// <summary>
    /// 注册自定义日期对象，支持 DateTimeOffset 类型
    /// 覆盖 Scriban 内置的 date 对象
    /// </summary>
    private static void RegisterDateObject(ScriptObject dateObject)
    {
        // Scriban 7 起 Import 标注 RequiresUnreferencedCode（反射创建
        // DynamicCustomFunction）；Import 目标是本方法显式引用的 lambda，
        // linker 不会裁剪被引用成员——运行时安全，方法级压制
#pragma warning disable IL2026
        // to_string - 格式化日期，支持 DateTimeOffset
        // 形参用 params：Hugo 的 time.Format/dateFormat 在部分应用形态下只给格式串
        //（`{{ date.to_string $config }}`），严格双参形参会报
        // "Invalid number of arguments 1 ... expecting 2"（doit 21 处实测）
        dateObject.Import("to_string", (params object?[] a) =>
        {
            var date = a.Length > 0 ? a[0] : null;
            var format = a.Length > 1 ? a[1]?.ToString() : null;
            var dt = ConvertToDateTimeOffset(date);
            return dt == null ? "" : FormatHugoDate(dt.Value, format);
        });

        // is_zero：Hugo 的 `.Date.IsZero`（无日期页的 .Date 是**零值时间** 0001-01-01T00:00:00Z，
        // 主题用它守卫日期/opengraph 的渲染——ananke 的 ShowDate、console/fixit 的 head 实测）。
        // 迁移器把 `.IsZero` 改写成 `date.is_zero <接收者>`（值成员在 Scriban 侧取不到）
        dateObject.Import("is_zero", (object? v) =>
        {
            var dt = ConvertToDateTimeOffset(v);
            return dt is null || dt.Value.UtcDateTime == DateTime.MinValue;
        });

        // unix：Hugo 的 `.Date.Unix`（秒级时间戳）
        dateObject.Import("unix", (object? v) =>
            ConvertToDateTimeOffset(v)?.ToUnixTimeSeconds() ?? 0);

        // now - 当前时间
        dateObject.Import("now", () => DateTimeOffset.Now);

        // parse - 解析日期字符串
        dateObject.Import("parse", (string? s) =>
        {
            if (DateTimeOffset.TryParse(s, out var result))
                return result;
            return DateTimeOffset.MinValue;
        });

        // add_days - 添加天数
        dateObject.Import("add_days", (object? date, int days) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            return dt?.AddDays(days);
        });

        // add_months - 添加月数
        dateObject.Import("add_months", (object? date, int months) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            return dt?.AddMonths(months);
        });

        // add_years - 添加年数
        dateObject.Import("add_years", (object? date, int years) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            return dt?.AddYears(years);
        });
#pragma warning restore IL2026
    }

    /// <summary>
    /// 将各种日期类型转换为 DateTimeOffset
    /// </summary>
    /// <summary>
    /// Hugo 的具名日期格式（<c>time.Format ":date_medium"</c> 等）→ .NET 格式串。
    /// 期望值来自 Hugo v0.166 探针（2026-03-10T15:04:05Z）：
    /// <c>:date_full</c> → "Tuesday, March 10, 2026"、<c>:date_long</c> → "March 10, 2026"、
    /// <c>:date_medium</c> → "Mar 10, 2026"、<c>:date_short</c> → "3/10/26"、
    /// <c>:time_medium</c> → "3:04:05 pm"、<c>:time_short</c> → "3:04 pm"
    /// </summary>
    /// <summary>
    /// 按 Hugo 的日期格式语义渲染：支持**具名格式**（<c>:date_medium</c> 等，Hugo v0.166
    /// 探针值）与 **Go 布局串**（<c>2006-01-02</c>/<c>January 2, 2006</c>），其余按 .NET
    /// 自定义格式串处理。<c>date.to_string</c>（迁移器产物）与 <c>time.format</c>
    /// （Hugo 函数，运行期布局）共用，避免两处漂移
    /// </summary>
    internal static string FormatHugoDate(DateTimeOffset dt, string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return dt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        // **Hugo 的具名日期格式**（`:date_medium` 等，v0.166 探针实测输出）：
        // 不映射的话 `time.Format ":date_medium"` 会被当作 .NET 自定义格式串解析，
        // 产出 `10aAe_0e10lu0` 这类乱码（hugo-paper 的 `<time>` 实测）
        if (NamedDateFormats.TryGetValue(format, out var named))
        {
            // Go 的 `pm` 是小写（"3:04:05 pm"），.NET 的 `tt` 给 "PM"
            // → 具名格式统一转小写（只作用于具名表，不动用户自定义格式串）
            return dt.ToString(named, System.Globalization.CultureInfo.InvariantCulture)
                .Replace("AM", "am", StringComparison.Ordinal)
                .Replace("PM", "pm", StringComparison.Ordinal);
        }

        // **运行期拿到的 Go 布局串也要转**：Hugo 主题常把布局放在表达式里
        //（ananke 的 `time.Format (compare.Default "January 2, 2006" .Site.Params.date_format)`
        // → 迁移产物 `date.to_string page.date (default site.params.date_format "January 2, 2006")`），
        // 迁移期只能转字面量，跑起来时布局是字符串变量。不转则按 .NET 自定义格式解析，
        // 输出 `Januar26 2, 2006` 这类乱码（6 个主题的 <time> 文本实测）。
        // 判据用 Go 特征 token，已转换过的 .NET 串不会被二次转换
        var rawFormat = format;
        if (GoDateFormat.LooksLikeGoLayout(format) || format.Contains("MST", StringComparison.Ordinal))
        {
            format = GoDateFormat.Convert(format);
        }

        var result = dt.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

        // 时区缩写：占位标记换成真实缩写（探针：`… MST` → "UTC"；非零偏移用 +0800 形态，
        // 与 Go 对固定偏移区的打印一致）
        if (result.Contains(GoDateFormat.ZoneAbbrevMarker, StringComparison.Ordinal))
        {
            result = result.Replace(
                GoDateFormat.ZoneAbbrevMarker,
                dt.Offset == TimeSpan.Zero
                    ? "UTC"
                    : (dt.Offset < TimeSpan.Zero ? "-" : "+") +
                      dt.Offset.Duration().ToString("hhmm", System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        // Go 的 Z0700：UTC → 字面 "Z"、非 UTC → 无冒号偏移
        //（探针：blog-awesome 的 2006-01-02T15:04:05Z0700 → 2026-03-10T00:00:00Z）
        if (rawFormat.Contains("Z0700", StringComparison.Ordinal) &&
            result.Contains(GoDateFormat.Z0700Marker, StringComparison.Ordinal))
        {
            result = result.Replace(
                GoDateFormat.Z0700Marker,
                dt.Offset == TimeSpan.Zero
                    ? "Z"
                    : (dt.Offset < TimeSpan.Zero ? "-" : "+") +
                      dt.Offset.Duration().ToString("hhmm", System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        // **不带冒号的数字偏移**（Go 的 `-0700`/`Z0700`，如 RFC1123Z：探针 → "+0000"）：
        // .NET 的 zzz 恒带冒号，故格式化后去掉
        if (rawFormat.Contains("-0700", StringComparison.Ordinal) ||
            rawFormat.Contains("Z0700", StringComparison.Ordinal))
        {
            result = System.Text.RegularExpressions.Regex.Replace(result, @"([+-]\d{2}):(\d{2})", "$1$2");
        }

        return result;
    }

    private static readonly Dictionary<string, string> NamedDateFormats = new(StringComparer.Ordinal)
    {
        [":date_full"] = "dddd, MMMM d, yyyy",
        [":date_long"] = "MMMM d, yyyy",
        [":date_medium"] = "MMM d, yyyy",
        [":date_short"] = "M/d/yy",
        [":time_full"] = "h:mm:ss tt zzz",
        [":time_medium"] = "h:mm:ss tt",
        [":time_short"] = "h:mm tt"
    };

    private static DateTimeOffset? ConvertToDateTimeOffset(object? value)
    {
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            string s when DateTimeOffset.TryParse(s, out var result) => result,
            long unix => DateTimeOffset.FromUnixTimeSeconds(unix),
            _ => null
        };
    }
}
